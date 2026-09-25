using System.Collections.Generic;

namespace LaneBattle.Core
{
    /// <summary>결정론적 난수 (xorshift64*). 같은 시드면 어떤 플랫폼에서든 같은 수열.</summary>
    public sealed class Rng
    {
        ulong _s;

        public Rng(ulong seed)
        {
            _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextU64()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            return _s * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>[0, maxExclusive)</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;
            return (int)(NextU64() % (ulong)maxExclusive);
        }

        public bool Coin() => (NextU64() & 1UL) == 0;

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
