using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;

namespace PraetorisClient
{
    internal static class RpcTraceLocalStore
    {
        private static readonly object Sync = new();
        private static string _pendingDirectory = "";
        private static string? _currentPath;
        private static StreamWriter? _writer;

        internal static void Initialize()
        {
            _pendingDirectory = Path.Combine(Paths.BepInExRootPath, "logs", "PraetorisClient", "RpcTrace", "pending");
            Directory.CreateDirectory(_pendingDirectory);
        }

        internal static void Append(string line, long localPeerId)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (Sync)
            {
                EnsureWriterLocked(localPeerId);
                if (_writer == null)
                    return;

                _writer.WriteLine(line);
                _writer.Flush();
            }
        }

        internal static void CloseCurrentFile()
        {
            lock (Sync)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
                _currentPath = null;
            }
        }

        internal static List<string> GetFlushableFiles()
        {
            lock (Sync)
            {
                CloseCurrentFile();
                List<string> files = new(Directory.GetFiles(_pendingDirectory, "*.jsonl"));
                files.Sort(StringComparer.Ordinal);
                return files;
            }
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
            lock (Sync)
            {
                if (_writer != null)
                    return true;

                return Directory.Exists(_pendingDirectory) && Directory.GetFiles(_pendingDirectory, "*.jsonl").Length > 0;
            }
        }

        internal static string BuildFileId(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(fileName) ? Guid.NewGuid().ToString("N") : fileName;
        }

        private static void EnsureWriterLocked(long localPeerId)
        {
            if (_writer != null)
                return;

            Directory.CreateDirectory(_pendingDirectory);
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
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
            _currentPath = Path.Combine(_pendingDirectory, fileName);
            _writer = new StreamWriter(_currentPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }
    }
}
