using System;
using System.Collections.Generic;
using System.IO;
using PraetorisClient.NetworkWardFeature;

internal static class Program
{
    private const int State = 100, Rpc = 200;
    private static void Main()
    {
        byte[] state = Frame(State, w =>
        {
            w.Write(1); Id(w, 91, 7);
            Object(w, 11, 1, 4); Object(w, 22, 2, 19); Id(w, 0, 0);
        });
        List<(long, uint, int, bool)> rows = Parse(state);
        Check(rows.Count == 2 && rows[0] == (11L, 1U, 46, false) && rows[1] == (22L, 2U, 61, false), "ZDO byte counts");
        rows = Parse(Routed(22, 2, 13));
        Check(rows.Count == 1 && rows[0] == (22L, 2U, 57, true), "RPC envelope and payload");
        Check(Parse(Routed(0, 0, 13)).Count == 0, "Untargeted RPC excluded");
        Check(Parse(Frame(300, w => w.Write(999))).Count == 0, "Other messages ignored");
        Check(Parse(Frame(State, w => { w.Write(0); Id(w, 0, 0); }, true), true).Count == 0, "Debug framing");
        byte[] malformed = (byte[])state.Clone(); malformed[4] = 255; malformed[5] = 255;
        Throws(malformed); Throws(Frame(State, w => w.Write(-1)));
        Throws(Frame(Rpc, w => { w.Write(new byte[40]); w.Write(-1); }));
        Throws(Frame(State, w => { w.Write(0); Object(w, 1, 1, 0); }));
        Console.WriteLine("PASS: state, RPC, exclusions, debug framing, invalid lengths, terminator and cursor preservation.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Id(BinaryWriter w, long user, uint id) { w.Write(user); w.Write(id); }
    private static void Object(BinaryWriter w, long user, uint id, int payload)
    {
        Id(w, user, id); w.Write((ushort)1); w.Write(2U); w.Write(3L);
        w.Write(1f); w.Write(2f); w.Write(3f); w.Write(payload); w.Write(new byte[payload]);
    }
    private static byte[] Routed(long user, uint id, int payload) => Frame(Rpc, w =>
    {
        w.Write(1L); w.Write(2L); w.Write(3L); Id(w, user, id); w.Write(42); w.Write(payload); w.Write(new byte[payload]);
    });
    private static byte[] Frame(int hash, Action<BinaryWriter> write, bool debug = false)
    {
        using MemoryStream payload = new MemoryStream(); using BinaryWriter body = new BinaryWriter(payload); write(body);
        using MemoryStream frame = new MemoryStream(); using BinaryWriter w = new BinaryWriter(frame);
        w.Write(hash); if (debug) w.Write("DebugMethod"); w.Write((int)payload.Length); w.Write(payload.ToArray()); return frame.ToArray();
    }
    private static List<(long, uint, int, bool)> Parse(byte[] frame, bool debug = false)
    {
        using MemoryStream stream = new MemoryStream(frame); using BinaryReader reader = new BinaryReader(stream);
        stream.Position = 3;
        List<(long, uint, int, bool)> rows = new List<(long, uint, int, bool)>();
        try { TrafficPacketReader.Read(reader, State, Rpc, debug, (user, id, bytes, rpc) => rows.Add((user, id, bytes, rpc))); }
        finally { Check(stream.Position == 3, "Cursor restored even on failure"); }
        return rows;
    }
    private static void Throws(byte[] frame)
    {
        try { Parse(frame); } catch (IOException) { return; } catch (InvalidDataException) { return; }
        throw new Exception("Malformed frame accepted");
    }
}
