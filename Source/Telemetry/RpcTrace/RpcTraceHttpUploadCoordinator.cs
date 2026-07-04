using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceHttpUploadCoordinator
    {
        private const int MaxHttpRowsPerBatch = 1000;
        private const int FirstRetryRowsPerBatch = 250;
        private const int MinHttpRowsPerBatch = 100;
        private const float InitialFailureRetrySeconds = 15f;
        private const float MaxFailureRetrySeconds = 300f;
        private const float FailureLogIntervalSeconds = 60f;
        private const float UploadFrameSummaryIntervalSeconds = 30f;
        private const float UploadFrameWarningThresholdMs = 150f;
        private const int HttpUploadTimeoutMilliseconds = 30000;
        private static readonly object Sync = new();
        private static readonly Queue<string> PendingFiles = new();
        private static bool _flushRequested;
        private static string _flushReason = "background";
        private static bool _uploading;
        private static float _nextUploadTime;
        private static int _consecutiveFailures;
        private static int _maxRowsPerUploadBatch = MaxHttpRowsPerBatch;
        private static float _nextFailureLogTime;
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
                _maxRowsPerUploadBatch = MaxHttpRowsPerBatch;
                _nextFailureLogTime = 0f;
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
                _maxRowsPerUploadBatch = MaxHttpRowsPerBatch;
                _nextFailureLogTime = 0f;
                ResetUploadFrameStatsLocked();
            }
        }

        internal static bool IsActive()
        {
            if (!HasUploadConfiguration())
                return false;

            return CanUpload();
        }

        internal static bool CanAcceptFlushRequest()
        {
            if (!HasUploadConfiguration())
                return false;

            return HasActiveGameplayClient() || ShouldDeferUploadDuringGameplay();
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

                ZdoTraceTelemetry.DrainPending();
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
            int batchIndex = 0;

            if (!File.Exists(path))
            {
                CompleteUpload(success: true);
                yield break;
            }

            RpcTraceLocalStore.PendingTraceReader reader;
            try
            {
                reader = RpcTraceLocalStore.OpenReader(path);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning(
                    "Failed to open HTTP RPC trace file "
                    + fileId
                    + ": "
                    + ex.Message);
                RequeueUpload(path);
                yield break;
            }

            using (reader)
            {
                while (IsActive() && File.Exists(path))
                {
                    int maxRows = GetMaxRowsPerUploadBatch();
                    Task<PreparedUploadBatch> prepareTask = Task.Run(() => PrepareUploadBatch(reader, maxRows));
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
                    Dictionary<string, string> headers = RpcTraceHttpUploadContract.BuildHeaders(
                        RpcTraceUploadTokenClient.Token,
                        batchId,
                        RpcTraceTelemetry.RuntimeId,
                        fileId,
                        batchIndex,
                        batch.FinalBatch,
                        flushReason,
                        PraetorisClientPlugin.TraceModVersion);

                    Task<UploadResult> uploadTask = Task.Run(() => SendHttpUpload(RpcTraceUploadTokenClient.EndpointUrl, headers, batch.Body));
                    while (!uploadTask.IsCompleted)
                        yield return null;

                    if (uploadTask.IsFaulted)
                    {
                        string message = uploadTask.Exception?.GetBaseException().Message ?? "unknown error";
                        RegisterUploadFailure(batchId, 0L, message);
                        RequeueUpload(path);
                        yield break;
                    }

                    UploadResult uploadResult = uploadTask.Result;
                    if (!uploadResult.Success)
                    {
                        if (!RpcTraceUploadTokenClient.ShouldRetryUpload(uploadResult.ResponseCode, uploadResult.Message))
                        {
                            CompleteUpload(success: false);
                            yield break;
                        }

                        RegisterUploadFailure(batchId, uploadResult.ResponseCode, uploadResult.Message);
                        RequeueUpload(path);
                        yield break;
                    }

                    RegisterUploadSuccess();
                    batchIndex++;
                    if (batch.FinalBatch)
                    {
                        PraetorisClientPlugin.Log.LogInfo($"Uploaded RPC trace file {fileId} over HTTP; deleting local copy.");
                        RpcTraceLocalStore.DeleteFile(path);
                        CompleteUpload(success: true);
                        yield break;
                    }
                }
            }

            RequeueUpload(path);
        }

        private static UploadResult SendHttpUpload(string endpointUrl, Dictionary<string, string> headers, byte[] body)
        {
            if (!TryNormalizeEndpointUrl(endpointUrl, out string normalizedEndpointUrl, out string endpointError))
                return new UploadResult(false, 0L, endpointError);

            try
            {
                Uri uri = new(normalizedEndpointUrl);
                int port = uri.Port > 0 ? uri.Port : (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
                using TcpClient client = new();
                client.SendTimeout = HttpUploadTimeoutMilliseconds;
                client.ReceiveTimeout = HttpUploadTimeoutMilliseconds;
                ConnectWithTimeout(client, uri.Host, port);
                using Stream stream = CreateRequestStream(client, uri);
                WriteHttpRequest(stream, uri, port, headers, body);
                return ReadHttpResponse(stream);
            }
            catch (IOException ex)
            {
                return new UploadResult(false, 0L, ex.Message);
            }
            catch (SocketException ex)
            {
                return new UploadResult(false, 0L, ex.Message);
            }
            catch (AuthenticationException ex)
            {
                return new UploadResult(false, 0L, ex.Message);
            }
        }

        private static void ConnectWithTimeout(TcpClient client, string host, int port)
        {
            IAsyncResult result = client.BeginConnect(host, port, null, null);
            try
            {
                if (!result.AsyncWaitHandle.WaitOne(HttpUploadTimeoutMilliseconds))
                    throw new IOException("Request timeout");

                client.EndConnect(result);
            }
            finally
            {
                result.AsyncWaitHandle.Close();
            }
        }

        private static Stream CreateRequestStream(TcpClient client, Uri uri)
        {
            NetworkStream networkStream = client.GetStream();
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return networkStream;

            SslStream sslStream = new(networkStream, leaveInnerStreamOpen: false);
            sslStream.AuthenticateAsClient(uri.Host);
            return sslStream;
        }

        private static void WriteHttpRequest(Stream stream, Uri uri, int port, Dictionary<string, string> headers, byte[] body)
        {
            StringBuilder builder = new();
            string pathAndQuery = string.IsNullOrWhiteSpace(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            builder.Append("POST ").Append(pathAndQuery).Append(" HTTP/1.1\r\n");
            builder.Append("Host: ").Append(BuildHostHeader(uri, port)).Append("\r\n");
            builder.Append("Connection: close\r\n");
            builder.Append("Content-Length: ").Append(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");

            foreach (KeyValuePair<string, string> header in headers)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                builder.Append(header.Key).Append(": ").Append(header.Value ?? "").Append("\r\n");
            }

            builder.Append("\r\n");
            byte[] headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static string BuildHostHeader(Uri uri, int port)
        {
            bool defaultPort = (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && port == 80)
                || (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && port == 443);
            return defaultPort ? uri.Host : uri.Host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static UploadResult ReadHttpResponse(Stream stream)
        {
            using MemoryStream output = new();
            byte[] buffer = new byte[8192];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                output.Write(buffer, 0, read);
                headerEnd = FindHeaderEnd(output.GetBuffer(), (int)output.Length);
            }

            byte[] responseBytes = output.ToArray();
            string responseText = Encoding.UTF8.GetString(responseBytes);
            string headerText = headerEnd >= 0 ? responseText.Substring(0, headerEnd) : responseText;
            string firstLine = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
            string[] parts = firstLine.Split(' ');
            if (parts.Length < 2 || !long.TryParse(parts[1], out long responseCode))
                return new UploadResult(false, 0L, "Invalid HTTP response");

            int bodyOffset = headerEnd >= 0 ? headerEnd + 4 : responseBytes.Length;
            int contentLength = GetContentLength(headerText);
            if (contentLength >= 0)
            {
                int remainingBodyBytes = contentLength - Math.Max(0, responseBytes.Length - bodyOffset);
                while (remainingBodyBytes > 0)
                {
                    int readLength = Math.Min(buffer.Length, remainingBodyBytes);
                    int read = stream.Read(buffer, 0, readLength);
                    if (read <= 0)
                        break;

                    output.Write(buffer, 0, read);
                    remainingBodyBytes -= read;
                }

                responseBytes = output.ToArray();
            }

            int bodyLength = Math.Max(0, Math.Min(responseBytes.Length - bodyOffset, contentLength >= 0 ? contentLength : responseBytes.Length - bodyOffset));
            string bodyText = bodyLength > 0 ? Encoding.UTF8.GetString(responseBytes, bodyOffset, bodyLength) : "";
            bool success = responseCode >= 200L && responseCode < 300L;
            return new UploadResult(success, responseCode, bodyText);
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int index = 0; index <= length - 4; index++)
            {
                if (buffer[index] == (byte)'\r'
                    && buffer[index + 1] == (byte)'\n'
                    && buffer[index + 2] == (byte)'\r'
                    && buffer[index + 3] == (byte)'\n')
                    return index;
            }

            return -1;
        }

        private static int GetContentLength(string headerText)
        {
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                int separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                string name = line.Substring(0, separator).Trim();
                if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line.Substring(separator + 1).Trim();
                if (int.TryParse(value, out int contentLength))
                    return Math.Max(0, contentLength);
            }

            return -1;
        }

        private static bool TryNormalizeEndpointUrl(string endpointUrl, out string normalizedEndpointUrl, out string error)
        {
            normalizedEndpointUrl = (endpointUrl ?? "").Trim();
            error = "";

            if (string.IsNullOrWhiteSpace(normalizedEndpointUrl))
            {
                error = "Empty upload endpoint URL";
                return false;
            }

            if (normalizedEndpointUrl.StartsWith("//", StringComparison.Ordinal))
                normalizedEndpointUrl = "http:" + normalizedEndpointUrl;
            else if (!normalizedEndpointUrl.Contains("://"))
                normalizedEndpointUrl = "http://" + normalizedEndpointUrl;

            if (!Uri.TryCreate(normalizedEndpointUrl, UriKind.Absolute, out Uri uri))
            {
                error = "Invalid upload endpoint URL";
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Unsupported upload endpoint scheme";
                return false;
            }

            normalizedEndpointUrl = uri.AbsoluteUri;
            return true;
        }

        private static PreparedUploadBatch PrepareUploadBatch(RpcTraceLocalStore.PendingTraceReader reader, int maxRows)
        {
            List<string> rows = reader.ReadRows(maxRows, out bool reachedEnd);
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

                int pushbackIndex = rows.Count - 1;
                reader.PushBack(rows, pushbackIndex);
                rows.RemoveAt(pushbackIndex);
                reachedEnd = false;
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
            int adjustedMaxRows;
            lock (Sync)
            {
                _consecutiveFailures++;
                AdjustBatchSizeAfterFailure(responseCode, message);
                adjustedMaxRows = _maxRowsPerUploadBatch;
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
                + "s with maxRows="
                + adjustedMaxRows
                + ".");
        }

        private static void RegisterUploadSuccess()
        {
            lock (Sync)
            {
                _consecutiveFailures = 0;
                _nextFailureLogTime = 0f;
            }
        }

        private static int GetMaxRowsPerUploadBatch()
        {
            lock (Sync)
                return Math.Max(MinHttpRowsPerBatch, _maxRowsPerUploadBatch);
        }

        private static void AdjustBatchSizeAfterFailure(long responseCode, string message)
        {
            if (!IsReceiverTimeoutFailure(responseCode, message))
                return;

            if (_maxRowsPerUploadBatch > FirstRetryRowsPerBatch)
            {
                _maxRowsPerUploadBatch = FirstRetryRowsPerBatch;
                return;
            }

            _maxRowsPerUploadBatch = MinHttpRowsPerBatch;
        }

        private static bool IsReceiverTimeoutFailure(long responseCode, string message)
        {
            if (responseCode == 504L || responseCode == 408L)
                return true;

            return (message ?? "").IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float GetFailureRetryDelaySeconds()
        {
            int failureCount = Math.Max(1, _consecutiveFailures);
            double multiplier = Math.Pow(2d, Math.Min(5, failureCount - 1));
            return Math.Min(MaxFailureRetrySeconds, InitialFailureRetrySeconds * (float)multiplier);
        }

        private static bool CanUpload()
        {
            if (ShouldDeferUploadDuringGameplay() && HasActiveGameplayClient())
                return false;

            return HasUploadRuntimeContext();
        }

        private static bool HasUploadConfiguration()
        {
            return !PraetorisClientPlugin.MeasurementDisableHttpTraceUpload.Value
                && PraetorisClientPlugin.RpcTraceHttpUploadPreferred.Value
                && RpcTraceUploadTokenClient.HasUsableToken();
        }

        private static bool HasActiveGameplayClient()
        {
            return ZNet.instance != null
                && ZRoutedRpc.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected
                && Game.instance != null
                && Player.m_localPlayer != null;
        }

        private static bool HasUploadRuntimeContext()
        {
            if (!ShouldDeferUploadDuringGameplay())
                return HasActiveGameplayClient();

            return PraetorisClientPlugin.Instance != null;
        }

        private static bool ShouldDeferUploadDuringGameplay()
        {
            return PraetorisClientPlugin.RpcTraceDeferHttpUploadDuringGameplay.Value;
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

        private sealed class UploadResult
        {
            internal UploadResult(bool success, long responseCode, string message)
            {
                Success = success;
                ResponseCode = responseCode;
                Message = message ?? "";
            }

            internal bool Success { get; }
            internal long ResponseCode { get; }
            internal string Message { get; }
        }
    }
}
