using System;
using System.IO;
using System.Security.Cryptography;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuideImageData
    {
        internal const int MaximumImages = 8;
        internal const int MaximumBytes = 512 * 1024;
        internal const int ChunkBytes = 24 * 1024;
        internal string Name = "";
        internal string Hash = "";
        internal int Length;
        internal byte[] Bytes = Array.Empty<byte>();

        internal static string Digest(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(bytes));
        }

        internal static void Validate(byte[] bytes)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes.Length < 33 || bytes.Length > MaximumBytes)
                throw new FormatException("Each guide image must be a PNG file of at most 512 KiB.");
            for (int i = 0; i < signature.Length; i++)
                if (bytes[i] != signature[i]) throw new FormatException("Guide images must use PNG format.");
            if (ReadInt(8) != 13 || ReadInt(12) != 0x49484452)
                throw new FormatException("Invalid PNG image header.");
            uint width = ReadInt(16);
            uint height = ReadInt(20);
            if (width == 0 || height == 0 || width > 2048 || height > 2048 || width * height > 2097152)
                throw new FormatException("Guide images must be at most 2048 pixels per side and 2 megapixels total.");

            bool hasData = false;
            int offset = 8;
            while (offset <= bytes.Length - 12)
            {
                uint length = ReadInt(offset);
                if (length > bytes.Length - offset - 12) throw new FormatException("Truncated PNG image.");
                int end = offset + 8 + (int)length;
                uint crc = 0xffffffff;
                for (int index = offset + 4; index < end; index++)
                {
                    crc ^= bytes[index];
                    for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
                }
                if ((crc ^ 0xffffffff) != ReadInt(end)) throw new FormatException("PNG image checksum failed.");
                uint type = ReadInt(offset + 4);
                if (type == 0x49444154) hasData = true;
                if (type == 0x49454e44)
                {
                    if (!hasData || length != 0 || end + 4 != bytes.Length) throw new FormatException("Invalid PNG image end.");
                    return;
                }
                offset = end + 4;
            }
            throw new FormatException("Incomplete PNG image.");

            uint ReadInt(int offset) => (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16 |
                                        (uint)bytes[offset + 2] << 8 | bytes[offset + 3];
        }

        internal static GuideImageData Load(string directory, string name)
        {
            string path = Path.Combine(directory, name);
            FileInfo file = new FileInfo(path);
            if (!file.Exists) throw new FormatException("Missing guide image: " + name);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new FormatException("Guide images cannot be symbolic links: " + name);
            if (file.Length > MaximumBytes) throw new FormatException("Guide image exceeds 512 KiB: " + name);
            byte[] bytes = File.ReadAllBytes(path);
            Validate(bytes);
            return new GuideImageData { Name = name, Bytes = bytes, Length = bytes.Length, Hash = Digest(bytes) };
        }
    }
}
