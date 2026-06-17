using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PraetorisClient
{
    internal static class RpcTraceHttpUploadCoordinator
    {
        private const int MaxHttpRowsPerBatch = 5000;
        private const float InitialFailureRetrySeconds = 15f;
        private const float MaxFailureRetrySeconds = 300f;
        private const float FailureLogIntervalSeconds = 60f;
        private static readonly object Sync = new();
        private static readonly Queue<string> PendingFiles = new();
        private static bool _flushRequested;
        private static string _flushReason = "background";
        private static bool _uploading;
        private static float _nextUploadTime;
        private static int _consecutiveFailures;
        private static float _nextFailureLogTime;

        internal static void Initialize()
        {
            lock (Sync)
            {
                PendingFiles.Clear();
                _flushRequested = false;
                _flushReason = "background";
                _uploading = false;
                _nextUploadTime = 0f;
                _consecutiveFailures = 0;
                _nextFailureLogTime = 0f;
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                PendingFiles.Clear();
                _uploading = false;
                _flushRequested = false;
                _consecutiveFailures = 0;
                _nextFailureLogTime = 0f;
            }
        }

        internal static bool IsActive()
        {
            return PraetorisClientPlugin.RpcTraceHttpUploadPreferred.Value
                && RpcTraceUploadTokenClient.HasUsableToken()
                && CanUpload();
        }

        internal static void RequestFlush(string reason)
        {
            lock (Sync)
            {
                _flushRequested = true;
                _flushReason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason;
                _nextUploadTime = 0f;
            }
        }

        internal static void Update()
        {
            if (!IsActive())
                return;

            lock (Sync)
            {
                if (_uploading)
                    return;
                if (!_flushRequested && Time.realtimeSinceStartup < _nextUploadTime)
                    return;
            }

            PrepareFilesIfNeeded();
            StartNextUploadIfReady();
        }

        private static void PrepareFilesIfNeeded()
        {
            lock (Sync)
            {
                if (PendingFiles.Count > 0 || _uploading)
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
                _nextUploadTime = Time.realtimeSinceStartup + RpcTraceUploadTokenClient.FlushIntervalSeconds;
            }
        }

        private static void StartNextUploadIfReady()
        {
            string path;
            string reason;
            lock (Sync)
            {
                if (_uploading || PendingFiles.Count == 0)
                    return;

                path = PendingFiles.Dequeue();
                reason = _flushReason;
                _uploading = true;
                _flushRequested = false;
            }

            if (PraetorisClientPlugin.Instance != null)
            {
                PraetorisClientPlugin.Instance.StartCoroutine(UploadFile(path, reason));
                return;
            }

            RequeueUpload(path);
        }

        private static IEnumerator UploadFile(string path, string flushReason)
        {
            string fileId = RpcTraceLocalStore.BuildFileId(path);
            int startLine = 0;
            int batchIndex = 0;

            while (IsActive() && File.Exists(path))
            {
                List<string> rows = RpcTraceLocalStore.ReadBatch(path, startLine, MaxHttpRowsPerBatch, out bool reachedEnd);
                if (rows.Count == 0 && reachedEnd)
                {
                    RpcTraceLocalStore.DeleteFile(path);
                    CompleteUpload(success: true);
                    yield break;
                }

                byte[] body = BuildBodyWithinLimit(rows, reachedEnd, out bool finalBatch);
                if (body.Length == 0 || rows.Count == 0)
                {
                    PraetorisClientPlugin.Log.LogWarning($"Skipping oversized HTTP RPC trace row in {fileId}.");
                    startLine++;
                    continue;
                }

                string batchId = fileId + "-" + batchIndex.ToString("D6");
                using UnityWebRequest request = new(RpcTraceUploadTokenClient.EndpointUrl, "POST")
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 30
                };
                Dictionary<string, string> headers = RpcTraceHttpUploadContract.BuildHeaders(
                    RpcTraceUploadTokenClient.Token,
                    batchId,
                    RpcTraceTelemetry.RuntimeId,
                    fileId,
                    batchIndex,
                    finalBatch,
                    flushReason,
                    PraetorisClientPlugin.TraceModVersion);
                foreach (KeyValuePair<string, string> header in headers)
                    request.SetRequestHeader(header.Key, header.Value);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success || request.responseCode < 200 || request.responseCode >= 300)
                {
                    string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
                    string message = string.IsNullOrWhiteSpace(responseText) ? request.error : responseText;
                    if (!RpcTraceUploadTokenClient.ShouldRetryUpload(request.responseCode, message))
                    {
                        CompleteUpload(success: false);
                        yield break;
                    }

                    RegisterUploadFailure(batchId, request.responseCode, message);
                    RequeueUpload(path);
                    yield break;
                }

                RegisterUploadSuccess();
                startLine += rows.Count;
                batchIndex++;
                if (finalBatch)
                {
                    PraetorisClientPlugin.Log.LogInfo($"Uploaded RPC trace file {fileId} over HTTP; deleting local copy.");
                    RpcTraceLocalStore.DeleteFile(path);
                    CompleteUpload(success: true);
                    yield break;
                }
            }

            RequeueUpload(path);
        }

        private static byte[] BuildBodyWithinLimit(List<string> rows, bool reachedEnd, out bool finalBatch)
        {
            finalBatch = reachedEnd;
            int maxBytes = Math.Max(4096, RpcTraceUploadTokenClient.MaxBatchBytes);
            while (rows.Count > 0)
            {
                byte[] body = GzipRows(rows);
                if (body.Length <= maxBytes)
                    return body;

                if (rows.Count == 1)
                {
                    rows.Clear();
                    finalBatch = false;
                    return Array.Empty<byte>();
                }

                rows.RemoveAt(rows.Count - 1);
                finalBatch = false;
            }

            return Array.Empty<byte>();
        }

        private static byte[] GzipRows(IReadOnlyList<string> rows)
        {
            using MemoryStream output = new();
            using (GZipStream gzip = new(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            using (StreamWriter writer = new(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (string row in rows)
                    writer.WriteLine(row);
            }

            return output.ToArray();
        }

        private static void RequeueUpload(string path)
        {
            lock (Sync)
            {
                if (File.Exists(path))
                    PendingFiles.Enqueue(path);
                _uploading = false;
                _flushRequested = false;
                _nextUploadTime = Time.realtimeSinceStartup + GetFailureRetryDelaySeconds();
            }
        }

        private static void CompleteUpload(bool success)
        {
            lock (Sync)
            {
                _uploading = false;
                if (success)
                    _consecutiveFailures = 0;
                if (success && PendingFiles.Count > 0)
                    _nextUploadTime = 0f;
                else
                    _nextUploadTime = Time.realtimeSinceStartup + (success ? RpcTraceUploadTokenClient.FlushIntervalSeconds : GetFailureRetryDelaySeconds());
            }
        }

        private static void RegisterUploadFailure(string batchId, long responseCode, string message)
        {
            bool shouldLog;
            float retryDelaySeconds;
            lock (Sync)
            {
                _consecutiveFailures++;
                retryDelaySeconds = GetFailureRetryDelaySeconds();
                shouldLog = Time.realtimeSinceStartup >= _nextFailureLogTime;
                if (shouldLog)
                    _nextFailureLogTime = Time.realtimeSinceStartup + FailureLogIntervalSeconds;
            }

            if (!shouldLog)
                return;

            PraetorisClientPlugin.Log.LogWarning(
                "HTTP RPC trace upload failed for "
                + batchId
                + ": HTTP "
                + responseCode
                + " "
                + message
                + ". Retrying in "
                + Math.Ceiling(retryDelaySeconds)
                + "s.");
        }

        private static void RegisterUploadSuccess()
        {
            lock (Sync)
            {
                _consecutiveFailures = 0;
                _nextFailureLogTime = 0f;
            }
        }

        private static float GetFailureRetryDelaySeconds()
        {
            int failureCount = Math.Max(1, _consecutiveFailures);
            double multiplier = Math.Pow(2d, Math.Min(5, failureCount - 1));
            return Math.Min(MaxFailureRetrySeconds, InitialFailureRetrySeconds * (float)multiplier);
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
