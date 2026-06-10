using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceFlushCoordinator
    {
        private const float AckTimeoutSeconds = 10f;
        private const float LogoutFlushTimeoutSeconds = 60f;
        private const float QuitFlushTimeoutSeconds = 60f;
        private const int MaxFlushRowsPerBatch = 5000;

        private static readonly object Sync = new();
        private static readonly Queue<string> PendingFiles = new();
        private static string? _currentPath;
        private static string _currentFileId = "";
        private static int _nextStartLine;
        private static int _batchIndex;
        private static bool _waitingForAck;
        private static bool _lastBatchSent;
        private static bool _flushRequested;
        private static string _flushReason = "";
        private static double _ackDeadlineRealtime;
        private static bool _allowLogout;
        private static bool _delayingLogout;
        private static bool _allowQuit;
        private static bool _delayingQuit;
        private static bool _allowMenuQuit;
        private static bool _delayingMenuQuit;

        internal static void Initialize()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            Application.wantsToQuit += OnWantsToQuit;
        }

        internal static void Shutdown()
        {
            Application.wantsToQuit -= OnWantsToQuit;
        }

        internal static void RequestFlush(string reason)
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return;

            lock (Sync)
            {
                _flushRequested = true;
                _flushReason = reason;
            }
        }

        internal static void Update()
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return;

            lock (Sync)
            {
                if (!_flushRequested && PendingFiles.Count == 0 && _currentPath == null)
                    return;
            }

            if (!CanUpload())
                return;

            PrepareFilesIfNeeded();
            SendNextBatchIfReady();
        }

        internal static void OnBatchAck(long sender, ZPackage package)
        {
            try
            {
                int protocolVersion = package.ReadInt();
                string fileId = package.ReadString();
                int batchIndex = package.ReadInt();
                bool accepted = package.ReadBool();
                bool finalBatch = package.ReadBool();
                int receivedRows = package.ReadInt();

                if (protocolVersion != RpcTraceTelemetry.ProtocolVersion)
                    return;

                lock (Sync)
                {
                    if (!_waitingForAck || fileId != _currentFileId || batchIndex != _batchIndex)
                        return;

                    if (!accepted)
                    {
                        PraetorisClientPlugin.Log.LogWarning($"Server rejected RPC trace batch {batchIndex} for {fileId}; keeping local file.");
                        ResetCurrentUploadLocked(retryLater: true);
                        return;
                    }

                    _waitingForAck = false;
                    _nextStartLine += Math.Max(0, receivedRows);

                    if (finalBatch && _lastBatchSent)
                    {
                        string uploadedPath = _currentPath ?? "";
                        PraetorisClientPlugin.Log.LogInfo($"Uploaded RPC trace file {fileId}; deleting local copy.");
                        ResetCurrentUploadLocked(retryLater: false);
                        if (uploadedPath.Length > 0)
                            RpcTraceLocalStore.DeleteFile(uploadedPath);
                    }
                    else
                    {
                        _batchIndex++;
                    }
                }
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to process RPC trace batch ack from peer {sender}: {ex.Message}");
            }
        }

        internal static bool ShouldAllowLogout(Game game, bool save, bool changeToStartScene)
        {
            if (_allowLogout)
            {
                _allowLogout = false;
                RpcTraceTelemetry.SuppressCaptureUntilDisconnected();
                return true;
            }

            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            if (!RpcTraceLocalStore.HasPendingFiles() || !CanUpload())
            {
                RpcTraceTelemetry.SuppressCaptureUntilDisconnected();
                return true;
            }

            if (_delayingLogout)
                return false;

            RequestFlush("logout");
            RpcTraceTelemetry.SuppressCaptureUntilDisconnected();
            _delayingLogout = true;
            if (PraetorisClientPlugin.Instance != null)
                PraetorisClientPlugin.Instance.StartCoroutine(FlushThenLogout(game, save, changeToStartScene));
            return false;
        }

        internal static bool ShouldAllowMenuQuit()
        {
            if (_allowMenuQuit)
            {
                _allowMenuQuit = false;
                RpcTraceTelemetry.DisableCaptureForShutdown();
                return true;
            }

            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            if (!RpcTraceLocalStore.HasPendingFiles() || !CanUpload())
            {
                RpcTraceTelemetry.DisableCaptureForShutdown();
                return true;
            }

            if (_delayingMenuQuit)
                return false;

            RequestFlush("quit");
            RpcTraceTelemetry.DisableCaptureForShutdown();
            _delayingMenuQuit = true;
            if (PraetorisClientPlugin.Instance != null)
                PraetorisClientPlugin.Instance.StartCoroutine(FlushThenApplicationQuit());
            return false;
        }

        private static bool OnWantsToQuit()
        {
            if (_allowQuit)
            {
                RpcTraceTelemetry.DisableCaptureForShutdown();
                return true;
            }

            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            if (!RpcTraceLocalStore.HasPendingFiles() || !CanUpload())
            {
                RpcTraceTelemetry.DisableCaptureForShutdown();
                return true;
            }

            if (_delayingQuit)
                return false;

            RequestFlush("quit");
            RpcTraceTelemetry.DisableCaptureForShutdown();
            _delayingQuit = true;
            if (PraetorisClientPlugin.Instance != null)
                PraetorisClientPlugin.Instance.StartCoroutine(FlushThenQuit());
            return false;
        }

        private static IEnumerator FlushThenLogout(Game game, bool save, bool changeToStartScene)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + LogoutFlushTimeoutSeconds;
            while (!IsIdle() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                Update();
                yield return null;
            }

            _allowLogout = true;
            _delayingLogout = false;
            RpcTraceTelemetry.SuppressCaptureUntilDisconnected();
            game.Logout(save, changeToStartScene);
        }

        private static IEnumerator FlushThenQuit()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + QuitFlushTimeoutSeconds;
            while (!IsIdle() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                Update();
                yield return null;
            }

            _allowQuit = true;
            _delayingQuit = false;
            RpcTraceTelemetry.DisableCaptureForShutdown();
            Application.Quit();
        }

        private static IEnumerator FlushThenApplicationQuit()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + QuitFlushTimeoutSeconds;
            while (!IsIdle() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                Update();
                yield return null;
            }

            _allowMenuQuit = true;
            _delayingMenuQuit = false;
            RpcTraceTelemetry.DisableCaptureForShutdown();
            Application.Quit();
        }

        private static void PrepareFilesIfNeeded()
        {
            lock (Sync)
            {
                if (_currentPath != null || PendingFiles.Count > 0)
                    return;

                foreach (string file in RpcTraceLocalStore.GetFlushableFiles())
                {
                    if (new FileInfo(file).Length == 0L)
                    {
                        RpcTraceLocalStore.DeleteFile(file);
                        continue;
                    }

                    PendingFiles.Enqueue(file);
                }

                _flushRequested = false;
            }
        }

        private static void SendNextBatchIfReady()
        {
            lock (Sync)
            {
                if (_waitingForAck)
                {
                    if (Time.realtimeSinceStartupAsDouble > _ackDeadlineRealtime)
                    {
                        PraetorisClientPlugin.Log.LogWarning($"Timed out waiting for RPC trace ack for {_currentFileId}; keeping local file.");
                        ResetCurrentUploadLocked(retryLater: true);
                    }

                    return;
                }

                if (_currentPath == null)
                {
                    if (PendingFiles.Count == 0)
                        return;

                    _currentPath = PendingFiles.Dequeue();
                    _currentFileId = RpcTraceLocalStore.BuildFileId(_currentPath);
                    _nextStartLine = 0;
                    _batchIndex = 0;
                    _lastBatchSent = false;
                }

                int maxRows = MaxFlushRowsPerBatch;
                List<string> rows = RpcTraceLocalStore.ReadBatch(_currentPath, _nextStartLine, maxRows, out bool reachedEnd);
                if (rows.Count == 0 && reachedEnd)
                {
                    string emptyPath = _currentPath;
                    ResetCurrentUploadLocked(retryLater: false);
                    RpcTraceLocalStore.DeleteFile(emptyPath);
                    return;
                }

                SendBatchLocked(rows, reachedEnd);
            }
        }

        private static void SendBatchLocked(List<string> rows, bool finalBatch)
        {
            if (ZRoutedRpc.instance == null)
                return;

            ZPackage package = new();
            package.Write(RpcTraceTelemetry.ProtocolVersion);
            package.Write(_currentFileId);
            package.Write(_batchIndex);
            package.Write(finalBatch);
            package.Write(_flushReason);
            package.Write(rows.Count);
            foreach (string row in rows)
                package.Write(row);

            _waitingForAck = true;
            _lastBatchSent = finalBatch;
            _ackDeadlineRealtime = Time.realtimeSinceStartupAsDouble + AckTimeoutSeconds;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.RpcTraceBatch, package);
        }

        private static void ResetCurrentUploadLocked(bool retryLater)
        {
            if (retryLater && _currentPath != null)
                PendingFiles.Enqueue(_currentPath);

            _currentPath = null;
            _currentFileId = "";
            _nextStartLine = 0;
            _batchIndex = 0;
            _waitingForAck = false;
            _lastBatchSent = false;
        }

        private static bool IsIdle()
        {
            lock (Sync)
            {
                return !_flushRequested && PendingFiles.Count == 0 && _currentPath == null && !_waitingForAck;
            }
        }

        private static bool CanUpload()
        {
            return ZNet.instance != null
                && ZRoutedRpc.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }
    }
}
