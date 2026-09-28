using System;
using System.IO;
using UnityEngine;

namespace PraetorisClient.CommunityChestFeature
{
    internal static class CommunityChestRpc
    {
        private const string RequestName = "Praetoris_CommunityChest_Request_v1";
        private const string ResponseName = "Praetoris_CommunityChest_Response_v1";

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZPackage>(RequestName, OnRequest);
            rpc.Register<ZPackage>(ResponseName, OnResponse);
        }

        internal static void Send(string operation, ZDOID chest, string transaction, int amount, long revision)
        {
            ZPackage package = new ZPackage();
            package.Write(operation);
            package.Write(chest);
            package.Write(transaction);
            package.Write(amount);
            package.Write(revision);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestName, package);
        }

        private static void OnRequest(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || package.Size() > 512) return;
            string operation = "";
            string transaction = "";
            try
            {
                operation = package.ReadString();
                ZDOID chestId = package.ReadZDOID();
                transaction = package.ReadString();
                int amount = package.ReadInt();
                long revision = package.ReadLong();
                ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
                string account = peer == null ? "" : PlayerResolver.SafeHostName(peer);
                if (peer == null || !peer.IsReady() || string.IsNullOrWhiteSpace(account) ||
                    !PlayerResolver.TryGetPeerPlayerId(peer, out long character))
                    throw new InvalidOperationException("A verified server connection is required.");
                ZDO player = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (operation == "place")
                {
                    if (!ZNet.instance.IsAdmin(account)) throw new InvalidOperationException("Only admins can place a Community Chest.");
                    Vector3 position = player.GetPosition() + Vector3.forward * 2f;
                    GameObject instance = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(CommunityChest.PrefabName), position, Quaternion.identity);
                    instance.GetComponent<ZNetView>().GetZDO().Persistent = true;
                    Respond(sender, operation, transaction, "Community Chest placed.", null);
                    return;
                }
                string path = CommunityChestStore.AccountPath(Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "PraetorisCommunityChests"), ZNet.instance.GetWorldUID(), account);
                CommunityChestRecord record = CommunityChestStore.Read(path);
                if (operation == "open" || operation == "prepare")
                {
                    ZDO chest = ZDOMan.instance.GetZDO(chestId);
                    if (chest == null || chest.GetPrefab() != CommunityChest.PrefabName.GetStableHashCode() ||
                        Vector3.Distance(player.GetPosition(), chest.GetPosition()) > 5f)
                        throw new InvalidOperationException("Move closer to the Community Chest.");
                    if (record.PendingTransaction.Length != 0 && record.PendingCharacter != character)
                        throw new InvalidOperationException("Open this chest with the character that started the pending transfer.");
                    if (operation == "prepare")
                    {
                        if (!Guid.TryParseExact(transaction, "N", out _)) throw new InvalidOperationException("Invalid transfer ID.");
                        if (record.PendingTransaction != transaction)
                        {
                            CommunityChestStore.Prepare(record, transaction, character, amount, revision);
                            CommunityChestStore.Write(path, record);
                        }
                    }
                }
                else if (operation == "commit")
                {
                    if (!Guid.TryParseExact(transaction, "N", out _)) throw new InvalidOperationException("Invalid transfer ID.");
                    CommunityChestStore.Commit(record, transaction, character);
                    CommunityChestStore.Write(path, record);
                }
                else if (operation == "cancel")
                {
                    if (record.PendingTransaction != transaction || record.PendingCharacter != character)
                        throw new InvalidOperationException("Transfer does not match.");
                    record.PendingTransaction = "";
                    record.PendingAmount = 0;
                    record.PendingCharacter = 0;
                    CommunityChestStore.Write(path, record);
                }
                else throw new InvalidOperationException("Unknown chest operation.");
                Respond(sender, operation, transaction, "", record);
                PraetorisClientPlugin.Log.LogInfo("CommunityChest " + operation + " balance=" + record.Balance + " revision=" + record.Revision);
            }
            catch (Exception exception)
            {
                string message = exception is InvalidOperationException ? exception.Message : "Community Chest storage is unavailable. Contact an admin.";
                if (!(exception is InvalidOperationException)) PraetorisClientPlugin.Log.LogWarning("CommunityChest request failed: " + exception.GetType().Name);
                Respond(sender, operation, transaction, message, null);
            }
        }

        private static void Respond(long sender, string operation, string transaction, string error, CommunityChestRecord? record)
        {
            ZPackage response = new ZPackage();
            response.Write(operation);
            response.Write(transaction);
            response.Write(error);
            response.Write(ZNet.instance.GetWorldUID());
            response.Write(record != null);
            if (record != null)
            {
                response.Write(record.Balance);
                response.Write(record.Revision);
                response.Write(record.PendingTransaction);
                response.Write(record.PendingAmount);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseName, response);
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || sender != ZRoutedRpc.instance.GetServerPeerID() || package.Size() > 2048) return;
            try { CommunityChestClient.Receive(package); }
            catch (Exception exception) { CommunityChestClient.Fail("Transfer paused. Reconnect and open the chest to recover."); PraetorisClientPlugin.Log.LogWarning("CommunityChest response failed: " + exception.GetType().Name); }
        }
    }
}
