using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PraetorisClient
{
    internal static class FrameTimeMonitor
    {
        private static readonly object Sync = new();
        private static readonly List<float> FrameSamplesMs = new(4096);
        private static string _metricsDirectory = "";
        private static string _summaryPath = "";
        private static string _longFramePath = "";
        private static StreamWriter? _summaryWriter;
        private static StreamWriter? _longFrameWriter;
        private static float _nextSummaryTime;
        private static int _longFrameCount;
        private static float _maxFrameMs;

        internal static void Initialize()
        {
            lock (Sync)
            {
                FrameSamplesMs.Clear();
                _longFrameCount = 0;
                _maxFrameMs = 0f;
                _nextSummaryTime = 0f;
                CloseWritersLocked();
                _metricsDirectory = Path.Combine(Paths.BepInExRootPath, "logs", "PraetorisClient", "FrameMetrics");
                Directory.CreateDirectory(_metricsDirectory);
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                _summaryPath = Path.Combine(_metricsDirectory, "frame_summary_" + timestamp + ".csv");
                _longFramePath = Path.Combine(_metricsDirectory, "frame_long_" + timestamp + ".csv");
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                FlushSummaryLocked(force: true);
                CloseWritersLocked();
            }
        }

        internal static void Update()
        {
            if (!PraetorisClientPlugin.FrameMetricsEnabled.Value)
                return;

            float frameMs = Time.unscaledDeltaTime * 1000f;
            float now = Time.realtimeSinceStartup;
            float thresholdMs = Math.Max(1f, PraetorisClientPlugin.FrameMetricsLongFrameThresholdMs.Value);

            lock (Sync)
            {
                EnsureWritersLocked();
                FrameSamplesMs.Add(frameMs);
                if (frameMs > _maxFrameMs)
                    _maxFrameMs = frameMs;

                if (frameMs >= thresholdMs)
                {
                    _longFrameCount++;
                    if (PraetorisClientPlugin.FrameMetricsLogLongFrames.Value)
                        WriteLongFrameLocked(frameMs, thresholdMs);
                }

                if (_nextSummaryTime <= 0f)
                    _nextSummaryTime = now + GetSummaryIntervalSeconds();

                if (now >= _nextSummaryTime)
                {
                    FlushSummaryLocked(force: false);
                    _nextSummaryTime = now + GetSummaryIntervalSeconds();
                }
            }
        }

        private static float GetSummaryIntervalSeconds()
        {
            return Math.Max(1f, PraetorisClientPlugin.FrameMetricsSummaryIntervalSeconds.Value);
        }

        private static void EnsureWritersLocked()
        {
            if (_summaryWriter == null)
            {
                Directory.CreateDirectory(_metricsDirectory);
                _summaryWriter = new StreamWriter(_summaryPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = false
                };
                _summaryWriter.WriteLine("timeUtc,realtimeSeconds,connected,worldName,positionX,positionY,positionZ,samples,fpsAverage,frameAverageMs,frameP50Ms,frameP95Ms,frameP99Ms,frameMaxMs,longFrameThresholdMs,longFrameCount");
            }

            if (_longFrameWriter == null)
            {
                _longFrameWriter = new StreamWriter(_longFramePath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = false
                };
                _longFrameWriter.WriteLine("timeUtc,realtimeSeconds,connected,worldName,positionX,positionY,positionZ,frameMs,thresholdMs");
            }
        }

        private static void FlushSummaryLocked(bool force)
        {
            if (FrameSamplesMs.Count == 0)
                return;

            if (!force && _summaryWriter == null)
                return;

            EnsureWritersLocked();
            FrameSamplesMs.Sort();
            float averageMs = Average(FrameSamplesMs);
            float p50Ms = PercentileSorted(FrameSamplesMs, 0.50f);
            float p95Ms = PercentileSorted(FrameSamplesMs, 0.95f);
            float p99Ms = PercentileSorted(FrameSamplesMs, 0.99f);
            float thresholdMs = Math.Max(1f, PraetorisClientPlugin.FrameMetricsLongFrameThresholdMs.Value);
            float fpsAverage = averageMs > 0f ? 1000f / averageMs : 0f;
            FrameContext context = CaptureContext();

            string line = string.Join(
                ",",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture),
                context.Connected ? "1" : "0",
                Csv(context.WorldName),
                context.PositionX.ToString("F2", CultureInfo.InvariantCulture),
                context.PositionY.ToString("F2", CultureInfo.InvariantCulture),
                context.PositionZ.ToString("F2", CultureInfo.InvariantCulture),
                FrameSamplesMs.Count.ToString(CultureInfo.InvariantCulture),
                fpsAverage.ToString("F1", CultureInfo.InvariantCulture),
                averageMs.ToString("F2", CultureInfo.InvariantCulture),
                p50Ms.ToString("F2", CultureInfo.InvariantCulture),
                p95Ms.ToString("F2", CultureInfo.InvariantCulture),
                p99Ms.ToString("F2", CultureInfo.InvariantCulture),
                _maxFrameMs.ToString("F2", CultureInfo.InvariantCulture),
                thresholdMs.ToString("F1", CultureInfo.InvariantCulture),
                _longFrameCount.ToString(CultureInfo.InvariantCulture));

            _summaryWriter?.WriteLine(line);
            _summaryWriter?.Flush();
            _longFrameWriter?.Flush();
            PraetorisClientPlugin.Log.LogInfo(
                "Frame metrics summary: samples="
                + FrameSamplesMs.Count
                + ", fpsAverage="
                + fpsAverage.ToString("F1", CultureInfo.InvariantCulture)
                + ", p95Ms="
                + p95Ms.ToString("F1", CultureInfo.InvariantCulture)
                + ", p99Ms="
                + p99Ms.ToString("F1", CultureInfo.InvariantCulture)
                + ", maxMs="
                + _maxFrameMs.ToString("F1", CultureInfo.InvariantCulture)
                + ", longFramesOver"
                + thresholdMs.ToString("F0", CultureInfo.InvariantCulture)
                + "ms="
                + _longFrameCount
                + ".");

            FrameSamplesMs.Clear();
            _longFrameCount = 0;
            _maxFrameMs = 0f;
        }

        private static void WriteLongFrameLocked(float frameMs, float thresholdMs)
        {
            FrameContext context = CaptureContext();
            string line = string.Join(
                ",",
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture),
                context.Connected ? "1" : "0",
                Csv(context.WorldName),
                context.PositionX.ToString("F2", CultureInfo.InvariantCulture),
                context.PositionY.ToString("F2", CultureInfo.InvariantCulture),
                context.PositionZ.ToString("F2", CultureInfo.InvariantCulture),
                frameMs.ToString("F2", CultureInfo.InvariantCulture),
                thresholdMs.ToString("F1", CultureInfo.InvariantCulture));
            _longFrameWriter?.WriteLine(line);
        }

        private static FrameContext CaptureContext()
        {
            bool connected = ZNet.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
            string worldName = ZNet.m_world != null ? ZNet.m_world.m_name : "";
            Vector3 position = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            return new FrameContext(connected, worldName, position.x, position.y, position.z);
        }

        private static float Average(List<float> samples)
        {
            double sum = 0d;
            for (int index = 0; index < samples.Count; index++)
                sum += samples[index];

            return samples.Count > 0 ? (float)(sum / samples.Count) : 0f;
        }

        private static float PercentileSorted(List<float> sortedSamples, float percentile)
        {
            if (sortedSamples.Count == 0)
                return 0f;
            if (sortedSamples.Count == 1)
                return sortedSamples[0];

            float clamped = Mathf.Clamp01(percentile);
            float rawIndex = (sortedSamples.Count - 1) * clamped;
            int lower = Mathf.FloorToInt(rawIndex);
            int upper = Mathf.CeilToInt(rawIndex);
            if (lower == upper)
                return sortedSamples[lower];

            float fraction = rawIndex - lower;
            return Mathf.Lerp(sortedSamples[lower], sortedSamples[upper], fraction);
        }

        private static string Csv(string value)
        {
            string safeValue = value ?? "";
            if (safeValue.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return safeValue;

            return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
        }

        private static void CloseWritersLocked()
        {
            _summaryWriter?.Flush();
            _summaryWriter?.Dispose();
            _summaryWriter = null;
            _longFrameWriter?.Flush();
            _longFrameWriter?.Dispose();
            _longFrameWriter = null;
        }

        private readonly struct FrameContext
        {
            internal FrameContext(bool connected, string worldName, float positionX, float positionY, float positionZ)
            {
                Connected = connected;
                WorldName = worldName ?? "";
                PositionX = positionX;
                PositionY = positionY;
                PositionZ = positionZ;
            }

            internal bool Connected { get; }
            internal string WorldName { get; }
            internal float PositionX { get; }
            internal float PositionY { get; }
            internal float PositionZ { get; }
        }
    }
}
