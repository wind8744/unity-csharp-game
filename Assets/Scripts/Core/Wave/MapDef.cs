using System;
using System.Collections.Generic;
using System.Text;

namespace LaneBattle.Core.Wave
{
    /// <summary>
    /// 맵 = 격자 위의 구불구불한 경로 + 경로가 아닌 모든 칸이 타워 자리 (원작 공격디펜스처럼 "아무 데나" 짓되 경로 옆이 효율적).
    /// 유닛은 경로를 따라 걷는다 (진행도 = 밀리칸). 좌표는 칸 중심 = 칸 × 1000 + 500.
    /// ASCII 로 정의: '.' 자리, '#' 경로, 'S' 출발(경로), 'B' 기지(경로 끝), ' ' 빈 땅(못 지음). 첫 줄이 맵의 위쪽.
    /// </summary>
    public sealed class MapDef
    {
        public string Name;
        public int W, H;
        public readonly List<(int x, int y)> Path = new List<(int, int)>();
        public bool[,] IsPath;
        public bool[,] Buildable;
        public int[,] Coverage;             // 칸마다 사거리 2.5 안 경로 칸 수 (봇의 자리 고르기, 힌트용)
        public int LengthMilli => (Path.Count - 1) * 1000;
        public int LengthCells => Path.Count - 1;
        public (int x, int y) Start => Path[0];
        public (int x, int y) End => Path[Path.Count - 1];

        public bool InBounds(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;
        public bool IsSlot(int x, int y) => InBounds(x, y) && Buildable[x, y] && !IsPath[x, y];
        public int SlotCount { get { int n = 0; for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (IsSlot(x, y)) n++; return n; } }

        /// <summary>경로 진행도(밀리)에 해당하는 위치(밀리). 범위 밖은 양 끝으로 고정.</summary>
        public (int X, int Y) PosAt(int distMilli)
        {
            if (distMilli <= 0) return Center(Path[0]);
            if (distMilli >= LengthMilli) return Center(Path[Path.Count - 1]);
            int i = distMilli / 1000, t = distMilli % 1000;
            var a = Center(Path[i]); var b = Center(Path[i + 1]);
            return (a.X + (b.X - a.X) * t / 1000, a.Y + (b.Y - a.Y) * t / 1000);
        }

        public static (int X, int Y) Center((int x, int y) c) => (c.x * 1000 + 500, c.y * 1000 + 500);

        // ─────────────────────────── 만들기 ───────────────────────────

        public static MapDef FromAscii(string name, string[] rows)
        {
            var m = new MapDef { Name = name, H = rows.Length, W = 0 };
            foreach (var r in rows) m.W = Math.Max(m.W, r.Length);
            m.IsPath = new bool[m.W, m.H]; m.Buildable = new bool[m.W, m.H];
            var pathCells = new bool[m.W, m.H];
            (int, int)? start = null;
            for (int row = 0; row < m.H; row++)
                for (int x = 0; x < m.W; x++)
                {
                    char ch = x < rows[row].Length ? rows[row][x] : ' ';
                    int y = m.H - 1 - row;
                    switch (ch)
                    {
                        case '.': m.Buildable[x, y] = true; break;
                        case '#': case 'B': pathCells[x, y] = true; break;
                        case 'S': pathCells[x, y] = true; start = (x, y); break;
                    }
                }
            if (start == null) throw new ArgumentException("맵에 S(출발)가 없다: " + name);
            // 출발에서 경로를 따라간다 (갈림길 없음)
            var visited = new bool[m.W, m.H];
            var cur = start.Value;
            m.Path.Add(cur); visited[cur.Item1, cur.Item2] = true;
            while (true)
            {
                (int, int)? next = null;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = cur.Item1 + dx, ny = cur.Item2 + dy;
                    if (m.InBounds(nx, ny) && pathCells[nx, ny] && !visited[nx, ny]) { next = (nx, ny); break; }
                }
                if (next == null) break;
                cur = next.Value; visited[cur.Item1, cur.Item2] = true; m.Path.Add(cur);
            }
            foreach (var (x, y) in m.Path) m.IsPath[x, y] = true;
            if (m.Path.Count < 2) throw new ArgumentException("경로가 너무 짧다: " + name);
            m.ComputeCoverage();
            return m;
        }

        void ComputeCoverage()
        {
            Coverage = new int[W, H];
            const long r2 = 2500L * 2500L;
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (!IsSlot(x, y)) continue;
                    int n = 0;
                    foreach (var p in Path)
                    {
                        long dx = (p.x - x) * 1000L, dy = (p.y - y) * 1000L;
                        if (dx * dx + dy * dy <= r2) n++;
                    }
                    Coverage[x, y] = n;
                }
        }

        /// <summary>뱀 모양: 가로 줄 runs 개를 3칸 간격으로, 끝에서 이어 붙인다. 출발은 왼쪽 위, 기지는 마지막 줄 끝.</summary>
        public static MapDef Snake(string name, int w, int h, int runs)
        {
            var grid = new char[h, w];
            for (int r = 0; r < h; r++) for (int x = 0; x < w; x++) grid[r, x] = '.';
            int row = 1;
            bool right = true;
            for (int run = 0; run < runs; run++)
            {
                for (int x = 1; x <= w - 2; x++) grid[row, x] = '#';
                if (run == 0) grid[row, 0] = 'S';
                if (run == runs - 1) { if (right) grid[row, w - 1] = 'B'; else grid[row, 0] = 'B'; }
                else
                {
                    int cx = right ? w - 2 : 1;
                    for (int r = row + 1; r < row + 3; r++) grid[r, cx] = '#';
                }
                row += 3; right = !right;
            }
            var rows = new string[h];
            for (int r = 0; r < h; r++) { var sb = new StringBuilder(); for (int x = 0; x < w; x++) sb.Append(grid[r, x]); rows[r] = sb.ToString(); }
            return FromAscii(name, rows);
        }

        /// <summary>테스트용 직선 맵: 가로 경로 하나, 위아래 한 줄씩 자리.</summary>
        public static MapDef Straight(int length, int slotRows = 1)
        {
            var rows = new List<string>();
            for (int i = 0; i < slotRows; i++) rows.Add(new string('.', length));
            rows.Add("S" + new string('#', length - 2) + "B");
            for (int i = 0; i < slotRows; i++) rows.Add(new string('.', length));
            return FromAscii($"직선{length}", rows.ToArray());
        }

        public string[] ToAscii()
        {
            var rows = new string[H];
            for (int row = 0; row < H; row++)
            {
                var sb = new StringBuilder();
                int y = H - 1 - row;
                for (int x = 0; x < W; x++)
                {
                    if ((x, y) == Start) sb.Append('S');
                    else if ((x, y) == End) sb.Append('B');
                    else if (IsPath[x, y]) sb.Append('#');
                    else if (Buildable[x, y]) sb.Append('.');
                    else sb.Append(' ');
                }
                rows[row] = sb.ToString();
            }
            return rows;
        }
    }

    public static class MapCatalog
    {
        /// <summary>1v1: 14×9, 가로 3줄 (경로 36칸, 자리 89).</summary>
        public static readonly MapDef OneVsOne = MapDef.Snake("굽이 셋", 14, 9, 3);
        /// <summary>2v2: 18×12, 가로 4줄.</summary>
        public static readonly MapDef TwoVsTwo = MapDef.Snake("굽이 넷", 18, 12, 4);
        /// <summary>3v3: 22×12, 가로 4줄 (더 길다).</summary>
        public static readonly MapDef ThreeVsThree = MapDef.Snake("긴 굽이 넷", 22, 12, 4);

        /// <summary>해금 맵: 짧고 빠른 두 굽이, 긴 다섯 굽이.</summary>
        public static readonly MapDef TwoBends = MapDef.Snake("굽이 둘", 14, 6, 2);
        public static readonly MapDef FiveBends = MapDef.Snake("굽이 다섯", 14, 15, 5);

        public static MapDef ForPlayers(int playersPerTeam) => playersPerTeam switch { 1 => OneVsOne, 2 => TwoVsTwo, _ => ThreeVsThree };
        public static readonly MapDef[] All = { OneVsOne, TwoVsTwo, ThreeVsThree, TwoBends, FiveBends };
        public static MapDef ByName(string name) { foreach (var m in All) if (m.Name == name) return m; return null; }
    }
}
