using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using BepInEx;

namespace PraetorisClient
{
    internal static class RpcTraceLocalStore
    {
        private const int FileBufferBytes = 65536;
        private const int WorkerPollMilliseconds = 250;
        private const int WorkerShutdownTimeoutMilliseconds = 10000;
        private const string CompressedTraceExtension = ".jsonl.gz";
        private static readonly object LifecycleSync = new();
        private static BlockingCollection<WorkItem>? _queue;
        private static Thread? _worker;
        private static string _pendingDirectory = "";
        private static volatile bool _hasActiveWriter;
        private static double _nextRotationRealtime;

        internal static void Initialize()
        {
            _pendingDirectory = Path.Combine(Paths.BepInExRootPath, "logs", "PraetorisClient", "NetworkMetrics", "pending");
            Directory.CreateDirectory(_pendingDirectory);

            lock (LifecycleSync)
            {
                if (_worker != null && _worker.IsAlive)
                    return;

                _queue = new BlockingCollection<WorkItem>();
                _worker = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "PraetorisClient network metric writer"
                };
                _worker.Start(_queue);
            }
        }

        internal static void Append(string line, long localPeerId)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            Append(line, localPeerId, worldUid);
        }

        internal static void Append(string line, long localPeerId, long worldUid)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            BlockingCollection<WorkItem>? queue = _queue;
            if (queue == null || queue.IsAddingCompleted)
                return;

            try
            {
                queue.Add(WorkItem.Append(line, localPeerId, worldUid));
            }
            catch (InvalidOperationException)
            {
            }
        }

        internal static void FlushIfDue(double realtime)
        {
            if (!_hasActiveWriter)
                return;

            if (_nextRotationRealtime <= 0.0)
                _nextRotationRealtime = realtime + GetBatchIntervalSeconds();

            if (realtime < _nextRotationRealtime)
                return;

            _nextRotationRealtime = realtime + GetBatchIntervalSeconds();
            EnqueueClose(null);
        }

        internal static void CloseCurrentFile()
        {
            using ManualResetEventSlim completed = new(false);
            EnqueueClose(completed);
            if (!completed.Wait(WorkerShutdownTimeoutMilliseconds))
                PraetorisClientPlugin.Log.LogWarning("Timed out waiting for network metric writer to close current file.");
        }

        internal static void CloseCurrentFileIfOpen()
        {
            BlockingCollection<WorkItem>? queue = _queue;
            if (!_hasActiveWriter && (queue == null || queue.Count == 0))
                return;

            CloseCurrentFile();
        }

        private static void EnqueueClose(ManualResetEventSlim? completed)
        {
            BlockingCollection<WorkItem>? queue = _queue;
            if (queue == null || queue.IsAddingCompleted)
                return;

            try
            {
                queue.Add(WorkItem.Close(completed));
            }
            catch (InvalidOperationException)
            {
            }
        }

        internal static void Shutdown()
        {
            BlockingCollection<WorkItem>? queue;
            Thread? worker;
            lock (LifecycleSync)
            {
                queue = _queue;
                worker = _worker;
                _queue = null;
                _worker = null;
            }

            if (queue == null)
                return;

            try
            {
                queue.CompleteAdding();
            }
            catch (InvalidOperationException)
            {
            }

            if (worker != null && worker.IsAlive && !worker.Join(WorkerShutdownTimeoutMilliseconds))
                PraetorisClientPlugin.Log.LogWarning("Network metric writer did not exit cleanly before timeout.");

            queue.Dispose();
            _hasActiveWriter = false;
        }

        private static void WriterLoop(object? state)
        {
            BlockingCollection<WorkItem> queue = (BlockingCollection<WorkItem>)state!;
            StreamWriter? writer = null;
            string? currentPath = null;
            int currentRowCount = 0;
            bool hasUnflushedRows = false;
            DateTime nextFlushUtc = DateTime.UtcNow.AddSeconds(1);

            try
            {
                while (true)
                {
                    if (!queue.TryTake(out WorkItem item, WorkerPollMilliseconds))
                    {
                        if (queue.IsCompleted)
                            return;
                        FlushIfNeeded(writer, ref hasUnflushedRows, ref nextFlushUtc);
                        continue;
                    }

                    if (item.Kind == WorkItemKind.Close)
                    {
                        FlushAndClose(ref writer, ref currentPath, ref currentRowCount, ref hasUnflushedRows);
                        SignalCompleted(item.Completed);
                        continue;
                    }

                    try
                    {
                        EnsureWriter(ref writer, ref currentPath, item.LocalPeerId, item.WorldUid);
                        writer?.WriteLine(item.Line);
                        currentRowCount++;
                        hasUnflushedRows = true;
                        _hasActiveWriter = true;
                        FlushIfNeeded(writer, ref hasUnflushedRows, ref nextFlushUtc);
                        if (currentRowCount >= GetMaxBatchRows())
                            FlushAndClose(ref writer, ref currentPath, ref currentRowCount, ref hasUnflushedRows);
                    }
                    catch (Exception ex)
                    {
                        PraetorisClientPlugin.Log.LogWarning("Failed to write network metric row: " + ex.Message);
                    }
                }
            }
            finally
            {
                FlushAndClose(ref writer, ref currentPath, ref currentRowCount, ref hasUnflushedRows);
            }
        }

        private static void SignalCompleted(ManualResetEventSlim? completed)
        {
            if (completed == null)
                return;

            try
            {
                completed.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static void EnsureWriter(ref StreamWriter? writer, ref string? currentPath, long localPeerId, long worldUid)
        {
            if (writer != null)
                return;

            Directory.CreateDirectory(_pendingDirectory);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string fileName = "network_metrics_"
                + timestamp
                + "_world_"
                + worldUid.ToString(CultureInfo.InvariantCulture)
                + "_peer_"
                + localPeerId.ToString(CultureInfo.InvariantCulture)
                + "_"
                + Guid.NewGuid().ToString("N")
                + CompressedTraceExtension;
            currentPath = Path.Combine(_pendingDirectory, fileName);
            FileStream stream = new(currentPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, FileBufferBytes, FileOptions.SequentialScan);
            GZipStream gzip = new(stream, CompressionLevel.Fastest);
            writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), FileBufferBytes)
            {
                AutoFlush = false
            };
            _hasActiveWriter = true;
        }

        private static void FlushIfNeeded(StreamWriter? writer, ref bool hasUnflushedRows, ref DateTime nextFlushUtc)
        {
            if (!hasUnflushedRows || writer == null || DateTime.UtcNow < nextFlushUtc)
                return;

            writer.Flush();
            hasUnflushedRows = false;
            nextFlushUtc = DateTime.UtcNow.AddSeconds(1);
        }

        private static void FlushAndClose(ref StreamWriter? writer, ref string? currentPath, ref int currentRowCount, ref bool hasUnflushedRows)
        {
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            finally
            {
                writer = null;
                currentPath = null;
                currentRowCount = 0;
                hasUnflushedRows = false;
                _hasActiveWriter = false;
                _nextRotationRealtime = 0.0;
            }
        }

        private static int GetMaxBatchRows()
        {
            return Math.Max(1, PraetorisClientPlugin.MetricMaxBatchRows.Value);
        }

        private static float GetBatchIntervalSeconds()
        {
            return Math.Max(1f, PraetorisClientPlugin.MetricBatchIntervalSeconds.Value);
        }

        private enum WorkItemKind
        {
            Append,
            Close
        }

        private sealed class WorkItem
        {
            private WorkItem(WorkItemKind kind, string line, long localPeerId, long worldUid, ManualResetEventSlim? completed)
            {
                Kind = kind;
                Line = line;
                LocalPeerId = localPeerId;
                WorldUid = worldUid;
                Completed = completed;
            }

            internal WorkItemKind Kind { get; }
            internal string Line { get; }
            internal long LocalPeerId { get; }
            internal long WorldUid { get; }
            internal ManualResetEventSlim? Completed { get; }

            internal static WorkItem Append(string line, long localPeerId, long worldUid)
            {
                return new WorkItem(WorkItemKind.Append, line, localPeerId, worldUid, null);
            }

            internal static WorkItem Close(ManualResetEventSlim? completed)
            {
                return new WorkItem(WorkItemKind.Close, "", 0L, 0L, completed);
            }

        }

    }
}
