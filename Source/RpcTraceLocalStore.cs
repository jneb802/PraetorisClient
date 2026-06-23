using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private static readonly object LifecycleSync = new();
        private static BlockingCollection<WorkItem>? _queue;
        private static Thread? _worker;
        private static string _pendingDirectory = "";
        private static volatile bool _hasActiveWriter;

        internal static void Initialize()
        {
            _pendingDirectory = Path.Combine(Paths.BepInExRootPath, "logs", "PraetorisClient", "RpcTrace", "pending");
            Directory.CreateDirectory(_pendingDirectory);

            lock (LifecycleSync)
            {
                if (_worker != null && _worker.IsAlive)
                    return;

                _queue = new BlockingCollection<WorkItem>();
                _worker = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "PraetorisClient RPC trace writer"
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
            _ = realtime;
        }

        internal static void CloseCurrentFile()
        {
            BlockingCollection<WorkItem>? queue = _queue;
            if (queue == null || queue.IsAddingCompleted)
                return;

            using ManualResetEventSlim completed = new(false);
            try
            {
                queue.Add(WorkItem.Close(completed));
                if (!completed.Wait(WorkerShutdownTimeoutMilliseconds))
                    PraetorisClientPlugin.Log.LogWarning("Timed out waiting for RPC trace writer to close current file.");
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
                PraetorisClientPlugin.Log.LogWarning("RPC trace writer did not exit cleanly before timeout.");

            queue.Dispose();
            _hasActiveWriter = false;
        }

        internal static List<string> GetFlushableFiles()
        {
            CloseCurrentFile();
            List<string> files = Directory.Exists(_pendingDirectory)
                ? new List<string>(Directory.GetFiles(_pendingDirectory, "*.jsonl"))
                : new List<string>();
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        internal static List<string> ReadBatch(string path, int startLine, int maxRows, out bool reachedEnd)
        {
            List<string> rows = new(Math.Max(1, maxRows));
            reachedEnd = true;

            using StreamReader reader = new(path, Encoding.UTF8);
            int lineIndex = 0;
            while (reader.ReadLine() is { } line)
            {
                if (lineIndex++ < startLine)
                    continue;

                if (rows.Count >= maxRows)
                {
                    reachedEnd = false;
                    break;
                }

                if (line.Length > 0)
                    rows.Add(line);
            }

            return rows;
        }

        internal static void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to delete uploaded RPC trace file {path}: {ex.Message}");
            }
        }

        internal static bool HasPendingFiles()
        {
            BlockingCollection<WorkItem>? queue = _queue;
            if (_hasActiveWriter || (queue != null && queue.Count > 0))
                return true;

            return Directory.Exists(_pendingDirectory) && Directory.GetFiles(_pendingDirectory, "*.jsonl").Length > 0;
        }

        internal static string BuildFileId(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(fileName) ? Guid.NewGuid().ToString("N") : fileName;
        }

        private static void WriterLoop(object? state)
        {
            BlockingCollection<WorkItem> queue = (BlockingCollection<WorkItem>)state!;
            StreamWriter? writer = null;
            string? currentPath = null;
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
                        FlushAndClose(ref writer, ref currentPath, ref hasUnflushedRows);
                        SignalCompleted(item.Completed);
                        continue;
                    }

                    try
                    {
                        EnsureWriter(ref writer, ref currentPath, item.LocalPeerId, item.WorldUid);
                        writer?.WriteLine(item.Line);
                        hasUnflushedRows = true;
                        _hasActiveWriter = true;
                        FlushIfNeeded(writer, ref hasUnflushedRows, ref nextFlushUtc);
                    }
                    catch (Exception ex)
                    {
                        PraetorisClientPlugin.Log.LogWarning("Failed to write RPC trace row: " + ex.Message);
                    }
                }
            }
            finally
            {
                FlushAndClose(ref writer, ref currentPath, ref hasUnflushedRows);
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
            string fileName = "rpc_trace_"
                + timestamp
                + "_world_"
                + worldUid.ToString(CultureInfo.InvariantCulture)
                + "_peer_"
                + localPeerId.ToString(CultureInfo.InvariantCulture)
                + "_"
                + Guid.NewGuid().ToString("N")
                + ".jsonl";
            currentPath = Path.Combine(_pendingDirectory, fileName);
            FileStream stream = new(currentPath, FileMode.Append, FileAccess.Write, FileShare.Read, FileBufferBytes, FileOptions.SequentialScan);
            writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), FileBufferBytes)
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

        private static void FlushAndClose(ref StreamWriter? writer, ref string? currentPath, ref bool hasUnflushedRows)
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
                hasUnflushedRows = false;
                _hasActiveWriter = false;
            }
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

            internal static WorkItem Close(ManualResetEventSlim completed)
            {
                return new WorkItem(WorkItemKind.Close, "", 0L, 0L, completed);
            }

        }
    }
}
