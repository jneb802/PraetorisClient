using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
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
        private const float UploadFrameSummaryIntervalSeconds = 30f;
        private const float UploadFrameWarningThresholdMs = 150f;
        private const float WorldReadyUploadDelaySeconds = 20f;
        private static readonly object Sync = new();
        private static readonly Queue<string> PendingFiles = new();
        private static bool _flushRequested;
        private static string _flushReason = "background";
        private static bool _uploading;
        private static float _nextUploadTime;
        private static int _consecutiveFailures;
        private static float _nextFailureLogTime;
        private static float _worldReadyUploadTime;
        private static float _uploadFrameWindowActiveUntil;
        private static float _nextUploadFrameSummaryTime;
        private static int _uploadFrameSamples;
        private static int _longUploadFrames;
        private static float _maxUploadFrameMs;

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
                _worldReadyUploadTime = 0f;
                ResetUploadFrameStatsLocked();
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
                _worldReadyUploadTime = 0f;
                ResetUploadFrameStatsLocked();
            }
        }

        internal static bool IsActive()
        {
            if (!HasUploadConfiguration())
            {
                _worldReadyUploadTime = 0f;
                return false;
            }

            return CanUpload();
        }

        internal static bool CanAcceptFlushRequest()
        {
            if (!HasUploadConfiguration())
            {
                _worldReadyUploadTime = 0f;
                return false;
            }

            return HasConnectedClient();
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
            RecordUploadFrame();

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
                _uploadFrameWindowActiveUntil = Time.realtimeSinceStartup + UploadFrameSummaryIntervalSeconds;
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
                Task<PreparedUploadBatch> prepareTask = Task.Run(() => PrepareUploadBatch(path, startLine));
                while (!prepareTask.IsCompleted)
                    yield return null;

                if (prepareTask.IsFaulted)
                {
                    PraetorisClientPlugin.Log.LogWarning(
                        "Failed to prepare HTTP RPC trace batch for "
                        + fileId
                        + ": "
                        + (prepareTask.Exception?.GetBaseException().Message ?? "unknown error"));
                    RequeueUpload(path);
                    yield break;
                }

                PreparedUploadBatch batch = prepareTask.Result;
                if (batch.EndOfFile)
                {
                    RpcTraceLocalStore.DeleteFile(path);
                    CompleteUpload(success: true);
                    yield break;
                }

                if (batch.SkippedOversizedRow)
                {
                    PraetorisClientPlugin.Log.LogWarning($"Skipping oversized HTTP RPC trace row in {fileId}.");
                    startLine++;
                    continue;
                }

                string batchId = fileId + "-" + batchIndex.ToString("D6");
                PraetorisClientPlugin.Log.LogInfo(
                    "Prepared HTTP RPC trace batch "
                    + batchId
                    + ": rows="
                    + batch.ConsumedRows
                    + ", gzipBytes="
                    + batch.Body.Length
                    + ", final="
                    + batch.FinalBatch
                    + ", prepareMs="
                    + batch.PreparationMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + " off main thread.");
                using UnityWebRequest request = new(RpcTraceUploadTokenClient.EndpointUrl, "POST")
                {
                    uploadHandler = new UploadHandlerRaw(batch.Body),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 30
                };
                Dictionary<string, string> headers = RpcTraceHttpUploadContract.BuildHeaders(
                    RpcTraceUploadTokenClient.Token,
                    batchId,
                    RpcTraceTelemetry.RuntimeId,
                    fileId,
                    batchIndex,
                    batch.FinalBatch,
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
                startLine += batch.ConsumedRows;
                batchIndex++;
                if (batch.FinalBatch)
                {
                    PraetorisClientPlugin.Log.LogInfo($"Uploaded RPC trace file {fileId} over HTTP; deleting local copy.");
                    RpcTraceLocalStore.DeleteFile(path);
                    CompleteUpload(success: true);
                    yield break;
                }
            }

            RequeueUpload(path);
        }

        private static PreparedUploadBatch PrepareUploadBatch(string path, int startLine)
        {
            List<string> rows = RpcTraceLocalStore.ReadBatch(path, startLine, MaxHttpRowsPerBatch, out bool reachedEnd);
            if (rows.Count == 0 && reachedEnd)
                return PreparedUploadBatch.End();

            Stopwatch stopwatch = Stopwatch.StartNew();
            int maxBytes = Math.Max(4096, RpcTraceUploadTokenClient.MaxBatchBytes);
            while (rows.Count > 0)
            {
                byte[] body = GzipRows(rows);
                if (body.Length <= maxBytes)
                {
                    stopwatch.Stop();
                    return PreparedUploadBatch.Ready(body, rows.Count, reachedEnd, stopwatch.Elapsed.TotalMilliseconds);
                }

                if (rows.Count == 1)
                {
                    return PreparedUploadBatch.SkipOversizedRow();
                }

                rows.RemoveAt(rows.Count - 1);
            }

            return PreparedUploadBatch.SkipOversizedRow();
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
                _uploadFrameWindowActiveUntil = Time.realtimeSinceStartup + 2f;
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
                _uploadFrameWindowActiveUntil = Time.realtimeSinceStartup + 2f;
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
            if (!HasConnectedClient())
            {
                _worldReadyUploadTime = 0f;
                return false;
            }

            if (_worldReadyUploadTime <= 0f)
                _worldReadyUploadTime = Time.realtimeSinceStartup + WorldReadyUploadDelaySeconds;

            return Time.realtimeSinceStartup >= _worldReadyUploadTime;
        }

        private static bool HasUploadConfiguration()
        {
            return PraetorisClientPlugin.RpcTraceHttpUploadPreferred.Value
                && RpcTraceUploadTokenClient.HasUsableToken();
        }

        private static bool HasConnectedClient()
        {
            return ZNet.instance != null
                && ZRoutedRpc.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected
                && Game.instance != null
                && Player.m_localPlayer != null;
        }

        private static void RecordUploadFrame()
        {
            bool shouldSummarize = false;
            int samples = 0;
            int longFrames = 0;
            float maxFrameMs = 0f;
            float now = Time.realtimeSinceStartup;
            float frameMs = Time.unscaledDeltaTime * 1000f;

            lock (Sync)
            {
                if (!_uploading && now > _uploadFrameWindowActiveUntil)
                    return;

                _uploadFrameSamples++;
                if (frameMs > _maxUploadFrameMs)
                    _maxUploadFrameMs = frameMs;
                if (frameMs >= UploadFrameWarningThresholdMs)
                    _longUploadFrames++;

                if (_nextUploadFrameSummaryTime <= 0f)
                    _nextUploadFrameSummaryTime = now + UploadFrameSummaryIntervalSeconds;

                if (now >= _nextUploadFrameSummaryTime)
                {
                    shouldSummarize = _uploadFrameSamples > 0;
                    samples = _uploadFrameSamples;
                    longFrames = _longUploadFrames;
                    maxFrameMs = _maxUploadFrameMs;
                    ResetUploadFrameStatsLocked();
                    _nextUploadFrameSummaryTime = now + UploadFrameSummaryIntervalSeconds;
                }
            }

            if (!shouldSummarize)
                return;

            PraetorisClientPlugin.Log.LogInfo(
                "HTTP RPC trace upload frame summary: samples="
                + samples
                + ", longFramesOver"
                + UploadFrameWarningThresholdMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                + "ms="
                + longFrames
                + ", maxFrameMs="
                + maxFrameMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }

        private static void ResetUploadFrameStatsLocked()
        {
            _uploadFrameWindowActiveUntil = 0f;
            _nextUploadFrameSummaryTime = 0f;
            _uploadFrameSamples = 0;
            _longUploadFrames = 0;
            _maxUploadFrameMs = 0f;
        }

        private sealed class PreparedUploadBatch
        {
            private PreparedUploadBatch(
                byte[] body,
                int consumedRows,
                bool finalBatch,
                bool endOfFile,
                bool skippedOversizedRow,
                double preparationMilliseconds)
            {
                Body = body;
                ConsumedRows = consumedRows;
                FinalBatch = finalBatch;
                EndOfFile = endOfFile;
                SkippedOversizedRow = skippedOversizedRow;
                PreparationMilliseconds = preparationMilliseconds;
            }

            internal byte[] Body { get; }
            internal int ConsumedRows { get; }
            internal bool FinalBatch { get; }
            internal bool EndOfFile { get; }
            internal bool SkippedOversizedRow { get; }
            internal double PreparationMilliseconds { get; }

            internal static PreparedUploadBatch Ready(byte[] body, int consumedRows, bool finalBatch, double preparationMilliseconds)
            {
                return new PreparedUploadBatch(
                    body,
                    consumedRows,
                    finalBatch,
                    endOfFile: false,
                    skippedOversizedRow: false,
                    preparationMilliseconds);
            }

            internal static PreparedUploadBatch End()
            {
                return new PreparedUploadBatch(Array.Empty<byte>(), 0, finalBatch: true, endOfFile: true, skippedOversizedRow: false, 0d);
            }

            internal static PreparedUploadBatch SkipOversizedRow()
            {
                return new PreparedUploadBatch(Array.Empty<byte>(), 0, finalBatch: false, endOfFile: false, skippedOversizedRow: true, 0d);
            }
        }
    }
}
