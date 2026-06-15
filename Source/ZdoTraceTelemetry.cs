using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PraetorisClient
{
    internal static class ZdoTraceTelemetry
    {
        [ThreadStatic]
        private static ReceiveContext? _activeReceiveContext;

        private static readonly object FilterSync = new();
        private static readonly HashSet<int> PrefabFilter = new();
        private static readonly HashSet<string> ZdoIdFilter = new(StringComparer.Ordinal);
        private static string _lastPrefabFilter = "";
        private static string _lastZdoIdFilter = "";
        private static FieldInfo? _deadZdosField;
        private static int _rateSecond = -1;
        private static int _rateCount;

        internal static void TracePackageSend(ZRpc rpc, ZPackage package)
        {
            bool canCapture = CanCapture();
            if (!canCapture || package == null)
                return;

            long receiverPeerId = RpcTraceTelemetry.GetPeerIdForRpc(rpc);
            long senderPeerId = RpcTraceTelemetry.GetLocalPeerId();
            ZdoPackageTrace? trace = TryParsePackage(package, senderPeerId, receiverPeerId);
            if (trace == null)
                return;

            WritePackageEvent("zdo_package_send", trace);

            foreach (ZdoTraceItem item in trace.Items)
            {
                if (!ShouldCaptureItem(item))
                    continue;

                WriteRevisionEvent("zdo_revision_send", trace, item, "sent", false);
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
                string packageHash = HashBytes(packageBytes);
                string packageId = BuildPackageId(senderPeerId, receiverPeerId, packageHash);
                ZPackage copy = new(packageBytes);
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
                    string prefabName = ResolvePrefabName(payloadHeader.PrefabHash);
                    string zdoIdText = FormatZdoId(zdoId);
                    items.Add(new ZdoTraceItem(
                        zdoId,
                        zdoIdText,
                        BuildZdoTraceId(zdoId, dataRevision),
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
                    packageBytes.Length,
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

        private static bool TryReserveEvent()
        {
            int maxEvents = PraetorisClientPlugin.ZdoTraceMaxEventsPerSecond.Value;
            if (maxEvents <= 0)
                return true;

            int second = Mathf.FloorToInt(Time.realtimeSinceStartup);
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
            if (!TryReserveEvent())
                return;

            long localPeerId = RpcTraceTelemetry.GetLocalPeerId();
            TelemetryJson json = RpcTraceTelemetry.ObjectWithEnvelope(eventName, localPeerId);
            json.Prop("role", "client");
            AddPackageFields(json, trace, localPeerId);
            RpcTraceTelemetry.AddClockFields(json);
            json.End();
            RpcTraceLocalStore.Append(json.ToString(), localPeerId);
        }

        private static bool WriteRevisionEvent(string eventName, ZdoPackageTrace trace, ZdoTraceItem item, string outcome, bool force)
        {
            if (!force && !TryReserveEvent())
                return false;

            long localPeerId = RpcTraceTelemetry.GetLocalPeerId();
            TelemetryJson json = RpcTraceTelemetry.ObjectWithEnvelope(eventName, localPeerId);
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
            RpcTraceTelemetry.AddClockFields(json);
            json.End();
            RpcTraceLocalStore.Append(json.ToString(), localPeerId);
            return true;
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

            try
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
                return prefab != null ? prefab.name : "";
            }
            catch
            {
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

        private static string BuildPackageId(long senderPeerId, long receiverPeerId, string packageHash)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            return worldUid.ToString(CultureInfo.InvariantCulture)
                + ":"
                + senderPeerId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + receiverPeerId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + packageHash;
        }

        private static string BuildZdoTraceId(ZDOID id, uint dataRevision)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
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

        private static string HashBytes(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            StringBuilder builder = new(hash.Length * 2);
            foreach (byte value in hash)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
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
