using System;

namespace PraetorisClient.NetworkWardFeature
{
    internal static class NetworkWardAccessList
    {
        internal static bool Contains(string list, string steamId)
        {
            if (!TryNormalize(steamId, out string identity)) return false;
            foreach (string entry in (list ?? "").Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (TryNormalize(entry, out string candidate) && candidate == identity) return true;
            return false;
        }

        private static bool TryNormalize(string value, out string result)
        {
            result = (value ?? "").Trim();
            if (result.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase)) result = result.Substring(6);
            if (result.Length != 17) return false;
            foreach (char digit in result) if (digit < '0' || digit > '9') return false;
            return true;
        }
    }
}
