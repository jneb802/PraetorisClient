using System;
using System.Collections.Generic;
using System.Globalization;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestRpc
    {
        private const int RegisterResponse = 1;
        private const int CommandResponse = 2;

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZPackage>(RpcNames.ServerChestRegisterRequest, OnRegisterRequest);
            rpc.Register<ZPackage>(RpcNames.ServerChestRegisterResponse, OnRegisterResponse);
            rpc.Register<ZPackage>(RpcNames.ServerChestCommandRequest, OnCommandRequest);
            rpc.Register<ZPackage>(RpcNames.ServerChestCommandResponse, OnCommandResponse);
        }

        internal static void RequestRegistration(ZDOID zdoId, string characterName, string platformId)
        {
            if (ZRoutedRpc.instance == null)
            {
                ServerChest.ShowMessage("ServerChest RPC is unavailable.");
                return;
            }

            ZPackage package = new();
            package.Write(zdoId);
            package.Write(characterName ?? "");
            package.Write(platformId ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.ServerChestRegisterRequest, package);
        }

        internal static void RequestSend(string characterName, IReadOnlyList<ServerChestService.SendItem> items)
        {
            ZPackage package = new();
            package.Write("send");
            package.Write(characterName ?? "");
            package.Write(items.Count);
            foreach (ServerChestService.SendItem item in items)
            {
                package.Write(item.PrefabName);
                package.Write(item.Amount);
                package.Write(item.Quality);
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.ServerChestCommandRequest, package);
        }

        internal static void RequestStatus(string characterName)
        {
            ZPackage package = new();
            package.Write("status");
            package.Write(characterName ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.ServerChestCommandRequest, package);
        }

        internal static void RequestFind(string characterName)
        {
            ZPackage package = new();
            package.Write("find");
            package.Write(characterName ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.ServerChestCommandRequest, package);
        }

        private static void OnRegisterRequest(long sender, ZPackage package)
        {
            ZDOID zdoId = package.ReadZDOID();
            string characterName = package.ReadString();
            string platformId = package.ReadString();
            ServerChestService.CommandResult result = ServerChestService.RegisterChest(zdoId, sender, characterName, platformId);
            SendResponse(sender, RpcNames.ServerChestRegisterResponse, RegisterResponse, result);
        }

        private static void OnRegisterResponse(long sender, ZPackage package)
        {
            package.ReadInt();
            bool success = package.ReadBool();
            string message = package.ReadString();
            ServerChest.ShowMessage(message);
            if (!success)
            {
                PraetorisClientPlugin.Log.LogInfo("ServerChest registration failed: " + message);
            }
        }

        private static void OnCommandRequest(long sender, ZPackage package)
        {
            if (!SenderIsAdmin(sender))
            {
                SendResponse(sender, RpcNames.ServerChestCommandResponse, CommandResponse, ServerChestService.CommandResult.Fail("You are not admin."));
                return;
            }

            string operation = package.ReadString();
            ServerChestService.CommandResult result;
            if (operation == "send")
            {
                string characterName = package.ReadString();
                int count = package.ReadInt();
                List<ServerChestService.SendItem> items = new();
                for (int index = 0; index < count; index++)
                {
                    items.Add(new ServerChestService.SendItem
                    {
                        PrefabName = package.ReadString(),
                        Amount = package.ReadInt(),
                        Quality = package.ReadInt()
                    });
                }

                result = ServerChestService.SendItems(characterName, items);
            }
            else if (operation == "status")
            {
                result = ServerChestService.Status(package.ReadString());
            }
            else if (operation == "find")
            {
                result = ServerChestService.Find(package.ReadString());
            }
            else
            {
                result = ServerChestService.CommandResult.Fail("Unknown ServerChest operation: " + operation + ".");
            }

            SendResponse(sender, RpcNames.ServerChestCommandResponse, CommandResponse, result);
        }

        private static void OnCommandResponse(long sender, ZPackage package)
        {
            package.ReadInt();
            bool success = package.ReadBool();
            string message = package.ReadString();
            if (Console.instance != null)
            {
                Console.instance.AddString(message);
            }

            if (!success)
            {
                ServerChest.ShowMessage(message);
            }
        }

        private static void SendResponse(long target, string rpcName, int responseType, ServerChestService.CommandResult result)
        {
            ZPackage response = new();
            response.Write(responseType);
            response.Write(result.Success);
            response.Write(result.Message);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, rpcName, response);
        }

        private static bool SenderIsAdmin(long sender)
        {
            if (ZNet.instance == null)
            {
                return false;
            }

            if (sender == ZNet.GetUID() && ZNet.instance.IsServer())
            {
                return true;
            }

            ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
            if (peer == null)
            {
                return false;
            }

            string hostName = PlayerResolver.SafeHostName(peer);
            return !string.IsNullOrWhiteSpace(hostName) && ZNet.instance.IsAdmin(hostName);
        }
    }
}
