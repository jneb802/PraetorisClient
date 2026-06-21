using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace PraetorisClient
{
    internal static class ZdoTraceTelemetry
    {
        private const int WorkerDrainTimeoutMilliseconds = 10000;

        [ThreadStatic]
        private static ReceiveContext? _activeReceiveContext;

        private static readonly object FilterSync = new();
        private static readonly HashSet<int> PrefabFilter = new();
        private static readonly HashSet<string> ZdoIdFilter = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> PrefabNameCache = new();
        private static readonly object WorkerSync = new();
        private static BlockingCollection<ZdoPackageSendWorkItem>? _sendQueue;
        private static Thread? _sendWorker;
        private static Dictionary<int, string> _prefabNameSnapshot = new();
        private static FieldInfo? _zPackageStreamField;
        private static string _lastPrefabFilter = "";
        private static string _lastZdoIdFilter = "";
        private static FieldInfo? _deadZdosField;
        private static int _rateSecond = -1;
        private static int _rateCount;
        private static double _nextPrefabSnapshotRealtime;

        internal static void Initialize()
        {
            lock (WorkerSync)
            {
                if (_sendWorker != null && _sendWorker.IsAlive)
                    return;

                _sendQueue = new BlockingCollection<ZdoPackageSendWorkItem>();
                _sendWorker = new Thread(SendWorkerLoop)
                {
                    IsBackground = true,
                    Name = "PraetorisClient ZDO send trace worker"
                };
                _sendWorker.Start(_sendQueue);
            }
        }

        internal static void Shutdown()
        {
            BlockingCollection<ZdoPackageSendWorkItem>? queue;
            Thread? worker;
            lock (WorkerSync)
            {
                queue = _sendQueue;
                worker = _sendWorker;
                _sendQueue = null;
                _sendWorker = null;
            }

            if (queue == null)
                return;

            queue.CompleteAdding();
            if (worker != null && worker.IsAlive && !worker.Join(WorkerDrainTimeoutMilliseconds))
                PraetorisClientPlugin.Log.LogWarning("Timed out waiting for ZDO send trace worker to drain.");

            queue.Dispose();
        }

        internal static void DrainPending()
        {
            BlockingCollection<ZdoPackageSendWorkItem>? queue = _sendQueue;
            if (queue == null || queue.IsAddingCompleted)
                return;

            using ManualResetEventSlim completed = new(false);
            try
            {
                queue.Add(ZdoPackageSendWorkItem.Barrier(completed));
                if (!completed.Wait(WorkerDrainTimeoutMilliseconds))
                    PraetorisClientPlugin.Log.LogWarning("Timed out waiting for pending ZDO send trace rows.");
            }
            catch (InvalidOperationException)
            {
            }
        }

        internal static void TracePackageSend(ZRpc rpc, ZPackage package)
        {
            bool canCapture = CanCapture();
            if (!canCapture || package == null)
                return;

            long receiverPeerId = RpcTraceTelemetry.GetPeerIdForRpc(rpc);
            long senderPeerId = RpcTraceTelemetry.GetLocalPeerId();
            if (!TryGetPackageBuffer(package, out byte[] packageBytes, out int packageSize))
                return;

            SnapshotPrefabNamesIfDue();
            BlockingCollection<ZdoPackageSendWorkItem>? queue = _sendQueue;
            if (queue == null || queue.IsAddingCompleted)
                return;

            try
            {
                queue.Add(new ZdoPackageSendWorkItem(
                    packageBytes,
                    packageSize,
                    senderPeerId,
                    receiverPeerId,
                    RpcTraceTelemetry.CaptureEnvelopeContext(senderPeerId)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        internal static void BeginReceive(ZRpc rpc, ZPackage package)
        {
            bool canCapture = CanCapture();
            if (!canCapture || package == null)
                return;

            long senderPeerId = RpcTraceTelemetry.GetPeerIdForRpc(rpc);
            long receiverPeerId = RpcTraceTelemetry.GetLocalPeerId();
            ZdoPackageTrace? trace = TryParsePackage(package, senderPeerId, receiverPeerId);
            if (trace == null)
                return;

            if (_activeReceiveContext != null && _activeReceiveContext.Trace.Matches(trace))
                return;

            EndReceive();

            ReceiveContext context = new(trace);
            _activeReceiveContext = context;

            WritePackageEvent("zdo_package_receive", trace);

            foreach (ZdoTraceItem item in trace.Items)
            {
                PopulateReceiveOutcome(item);
                if (!ShouldCaptureItem(item))
                    continue;

                item.Captured = WriteRevisionEvent("zdo_revision_receive", trace, item, item.ApplyOutcome, false);
                if (ShouldDeserialize(item))
                    context.ApplyItems.Add(item);
                else if (item.Captured)
                    WriteRevisionEvent("zdo_revision_skip", trace, item, item.ApplyOutcome, true);
            }
        }

        internal static void BeginReceiveFromRpcPackage(ZRpc rpc, ZPackage package)
        {
            if (!CanCapture() || package == null)
                return;

            try
            {
                ZPackage copy = new(package.GetArray());
                int methodHash = copy.ReadInt();
                if (methodHash != "ZDOData".GetStableHashCode())
                    return;

                BeginReceive(rpc, copy.ReadPackage());
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to read inbound ZDOData RPC package: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void EndReceive()
        {
            _activeReceiveContext = null;
        }

        internal static void OnDeserializeComplete(ZDO zdo)
        {
            ReceiveContext? context = _activeReceiveContext;
            if (context == null || zdo == null)
                return;

            ZdoTraceItem? item = context.TakeNextApplyItem(zdo.m_uid);
            if (item == null || !item.Captured)
                return;

            WriteRevisionEvent("zdo_revision_apply", context.Trace, item, item.ApplyOutcome, true);
        }

        private static bool CanCapture()
        {
            return PraetorisClientPlugin.ZdoTraceEnabled.Value
                && RpcTraceTelemetry.CanCaptureZdoTrace();
        }

        private static ZdoPackageTrace? TryParsePackage(ZPackage package, long senderPeerId, long receiverPeerId)
        {
            try
            {
                byte[] packageBytes = package.GetArray();
                long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
                return TryParsePackageBytes(packageBytes, packageBytes.Length, senderPeerId, receiverPeerId, worldUid, usePrefabSnapshot: false);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to copy ZDOData trace package: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static ZdoPackageTrace? TryParsePackageBytes(byte[] packageBytes, int packageSize, long senderPeerId, long receiverPeerId, long worldUid, bool usePrefabSnapshot)
        {
            try
            {
                string packageHash = HashBytes(packageBytes, packageSize);
                string packageId = BuildPackageId(worldUid, senderPeerId, receiverPeerId, packageHash);
                ZPackage copy = new(packageBytes, packageSize);
                int invalidSectorCount = copy.ReadInt();
                for (int index = 0; index < invalidSectorCount; index++)
                    copy.ReadZDOID();

                List<ZdoTraceItem> items = new();
                while (copy.GetPos() < copy.Size())
                {
                    ZDOID zdoId = copy.ReadZDOID();
                    if (zdoId.IsNone())
                        break;

                    ushort ownerRevision = copy.ReadUShort();
                    uint dataRevision = copy.ReadUInt();
                    long ownerPeerId = copy.ReadLong();
                    Vector3 position = copy.ReadVector3();
                    ZPackage itemPayload = copy.ReadPackage();
                    int itemPayloadBytes = itemPayload.Size();
                    ZdoPayloadHeader payloadHeader = ReadPayloadHeader(itemPayload);
                    string prefabName = usePrefabSnapshot
                        ? ResolvePrefabNameFromSnapshot(payloadHeader.PrefabHash)
                        : ResolvePrefabName(payloadHeader.PrefabHash);
                    string zdoIdText = FormatZdoId(zdoId);
                    items.Add(new ZdoTraceItem(
                        zdoId,
                        zdoIdText,
                        BuildZdoTraceId(worldUid, zdoId, dataRevision),
                        ownerRevision,
                        dataRevision,
                        ownerPeerId,
                        position,
                        itemPayloadBytes,
                        payloadHeader.Flags,
                        payloadHeader.ExtraDataMask,
                        payloadHeader.PrefabHash,
                        prefabName));
                }

                return new ZdoPackageTrace(
                    packageId,
                    packageHash,
                    senderPeerId,
                    receiverPeerId,
                    packageSize,
                    invalidSectorCount,
                    items);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to parse ZDOData trace package: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void PopulateReceiveOutcome(ZdoTraceItem item)
        {
            ZDO? current = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(item.ZdoId) : null;
            item.ExistedBefore = current != null;
            item.LocalDataRevision = current != null ? current.DataRevision : 0U;
            item.LocalOwnerRevision = current != null ? current.OwnerRevision : (ushort)0;

            if (current == null)
            {
                item.ApplyOutcome = IsServerDeadZdo(item.ZdoId) ? "created_then_destroyed" : "created";
                return;
            }

            if (item.DataRevision > current.DataRevision)
            {
                item.ApplyOutcome = "updated";
                return;
            }

            item.ApplyOutcome = item.OwnerRevision > current.OwnerRevision ? "owner_only" : "stale";
        }

        private static bool ShouldDeserialize(ZdoTraceItem item)
        {
            return item.ApplyOutcome == "created"
                || item.ApplyOutcome == "created_then_destroyed"
                || item.ApplyOutcome == "updated";
        }

        private static bool ShouldCaptureItem(ZdoTraceItem item)
        {
            RefreshFilters();

            lock (FilterSync)
            {
                if (item.PrefabHash != 0 && PrefabFilter.Contains(item.PrefabHash))
                    return true;

                if (ZdoIdFilter.Contains(item.ZdoIdText))
                    return true;
            }

            float sampleRate = Math.Max(0f, PraetorisClientPlugin.ZdoTraceSampleRate.Value);
            if (sampleRate >= 1f)
                return true;
            if (sampleRate <= 0f)
                return false;

            uint hash = unchecked((uint)item.ZdoTraceId.GetStableHashCode());
            double normalized = hash / (double)uint.MaxValue;
            return normalized < sampleRate;
        }

        private static void RefreshFilters()
        {
            string prefabConfig = PraetorisClientPlugin.ZdoTracePrefabFilter.Value ?? "";
            string zdoConfig = PraetorisClientPlugin.ZdoTraceZdoIdFilter.Value ?? "";
            lock (FilterSync)
            {
                if (!string.Equals(prefabConfig, _lastPrefabFilter, StringComparison.Ordinal))
                {
                    PrefabFilter.Clear();
                    foreach (string token in prefabConfig.Split(','))
                    {
                        string trimmed = token.Trim();
                        if (trimmed.Length == 0)
                            continue;

                        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hash))
                            PrefabFilter.Add(hash);
                        else
                            PrefabFilter.Add(trimmed.GetStableHashCode());
                    }

                    _lastPrefabFilter = prefabConfig;
                }

                if (string.Equals(zdoConfig, _lastZdoIdFilter, StringComparison.Ordinal))
                    return;

                ZdoIdFilter.Clear();
                foreach (string token in zdoConfig.Split(','))
                {
                    string trimmed = token.Trim();
                    if (trimmed.Length > 0)
                        ZdoIdFilter.Add(trimmed);
                }

                _lastZdoIdFilter = zdoConfig;
            }
        }

        private static bool TryReserveEvent(double realtime)
        {
            int maxEvents = PraetorisClientPlugin.ZdoTraceMaxEventsPerSecond.Value;
            if (maxEvents <= 0)
                return true;

            int second = Mathf.FloorToInt((float)realtime);
            lock (FilterSync)
            {
                if (second != _rateSecond)
                {
                    _rateSecond = second;
                    _rateCount = 0;
                }

                if (_rateCount >= maxEvents)
                    return false;

                _rateCount++;
                return true;
            }
        }

        private static void WritePackageEvent(string eventName, ZdoPackageTrace trace)
        {
            WritePackageEvent(eventName, trace, null);
        }

        private static void WritePackageEvent(string eventName, ZdoPackageTrace trace, RpcTraceTelemetry.TraceEnvelopeContext? context)
        {
            double realtime = context?.Realtime ?? Time.realtimeSinceStartupAsDouble;
            if (!TryReserveEvent(realtime))
                return;

            long localPeerId = context?.LocalPeerId ?? RpcTraceTelemetry.GetLocalPeerId();
            TelemetryJson json = context.HasValue
                ? RpcTraceTelemetry.ObjectWithEnvelope(eventName, context.Value)
                : RpcTraceTelemetry.ObjectWithEnvelope(eventName, localPeerId);
            json.Prop("role", "client");
            AddPackageFields(json, trace, localPeerId);
            WriteTraceRow(json, localPeerId, context);
        }

        private static bool WriteRevisionEvent(string eventName, ZdoPackageTrace trace, ZdoTraceItem item, string outcome, bool force)
        {
            return WriteRevisionEvent(eventName, trace, item, outcome, force, null);
        }

        private static bool WriteRevisionEvent(
            string eventName,
            ZdoPackageTrace trace,
            ZdoTraceItem item,
            string outcome,
            bool force,
            RpcTraceTelemetry.TraceEnvelopeContext? context)
        {
            double realtime = context?.Realtime ?? Time.realtimeSinceStartupAsDouble;
            if (!force && !TryReserveEvent(realtime))
                return false;

            long localPeerId = context?.LocalPeerId ?? RpcTraceTelemetry.GetLocalPeerId();
            TelemetryJson json = context.HasValue
                ? RpcTraceTelemetry.ObjectWithEnvelope(eventName, context.Value)
                : RpcTraceTelemetry.ObjectWithEnvelope(eventName, localPeerId);
            json.Prop("role", "client");
            AddPackageFields(json, trace, localPeerId);
            json.Prop("zdoTraceId", item.ZdoTraceId);
            json.Prop("zdoId", item.ZdoIdText);
            json.Prop("targetZdo", item.ZdoIdText);
            json.Prop("zdoUserId", item.ZdoId.UserID);
            json.Prop("zdoLocalId", (long)item.ZdoId.ID);
            json.Prop("zdoDataRevision", item.DataRevision);
            json.Prop("zdoOwnerRevision", item.OwnerRevision);
            json.Prop("zdoOwnerPeerId", item.OwnerPeerId);
            json.Prop("zdoPrefabHash", item.PrefabHash);
            json.Prop("zdoPrefabName", item.PrefabName);
            json.Prop("zdoPayloadFlags", item.PayloadFlags);
            json.Prop("zdoExtraDataMask", item.ExtraDataMask);
            json.Prop("zdoHasExtraData", item.HasExtraData);
            json.Prop("positionX", item.Position.x);
            json.Prop("positionY", item.Position.y);
            json.Prop("positionZ", item.Position.z);
            json.Prop("itemPayloadBytes", item.ItemPayloadBytes);
            json.Prop("payloadBytes", item.ItemPayloadBytes);
            json.Prop("applyOutcome", outcome);
            json.Prop("existedBefore", item.ExistedBefore);
            json.Prop("localDataRevision", item.LocalDataRevision);
            json.Prop("localOwnerRevision", item.LocalOwnerRevision);
            WriteTraceRow(json, localPeerId, context);
            return true;
        }

        private static void WriteTraceRow(TelemetryJson json, long localPeerId, RpcTraceTelemetry.TraceEnvelopeContext? context)
        {
            if (context.HasValue)
                RpcTraceTelemetry.AddClockFields(json, context.Value);
            else
                RpcTraceTelemetry.AddClockFields(json);
            json.End();
            RpcTraceLocalStore.Append(json.ToString(), localPeerId, context?.WorldUid ?? (ZNet.m_world != null ? ZNet.m_world.m_uid : 0L));
        }

        private static void AddPackageFields(TelemetryJson json, ZdoPackageTrace trace, long localPeerId)
        {
            json.Prop("zdoPackageId", trace.PackageId);
            json.Prop("packageHash", trace.PackageHash);
            json.Prop("senderPeerId", trace.SenderPeerId);
            json.Prop("receiverPeerId", trace.ReceiverPeerId);
            json.Prop("localPeerId", localPeerId);
            json.Prop("payloadBytes", trace.PackageBytes);
            json.Prop("zdoCount", trace.Items.Count);
            json.Prop("invalidSectorCount", trace.InvalidSectorCount);
        }

        private static ZdoPayloadHeader ReadPayloadHeader(ZPackage payload)
        {
            try
            {
                ZPackage copy = new(payload.GetArray());
                int flags = copy.ReadUShort();
                int prefabHash = copy.ReadInt();
                return new ZdoPayloadHeader(flags, flags & 0xff, prefabHash);
            }
            catch
            {
                return new ZdoPayloadHeader(0, 0, 0);
            }
        }

        private static string ResolvePrefabName(int prefabHash)
        {
            if (prefabHash == 0 || ZNetScene.instance == null)
                return "";

            if (PrefabNameCache.TryGetValue(prefabHash, out string cachedName))
                return cachedName;

            try
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
                string prefabName = prefab != null ? prefab.name : "";
                PrefabNameCache[prefabHash] = prefabName;
                return prefabName;
            }
            catch
            {
                PrefabNameCache[prefabHash] = "";
                return "";
            }
        }

        private static bool IsServerDeadZdo(ZDOID id)
        {
            try
            {
                if (ZDOMan.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
                    return false;

                _deadZdosField ??= typeof(ZDOMan).GetField("m_deadZDOs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (_deadZdosField?.GetValue(ZDOMan.instance) is Dictionary<ZDOID, long> deadZdos)
                    return deadZdos.ContainsKey(id);
            }
            catch
            {
            }

            return false;
        }

        private static string BuildPackageId(long worldUid, long senderPeerId, long receiverPeerId, string packageHash)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture)
                + ":"
                + senderPeerId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + receiverPeerId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + packageHash;
        }

        private static string BuildZdoTraceId(long worldUid, ZDOID id, uint dataRevision)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture)
                + ":"
                + FormatZdoId(id)
                + ":"
                + dataRevision.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatZdoId(ZDOID id)
        {
            return id.IsNone() ? "" : id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);
        }

        private static string HashBytes(byte[] bytes, int count)
        {
            const ulong fnvOffset = 14695981039346656037UL;
            const ulong fnvPrime = 1099511628211UL;
            ulong hash = fnvOffset;
            int length = Math.Min(Math.Max(0, count), bytes.Length);
            for (int index = 0; index < length; index++)
            {
                hash ^= bytes[index];
                hash *= fnvPrime;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static bool TryGetPackageBuffer(ZPackage package, out byte[] buffer, out int size)
        {
            buffer = Array.Empty<byte>();
            size = 0;

            try
            {
                _zPackageStreamField ??= typeof(ZPackage).GetField("m_stream", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_zPackageStreamField?.GetValue(package) is System.IO.MemoryStream stream)
                {
                    size = package.Size();
                    buffer = stream.GetBuffer();
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                buffer = package.GetArray();
                size = buffer.Length;
                return true;
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to copy outbound ZDOData trace package: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static void SendWorkerLoop(object? state)
        {
            BlockingCollection<ZdoPackageSendWorkItem> queue = (BlockingCollection<ZdoPackageSendWorkItem>)state!;
            foreach (ZdoPackageSendWorkItem workItem in queue.GetConsumingEnumerable())
            {
                if (workItem.IsBarrier)
                {
                    workItem.Completed?.Set();
                    continue;
                }

                try
                {
                    ZdoPackageTrace? trace = TryParsePackageBytes(
                        workItem.PackageBytes,
                        workItem.PackageSize,
                        workItem.SenderPeerId,
                        workItem.ReceiverPeerId,
                        workItem.EnvelopeContext.WorldUid,
                        usePrefabSnapshot: true);
                    if (trace == null)
                        continue;

                    WritePackageEvent("zdo_package_send", trace, workItem.EnvelopeContext);
                    foreach (ZdoTraceItem item in trace.Items)
                    {
                        if (!ShouldCaptureItem(item))
                            continue;

                        WriteRevisionEvent("zdo_revision_send", trace, item, "sent", false, workItem.EnvelopeContext);
                    }
                }
                catch (Exception ex)
                {
                    PraetorisClientPlugin.Log.LogWarning("Failed to write outbound ZDO trace rows: " + ex.Message);
                }
            }
        }

        private static void SnapshotPrefabNamesIfDue()
        {
            double realtime = Time.realtimeSinceStartupAsDouble;
            if (realtime < _nextPrefabSnapshotRealtime)
                return;

            _nextPrefabSnapshotRealtime = realtime + 5.0;
            if (ZNetScene.instance == null)
                return;

            Dictionary<int, string> snapshot = new();
            AddPrefabNames(snapshot, ZNetScene.instance.m_prefabs);
            AddPrefabNames(snapshot, ZNetScene.instance.m_nonNetViewPrefabs);
            _prefabNameSnapshot = snapshot;
        }

        private static void AddPrefabNames(Dictionary<int, string> snapshot, List<GameObject> prefabs)
        {
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || string.IsNullOrEmpty(prefab.name))
                    continue;

                snapshot[prefab.name.GetStableHashCode()] = prefab.name;
            }
        }

        private static string ResolvePrefabNameFromSnapshot(int prefabHash)
        {
            return _prefabNameSnapshot.TryGetValue(prefabHash, out string prefabName) ? prefabName : "";
        }

        private readonly struct ZdoPackageSendWorkItem
        {
            private ZdoPackageSendWorkItem(
                bool isBarrier,
                byte[] packageBytes,
                int packageSize,
                long senderPeerId,
                long receiverPeerId,
                RpcTraceTelemetry.TraceEnvelopeContext envelopeContext,
                ManualResetEventSlim? completed)
            {
                IsBarrier = isBarrier;
                PackageBytes = packageBytes;
                PackageSize = packageSize;
                SenderPeerId = senderPeerId;
                ReceiverPeerId = receiverPeerId;
                EnvelopeContext = envelopeContext;
                Completed = completed;
            }

            internal ZdoPackageSendWorkItem(
                byte[] packageBytes,
                int packageSize,
                long senderPeerId,
                long receiverPeerId,
                RpcTraceTelemetry.TraceEnvelopeContext envelopeContext)
                : this(false, packageBytes, packageSize, senderPeerId, receiverPeerId, envelopeContext, null)
            {
            }

            internal static ZdoPackageSendWorkItem Barrier(ManualResetEventSlim completed)
            {
                return new ZdoPackageSendWorkItem(true, Array.Empty<byte>(), 0, 0L, 0L, default, completed);
            }

            internal bool IsBarrier { get; }
            internal byte[] PackageBytes { get; }
            internal int PackageSize { get; }
            internal long SenderPeerId { get; }
            internal long ReceiverPeerId { get; }
            internal RpcTraceTelemetry.TraceEnvelopeContext EnvelopeContext { get; }
            internal ManualResetEventSlim? Completed { get; }
        }

        private sealed class ReceiveContext
        {
            internal ReceiveContext(ZdoPackageTrace trace)
            {
                Trace = trace;
            }

            internal ZdoPackageTrace Trace { get; }

            internal List<ZdoTraceItem> ApplyItems { get; } = new();

            internal ZdoTraceItem? TakeNextApplyItem(ZDOID zdoId)
            {
                for (int index = 0; index < ApplyItems.Count; index++)
                {
                    ZdoTraceItem item = ApplyItems[index];
                    if (item.ZdoId != zdoId)
                        continue;

                    ApplyItems.RemoveAt(index);
                    return item;
                }

                return null;
            }
        }

        private sealed class ZdoPackageTrace
        {
            internal ZdoPackageTrace(
                string packageId,
                string packageHash,
                long senderPeerId,
                long receiverPeerId,
                int packageBytes,
                int invalidSectorCount,
                List<ZdoTraceItem> items)
            {
                PackageId = packageId;
                PackageHash = packageHash;
                SenderPeerId = senderPeerId;
                ReceiverPeerId = receiverPeerId;
                PackageBytes = packageBytes;
                InvalidSectorCount = invalidSectorCount;
                Items = items;
            }

            internal string PackageId { get; }
            internal string PackageHash { get; }
            internal long SenderPeerId { get; }
            internal long ReceiverPeerId { get; }
            internal int PackageBytes { get; }
            internal int InvalidSectorCount { get; }
            internal List<ZdoTraceItem> Items { get; }

            internal bool Matches(ZdoPackageTrace other)
            {
                return string.Equals(PackageHash, other.PackageHash, StringComparison.Ordinal)
                    && SenderPeerId == other.SenderPeerId
                    && ReceiverPeerId == other.ReceiverPeerId;
            }
        }

        private sealed class ZdoTraceItem
        {
            internal ZdoTraceItem(
                ZDOID zdoId,
                string zdoIdText,
                string zdoTraceId,
                ushort ownerRevision,
                uint dataRevision,
                long ownerPeerId,
                Vector3 position,
                int itemPayloadBytes,
                int payloadFlags,
                int extraDataMask,
                int prefabHash,
                string prefabName)
            {
                ZdoId = zdoId;
                ZdoIdText = zdoIdText;
                ZdoTraceId = zdoTraceId;
                OwnerRevision = ownerRevision;
                DataRevision = dataRevision;
                OwnerPeerId = ownerPeerId;
                Position = position;
                ItemPayloadBytes = itemPayloadBytes;
                PayloadFlags = payloadFlags;
                ExtraDataMask = extraDataMask;
                PrefabHash = prefabHash;
                PrefabName = prefabName;
            }

            internal ZDOID ZdoId { get; }
            internal string ZdoIdText { get; }
            internal string ZdoTraceId { get; }
            internal ushort OwnerRevision { get; }
            internal uint DataRevision { get; }
            internal long OwnerPeerId { get; }
            internal Vector3 Position { get; }
            internal int ItemPayloadBytes { get; }
            internal int PayloadFlags { get; }
            internal int ExtraDataMask { get; }
            internal bool HasExtraData => ExtraDataMask != 0;
            internal int PrefabHash { get; }
            internal string PrefabName { get; }
            internal string ApplyOutcome { get; set; } = "";
            internal bool ExistedBefore { get; set; }
            internal uint LocalDataRevision { get; set; }
            internal ushort LocalOwnerRevision { get; set; }
            internal bool Captured { get; set; }
        }

        private readonly struct ZdoPayloadHeader
        {
            internal ZdoPayloadHeader(int flags, int extraDataMask, int prefabHash)
            {
                Flags = flags;
                ExtraDataMask = extraDataMask;
                PrefabHash = prefabHash;
            }

            internal int Flags { get; }
            internal int ExtraDataMask { get; }
            internal int PrefabHash { get; }
        }
    }
}
