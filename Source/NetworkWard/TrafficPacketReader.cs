using System;
using System.IO;

namespace PraetorisClient.NetworkWardFeature
{
    // Reads application messages without allocating/copying payloads. The caller's cursor is preserved.
    internal static class TrafficPacketReader
    {
        internal static void Read(BinaryReader reader, int stateHash, int rpcHash, bool debug,
            Action<long, uint, int, bool> record)
        {
            Stream stream = reader.BaseStream;
            long saved = stream.Position;
            try
            {
                stream.Position = 0;
                int method = reader.ReadInt32();
                if (method != stateHash && method != rpcHash) return;
                if (debug) reader.ReadString();
                int length = reader.ReadInt32();
                long end = End(stream, length, stream.Length);
                if (method == rpcHash)
                {
                    Skip(stream, 24, end); // Message, sender and recipient IDs.
                    long user = reader.ReadInt64();
                    uint id = reader.ReadUInt32();
                    reader.ReadInt32(); // Method hash.
                    int payload = reader.ReadInt32();
                    Skip(stream, payload, end);
                    if (stream.Position != end) throw new InvalidDataException("RPC length mismatch.");
                    if (user != 0 || id != 0) record(user, id, length, true);
                    return;
                }

                int invalid = reader.ReadInt32();
                if (invalid < 0 || invalid > (end - stream.Position) / 12)
                    throw new InvalidDataException("Invalid sector count.");
                Skip(stream, invalid * 12, end);
                while (stream.Position < end)
                {
                    long start = stream.Position;
                    long user = reader.ReadInt64();
                    uint id = reader.ReadUInt32();
                    if (user == 0 && id == 0)
                    {
                        if (stream.Position != end) throw new InvalidDataException("ZDO terminator mismatch.");
                        return;
                    }
                    Skip(stream, 26, end); // Owner revision, data revision, owner, position.
                    int payload = reader.ReadInt32();
                    Skip(stream, payload, end);
                    record(user, id, checked((int)(stream.Position - start)), false);
                }
                throw new InvalidDataException("Missing ZDO terminator.");
            }
            finally { stream.Position = saved; }
        }

        private static long End(Stream stream, int length, long limit)
        {
            if (length < 0 || length > limit - stream.Position)
                throw new InvalidDataException("Invalid payload length.");
            return stream.Position + length;
        }

        private static void Skip(Stream stream, int bytes, long end) => stream.Position = End(stream, bytes, end);
    }
}
