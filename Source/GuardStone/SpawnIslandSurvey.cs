using System;
using System.Collections.Generic;
using System.IO;

namespace PraetorisClient.GuardStoneFeature
{
    // Incremental four-neighbour search. Separate land masses cannot connect diagonally.
    internal sealed class SpawnIslandSurvey
    {
        private readonly Func<double, double, double> _height;
        private readonly double _waterLevel;
        private readonly long _worldId;
        private readonly int _originX;
        private readonly int _originZ;
        private readonly int _width;
        private readonly byte[] _state; // 0 unknown, 1 queued/water, 2 connected land
        private readonly bool[] _outside;
        private readonly Queue<int> _queue = new Queue<int>();
        private bool _fillingWater;
        internal bool Complete { get; private set; }
        internal int Samples { get; private set; }

        internal SpawnIslandSurvey(long worldId, double spawnX, double spawnZ, int radius,
            double waterLevel, Func<double, double, double> height)
        {
            if (radius < 64 || radius > 4096 || radius % SpawnIslandBoundary.CellSize != 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            _height = height;
            _waterLevel = waterLevel;
            _worldId = worldId;
            int half = radius / SpawnIslandBoundary.CellSize;
            _width = half * 2 + 1;
            _originX = (int)Math.Floor(spawnX / SpawnIslandBoundary.CellSize) - half;
            _originZ = (int)Math.Floor(spawnZ / SpawnIslandBoundary.CellSize) - half;
            _state = new byte[_width * _width];
            _outside = new bool[_state.Length];
            int seed = half * _width + half;
            _state[seed] = 1;
            _queue.Enqueue(seed);
        }

        internal void Step()
        {
            int budget = _fillingWater ? 4096 : 128;
            while (budget-- > 0 && _queue.Count > 0)
            {
                int index = _queue.Dequeue();
                int x = index % _width;
                int z = index / _width;
                if (!_fillingWater)
                {
                    double height = _height((_originX + x + 0.5) * SpawnIslandBoundary.CellSize,
                        (_originZ + z + 0.5) * SpawnIslandBoundary.CellSize);
                    Samples++;
                    if (double.IsNaN(height) || double.IsInfinity(height))
                        throw new InvalidDataException("Terrain returned an invalid height.");
                    if (height <= _waterLevel)
                    {
                        if (Samples == 1)
                            throw new InvalidDataException("Spawn terrain is under water. An automatic survey cannot identify this island.");
                        continue;
                    }
                    if (x <= 2 || z <= 2 || x >= _width - 3 || z >= _width - 3)
                        throw new InvalidDataException("Island reaches the survey limit. Retry with a larger radius; no boundary was saved.");
                    _state[index] = 2;
                }
                Visit(x - 1, z);
                Visit(x + 1, z);
                Visit(x, z - 1);
                Visit(x, z + 1);
            }
            if (_queue.Count != 0) return;
            if (_fillingWater)
            {
                Complete = true;
                return;
            }

            // Water connected to the edge is outside. Enclosed lakes belong to the island.
            _fillingWater = true;
            for (int i = 0; i < _width; i++)
            {
                Visit(i, 0);
                Visit(i, _width - 1);
                Visit(0, i);
                Visit(_width - 1, i);
            }
        }

        private void Visit(int x, int z)
        {
            if (x < 0 || z < 0 || x >= _width || z >= _width) return;
            int index = z * _width + x;
            if (_fillingWater)
            {
                if (_state[index] == 2 || _outside[index]) return;
                _outside[index] = true;
            }
            else
            {
                if (_state[index] != 0) return;
                _state[index] = 1;
            }
            _queue.Enqueue(index);
        }

        internal SpawnIslandBoundary Build()
        {
            if (!Complete) throw new InvalidOperationException("The island survey has not finished.");
            byte[] cells = new byte[(_state.Length + 7) / 8];
            for (int z = 0; z < _width; z++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (_outside[z * _width + x]) continue;
                    // Include one coastal cell so small unsampled strips are protected.
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int index = (z + dz) * _width + x + dx;
                            cells[index / 8] |= (byte)(1 << (index % 8));
                        }
                    }
                }
            }
            return new SpawnIslandBoundary(_worldId, _originX, _originZ, _width, cells);
        }
    }
}
