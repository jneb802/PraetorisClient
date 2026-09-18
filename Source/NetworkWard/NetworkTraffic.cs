using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.NetworkWardFeature
{
    internal sealed class TrafficObject
    {
        internal ZNetView View = null!;
        internal ZDOID Id;
        internal string Name = "";
        internal string Prefab = "";
        internal float Distance;
        internal long Sent;
        internal long Received;
        internal long StateBytes;
        internal long RpcBytes;
        internal readonly int[] Seconds = new int[11];
        internal readonly long[] Out = new long[11];
        internal readonly long[] In = new long[11];
        internal readonly long[] State = new long[11];
        internal readonly long[] Rpc = new long[11];

        internal void Add(int second, int bytes, bool sent, bool rpc)
        {
            int slot = second % 11;
            if (Seconds[slot] != second)
            {
                Seconds[slot] = second;
                Out[slot] = In[slot] = State[slot] = Rpc[slot] = 0;
            }
            if (sent) Out[slot] += bytes; else In[slot] += bytes;
            if (rpc) Rpc[slot] += bytes; else State[slot] += bytes;
        }

        internal void Sum(int second)
        {
            Sent = Received = StateBytes = RpcBytes = 0;
            for (int i = 0; i < 11; i++)
            {
                // Ten complete seconds: stable denominator and no partial-bucket spike.
                if (Seconds[i] < second - 10 || Seconds[i] >= second) continue;
                Sent += Out[i]; Received += In[i]; StateBytes += State[i]; RpcBytes += Rpc[i];
            }
        }
    }

    internal static class NetworkTraffic
    {
        private static readonly Dictionary<(long, uint), TrafficObject> Objects = new Dictionary<(long, uint), TrafficObject>();
        private static readonly Action<long, uint, int, bool> RecordSent = (user, id, bytes, rpc) => Record(user, id, bytes, rpc, true);
        private static readonly Action<long, uint, int, bool> RecordReceived = (user, id, bytes, rpc) => Record(user, id, bytes, rpc, false);
        private static readonly int StateHash = "ZDOData".GetStableHashCode();
        private static readonly int RpcHash = "RoutedRPC".GetStableHashCode();
        internal static bool Active { get; private set; }
        internal static int ParseErrors { get; private set; }
        internal static float Radius { get; private set; } = 40f;
        internal static Vector3 Center { get; private set; }
        internal static List<TrafficObject> Rows { get; private set; } = new List<TrafficObject>();
        private static int _startSecond;
        internal static float Seconds => Mathf.Clamp(Mathf.FloorToInt(Time.realtimeSinceStartup) - _startSecond, 1, 10);

        internal static void Start(Vector3 center, float radius)
        {
            Objects.Clear(); Rows.Clear(); ParseErrors = 0;
            Center = center; Radius = radius;
            _startSecond = Mathf.FloorToInt(Time.realtimeSinceStartup);
            Active = true;
            Refresh();
        }

        internal static void Stop() { Active = false; Objects.Clear(); Rows.Clear(); }

        internal static void Refresh()
        {
            if (!Active || ZNetScene.instance == null) return;
            HashSet<(long, uint)> loaded = new HashSet<(long, uint)>();
            foreach (KeyValuePair<ZDO, ZNetView> pair in ZNetScene.instance.m_instances)
            {
                ZNetView view = pair.Value;
                if (view == null || !view.IsValid()) continue;
                float distance = Vector3.Distance(Center, view.transform.position);
                if (distance > Radius) continue;
                ZDOID id = pair.Key.m_uid;
                (long, uint) key = (id.UserID, id.ID);
                loaded.Add(key);
                if (!Objects.TryGetValue(key, out TrafficObject row))
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(pair.Key.GetPrefab());
                    string prefabName = prefab != null ? prefab.name : view.name.Replace("(Clone)", "");
                    Piece piece = view.GetComponent<Piece>();
                    Character character = view.GetComponent<Character>();
                    ItemDrop item = view.GetComponent<ItemDrop>();
                    string name = character is Player ? "Player" : piece != null ? piece.m_name : character != null ? character.m_name : item != null ? item.m_itemData.m_shared.m_name : prefabName;
                    row = new TrafficObject { Id = id, View = view, Prefab = prefabName, Name = Localization.instance.Localize(string.IsNullOrWhiteSpace(name) ? prefabName : name) };
                    Objects.Add(key, row);
                }
                row.View = view;
                row.Distance = distance;
                row.Sum(Mathf.FloorToInt(Time.realtimeSinceStartup));
            }
            foreach ((long, uint) key in Objects.Keys.Where(key => !loaded.Contains(key)).ToArray()) Objects.Remove(key);
            Rows = Objects.Values.OrderByDescending(row => row.Sent + row.Received).ThenBy(row => row.Name).ToList();
        }

        internal static void Observe(ZPackage package, bool sent)
        {
            if (!Active || !NetworkWardAccess.HasAccess || Player.m_localPlayer == null || package == null) return;
            try { TrafficPacketReader.Read(package.m_reader, StateHash, RpcHash, ZRpc.m_DEBUG, sent ? RecordSent : RecordReceived); }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException || exception is OverflowException)
            {
                if (ParseErrors++ == 0) PraetorisClientPlugin.Log.LogWarning("Network ward could not attribute a packet: " + exception.Message);
            }
        }

        private static void Record(long user, uint id, int bytes, bool rpc, bool sent)
        {
            // Constructing a new ZDOID registers unknown users in the game's global ID table.
            // A passive observer must not change that table while inspecting incoming bytes.
            if (Objects.TryGetValue((user, id), out TrafficObject row))
                row.Add(Mathf.FloorToInt(Time.realtimeSinceStartup), bytes, sent, rpc);
        }

        internal static string Owner(TrafficObject row)
        {
            if (row.View == null || !row.View.IsValid()) return "Unloaded";
            long owner = row.View.GetZDO().GetOwner();
            if (owner == 0) return "None";
            if (owner == ZDOMan.GetSessionID()) return "You";
            foreach (ZNet.PlayerInfo player in ZNet.instance.GetPlayerList())
                if (player.m_characterID.UserID == owner) return player.m_name;
            return owner == ZRoutedRpc.instance.GetServerPeerID() ? "Server" : "Other player";
        }
    }
}
