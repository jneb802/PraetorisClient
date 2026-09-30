using System;
using System.Collections.Generic;
using PraetorisClient.ShipPasswordFeature;

// This checks permission and password data rules with an in-memory ZDO substitute.
// World saving, RPCs, Harmony patches, and Unity interactions require live validation.
internal static class Program
{
    private static int _checks;

    private static void Main()
    {
        ZDO boat = new ZDO();
        Check(!ShipPasswordData.IsProtected(boat), "new boat has no password");
        Check(!ShipPasswordData.HasAccess(boat, 12), "new character has no saved access");
        ShipPasswordData.CreateVerifier("test-password", out string salt, out string verifier);
        ShipPasswordData.ApplyVerifier(boat, salt, verifier);
        Check(ShipPasswordData.Verify(boat, "test-password"), "correct password accepted");
        Check(!ShipPasswordData.Verify(boat, "wrong-password"), "incorrect password rejected");
        ShipPasswordData.GrantAccess(boat, 12);
        ShipPasswordData.GrantAccess(boat, 312);
        Check(ShipPasswordData.HasAccess(boat, 12), "first character authorized");
        Check(ShipPasswordData.HasAccess(boat, 312), "second character authorized");
        Check(!ShipPasswordData.HasAccess(boat, 2), "partial character identifiers cannot match");
        string saved = boat.Snapshot();
        ShipPasswordData.GrantAccess(boat, 12);
        Check(saved == boat.Snapshot(), "repeat grants do not grow the record");
        ShipPasswordData.GrantAccess(boat, 0);
        Check(!ShipPasswordData.HasAccess(boat, 0), "invalid character cannot gain access");
        Check(saved == boat.Snapshot(), "invalid character does not alter the record");
        ShipPasswordData.CreateVerifier("replacement", out salt, out verifier);
        ShipPasswordData.ApplyVerifier(boat, salt, verifier);
        Check(ShipPasswordData.HasAccess(boat, 12), "password change retains saved access");
        Check(!ShipPasswordData.Verify(boat, "test-password"), "old password rejected after change");
        ShipPasswordData.ApplyVerifier(boat, "", "");
        Check(!ShipPasswordData.IsProtected(boat), "clearing password removes protection");
        Check(ShipPasswordData.HasAccess(boat, 312), "clearing password retains saved access");
        Check(!ShipPasswordData.HasAccess(new ZDO(), 12), "replacement boat has independent permissions");
        Check(!ShipPasswordData.HasAccess(null, 12), "missing boat has no access");
        boat.Set(ShipPasswordData.SaltHash, "invalid base64");
        Check(!ShipPasswordData.Verify(boat, "replacement"), "damaged verifier fails closed");
        Console.WriteLine($"Passed {_checks} ship password data checks.");
    }

    private static void Check(bool passed, string claim)
    {
        if (!passed)
        {
            throw new InvalidOperationException(claim);
        }

        _checks++;
    }
}

internal sealed class ZDO
{
    private readonly Dictionary<int, string> _values = new Dictionary<int, string>();
    internal string GetString(int key, string fallback) => _values.TryGetValue(key, out string? value) ? value : fallback;
    internal void Set(int key, string value) => _values[key] = value;
    internal string Snapshot() => System.Text.Json.JsonSerializer.Serialize(_values);
}

internal static class TestHash
{
    internal static int GetStableHashCode(this string value) => StringComparer.Ordinal.GetHashCode(value);
}
