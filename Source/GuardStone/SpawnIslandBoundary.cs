using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PraetorisClient.GuardStoneFeature
{
    // A world-specific, frozen map. Runtime lookups never sample terrain.
    internal sealed class SpawnIslandBoundary
    {
        internal const int CellSize = 8;
        internal const int MaximumWidth = 1025;
        internal const int MaximumEncodedLength = 200000;
        private const int FormatVersion = 1;
        private readonly byte[] _cells;
        internal long WorldId { get; }
        internal int OriginX { get; }
        internal int OriginZ { get; }
        internal int Width { get; }

        internal SpawnIslandBoundary(long worldId, int originX, int originZ, int width, byte[] cells)
        {
            if (width < 3 || width > MaximumWidth || Math.Abs((long)originX) > 4096 ||
                Math.Abs((long)originZ) > 4096 || cells.Length != (width * width + 7) / 8)
                throw new InvalidDataException("Invalid spawn island map dimensions.");
            if (Array.TrueForAll(cells, cell => cell == 0))
                throw new InvalidDataException("Spawn island map contains no protected cells.");
            WorldId = worldId;
            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            _cells = (byte[])cells.Clone();
        }

        internal bool Contains(double x, double z)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(z) || double.IsInfinity(z))
                return false;
            double cellX = Math.Floor(x / CellSize) - OriginX;
            double cellZ = Math.Floor(z / CellSize) - OriginZ;
            return cellX >= 0 && cellX < Width && cellZ >= 0 && cellZ < Width &&
                   IsSet((int)cellX, (int)cellZ);
        }

        internal bool IsSet(int x, int z)
        {
            int index = z * Width + x;
            return (_cells[index / 8] & (1 << (index % 8))) != 0;
        }

        internal string Encode()
        {
            using MemoryStream stream = new MemoryStream();
            using (GZipStream compressed = new GZipStream(stream, CompressionMode.Compress, true))
            using (BinaryWriter writer = new BinaryWriter(compressed))
            {
                writer.Write(FormatVersion);
                writer.Write(WorldId);
                writer.Write(OriginX);
                writer.Write(OriginZ);
                writer.Write(Width);
                writer.Write(_cells);
            }
            string encoded = Convert.ToBase64String(stream.ToArray());
            if (encoded.Length > MaximumEncodedLength)
                throw new InvalidDataException("Spawn island map is too large to synchronize.");
            return encoded;
        }

        internal static SpawnIslandBoundary Decode(string encoded)
        {
            if (encoded.Length > MaximumEncodedLength)
                throw new InvalidDataException("Spawn island map exceeds the size limit.");
            using MemoryStream stream = new MemoryStream(Convert.FromBase64String(encoded));
            using GZipStream compressed = new GZipStream(stream, CompressionMode.Decompress);
            using BinaryReader reader = new BinaryReader(compressed);
            if (reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("Unsupported spawn island map version.");
            long worldId = reader.ReadInt64();
            int originX = reader.ReadInt32();
            int originZ = reader.ReadInt32();
            int width = reader.ReadInt32();
            if (width < 3 || width > MaximumWidth)
                throw new InvalidDataException("Invalid spawn island map width.");
            byte[] cells = reader.ReadBytes((width * width + 7) / 8);
            SpawnIslandBoundary boundary = new SpawnIslandBoundary(worldId, originX, originZ, width, cells);
            if (reader.Read() != -1)
                throw new InvalidDataException("Unexpected data after spawn island map.");
            return boundary;
        }

        internal string ToSvg()
        {
            StringBuilder svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
                .Append(Width).Append(' ').Append(Width).Append("\"><title>Spawn island ward restriction. Gold is protected. Blue is unprotected. North is up. Each cell is 8 metres.</title>")
                .Append("<desc>World ").Append(WorldId).Append(". Southwest corner: X ").Append(OriginX * CellSize)
                .Append(", Z ").Append(OriginZ * CellSize).Append(" metres. Red cross marks the spawn cell.</desc>")
                .Append("<rect width=\"100%\" height=\"100%\" fill=\"#16324f\"/><g fill=\"#e8c36a\">");
            for (int z = 0; z < Width; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (!IsSet(x, z)) continue;
                    int start = x;
                    while (x + 1 < Width && IsSet(x + 1, z)) x++;
                    svg.Append("<rect x=\"").Append(start).Append("\" y=\"").Append(Width - 1 - z)
                        .Append("\" width=\"").Append(x - start + 1).Append("\" height=\"1\"/>");
                }
            }
            int center = Width / 2;
            return svg.Append("</g><path stroke=\"#e63946\" stroke-width=\"1\" d=\"M ")
                .Append(center - 4).Append(' ').Append(center).Append(" h 8 M ")
                .Append(center).Append(' ').Append(center - 4).Append(" v 8\"/></svg>").ToString();
        }
    }
}
