using System;
using System.Security.Cryptography;

namespace PraetorisClient.ShipPasswordFeature
{
    internal static class ShipPasswordData
    {
        internal const int MaximumPasswordLength = 32;
        private const int SaltLength = 16;
        private const int VerifierLength = 32;
        private const int Iterations = 100000;

        internal static readonly int SaltHash = "PraetorisShipPassword_salt".GetStableHashCode();
        internal static readonly int VerifierHash = "PraetorisShipPassword_verifier".GetStableHashCode();

        internal static bool IsProtected(ZDO? zdo)
        {
            return zdo != null && !string.IsNullOrEmpty(zdo.GetString(VerifierHash, ""));
        }

        internal static void CreateVerifier(string password, out string saltValue, out string verifierValue)
        {
            byte[] salt = new byte[SaltLength];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
            }

            byte[] verifier = Derive(password, salt);
            saltValue = Convert.ToBase64String(salt);
            verifierValue = Convert.ToBase64String(verifier);
        }

        internal static void ApplyVerifier(ZDO zdo, string saltValue, string verifierValue)
        {
            zdo.Set(SaltHash, saltValue);
            zdo.Set(VerifierHash, verifierValue);
        }

        internal static bool Verify(ZDO zdo, string password)
        {
            try
            {
                byte[] salt = Convert.FromBase64String(zdo.GetString(SaltHash, ""));
                byte[] expected = Convert.FromBase64String(zdo.GetString(VerifierHash, ""));
                if (salt.Length != SaltLength || expected.Length != VerifierLength)
                {
                    return false;
                }

                byte[] actual = Derive(password, salt);
                int difference = actual.Length ^ expected.Length;
                for (int index = 0; index < actual.Length && index < expected.Length; index++)
                {
                    difference |= actual[index] ^ expected[index];
                }

                return difference == 0;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            using (Rfc2898DeriveBytes derivation = new Rfc2898DeriveBytes(
                       password,
                       salt,
                       Iterations,
                       HashAlgorithmName.SHA256))
            {
                return derivation.GetBytes(VerifierLength);
            }
        }
    }
}
