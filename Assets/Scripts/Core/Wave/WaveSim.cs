using System;
using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    /// <summary>라인 형태. 좌표는 밀리칸(1칸 = 1000). 공격 유닛은 X=0 에서 나타나 X=Length 의 기지로 걷는다.</summary>
    public sealed class LaneConfig
    {
        public int Width = 4;           // 열 수 (라인 너비)
        public int Length = 16;         // 칸
        public int GridStartX = 5;      // 앞줄 x. 줄은 GridStartX, +1, +2
        public int Rows = 3;
        public int TicksPerSecond = 20;
        public int MaxSeconds = 40;
        public int SpawnGapTicks = 8;   // 같은 웨이브 안에서 유닛 간 출발 간격
    }

    public enum UnitKind { Defender, Attacker }
    public enum UnitState { Idle, Walking, Attacking, Dead }

    public sealed class SimUnit
    {
        public int Id;
        public UnitKind Kind;
        public string Name;
        public DefenderDef Def;         // 방어 유닛일 때
        public AttackerDef Atk;         // 공격 유닛일 때
        public int X, Y;                // 밀리칸
        public int Hp, MaxHp;
        public int Damage;              // 한 방 피해
        public int CooldownTicks, CooldownLeft;
        public int RangeMilli;
        public int SpeedPerTick;        // 밀리칸/틱
        public bool Flying, AntiAir;
        public int SplashMilli;
        public int HealPerTick10;       // 초당 치유 (틱마다 /TicksPerSecond 적용용, 누적기 사용)
        public int HealAcc;
        public int BurnLeftTicks, BurnPerTick10;
        public int BurnAcc;
        public UnitState State = UnitState.Idle;
        public int TargetId = -1;
        public int Col, Row;            // 방어 유닛 슬롯
        public int Owner;               // 플레이어 인덱스 (방어 유닛)
        public bool Boss;
        public bool Alive => State != UnitState.Dead;
    }

    public enum SimEventType { Spawn, Attack, Damage, Death, Leak, Heal, Timeout }

    public struct SimEvent
    {
        public SimEventType Type;
        public int A, B, Value;         // 주체, 대상, 수치
        public int Tick;
    }

    /// <summary>
    /// 한 웨이브의 결정론 시뮬레이션. 정수만 쓴다. 같은 (설정, 시드, 입력)이면 어떤 기기에서든 같은 결과.
    /// 사용: 방어 유닛 배치 → 공격 유닛 큐 → Tick() 을 IsOver 까지 반복. 이벤트는 틱마다 Events 에 쌓인다.
    /// </summary>
    public sealed class WaveSim
    {
        public LaneConfig Cfg { get; }
        public int Tick { get; private set; }
        public bool IsOver { get; private set; }
        public int Leaked { get; private set; }       // 기지 누수 피해 합
        public int Kills { get; private set; }
        public int DefendersLost { get; private set; }
        public readonly List<SimUnit> Units = new List<SimUnit>();
        public readonly List<SimEvent> Events = new List<SimEvent>();
        readonly List<(int tick, SimUnit unit)> _spawnQueue = new List<(int, SimUnit)>();
        readonly Rng _rng;
        int _nextId = 1;
        int _queuedSpawnTick;

        public WaveSim(LaneConfig cfg, ulong seed)
        {
            Cfg = cfg;
            _rng = new Rng(seed);
        }

        int Milli(int cells) => cells * 1000;

        // ─────────────────────────── 구성 ───────────────────────────

        public SimUnit AddDefender(DefenderDef def, int col, int row, int owner = 0, bool upgraded = false)
        {
            if (col < 0 || col >= Cfg.Width || row < 0 || row >= Cfg.Rows) throw new ArgumentOutOfRangeException(nameof(col));
            foreach (var u in Units) if (u.Kind == UnitKind.Defender && u.Alive && u.Col == col && u.Row == row) throw new InvalidOperationException("slot occupied");
            int mult = upgraded ? 150 : 100;
            var u2 = new SimUnit
            {
                Id = _nextId++, Kind = UnitKind.Defender, Name = def.Name, Def = def, Col = col, Row = row, Owner = owner,
                X = Milli(Cfg.GridStartX + row) + 500, Y = Milli(col) + 500,
                MaxHp = def.Hp * mult / 100, Damage = def.Atk * mult / 100,
                CooldownTicks = def.AttacksPer10s > 0 ? Math.Max(1, Cfg.TicksPerSecond * 10 / def.AttacksPer10s) : 0,
                RangeMilli = Milli(def.Range), AntiAir = def.AntiAir, SplashMilli = Milli(def.SplashRadius),
                HealPerTick10 = def.HealPerSec * mult / 100,
                State = UnitState.Idle,
            };
            u2.Hp = u2.MaxHp;
            Units.Add(u2);
            return u2;
        }

        /// <summary>공격 유닛을 출발 대기열에 넣는다. 열은 무작위(결정론).</summary>
        public SimUnit QueueAttacker(AttackerDef def, bool boss = false, int hpPercent = 100)
        {
            int col = _rng.Next(Cfg.Width);
            int hp = def.Hp * hpPercent / 100 * (boss ? 2 : 1);
            var u = new SimUnit
            {
                Id = _nextId++, Kind = UnitKind.Attacker, Name = def.Name, Atk = def,
                X = -1000, Y = Milli(col) + 500, MaxHp = hp, Hp = hp, Damage = def.Atk,
                CooldownTicks = def.AttacksPer10s > 0 ? Math.Max(1, Cfg.TicksPerSecond * 10 / def.AttacksPer10s) : 0,
                RangeMilli = Milli(def.Range), SpeedPerTick = Math.Max(1, def.SpeedMilliPerSec / Cfg.TicksPerSecond),
                Flying = def.Flying, SplashMilli = Milli(def.SplashRadius), HealPerTick10 = def.HealPerSec,
                Boss = boss, Col = col, State = UnitState.Idle,
            };
            _spawnQueue.Add((_queuedSpawnTick, u));
            _queuedSpawnTick += Cfg.SpawnGapTicks;
            return u;
        }

        public void QueueBaseWave(int round, int scalePercent = 100)
        {
            foreach (var (id, count, boss) in WaveCatalog.BaseWave(round))
            {
                int n = Math.Max(1, count * scalePercent / 100);
                for (int i = 0; i < n; i++) QueueAttacker(WaveCatalog.Attacker(id), boss);
            }
        }

        // ─────────────────────────── 진행 ───────────────────────────

        public void RunToEnd() { while (!IsOver) Tick_(); }
        public void Tick_() => Step();

        public void Step()
        {
            if (IsOver) return;
            Events.Clear();
            Tick++;

            // 출발
            for (int i = _spawnQueue.Count - 1; i >= 0; i--)
                if (_spawnQueue[i].tick < Tick)
                {
                    var u = _spawnQueue[i].unit;
                    u.X = 0; u.State = UnitState.Walking;
                    Units.Add(u);
                    Emit(SimEventType.Spawn, u.Id, -1, 0);
                    _spawnQueue.RemoveAt(i);
                }

            // 화상, 치유
            foreach (var u in Units)
            {
                if (!u.Alive) continue;
                if (u.BurnLeftTicks > 0)
                {
                    u.BurnLeftTicks--;
                    u.BurnAcc += u.BurnPerTick10;
                    int dmg = u.BurnAcc / Cfg.TicksPerSecond;
                    if (dmg > 0) { u.BurnAcc -= dmg * Cfg.TicksPerSecond; ApplyDamage(u, dmg, -1); }
                }
                if (u.HealPerTick10 > 0 && u.Alive)
                {
                    u.HealAcc += u.HealPerTick10;
                    int heal = u.HealAcc / Cfg.TicksPerSecond;
                    if (heal > 0)
                    {
                        u.HealAcc -= heal * Cfg.TicksPerSecond;
                        var t = MostDamagedAlly(u);
                        if (t != null) { t.Hp = Math.Min(t.MaxHp, t.Hp + heal); Emit(SimEventType.Heal, u.Id, t.Id, heal); }
                    }
                }
            }

            // 방어 유닛 공격
            foreach (var d in Units)
            {
                if (d.Kind != UnitKind.Defender || !d.Alive || d.CooldownTicks == 0) continue;
                if (d.CooldownLeft > 0) { d.CooldownLeft--; continue; }
                var t = NearestEnemy(d, d.RangeMilli);
                if (t == null) { d.State = UnitState.Idle; d.TargetId = -1; continue; }
                d.State = UnitState.Attacking; d.TargetId = t.Id;
                d.CooldownLeft = d.CooldownTicks;
                int dmg = d.Damage;
                if (t.Flying && d.Def.AirDamageMultiplier > 1) dmg *= d.Def.AirDamageMultiplier;
                Emit(SimEventType.Attack, d.Id, t.Id, dmg);
                if (d.SplashMilli > 0) SplashDamage(d, t, dmg); else ApplyDamage(t, dmg, d.Id);
                if (d.Def.BurnPerSec > 0 && t.Alive) { t.BurnLeftTicks = d.Def.BurnSeconds * Cfg.TicksPerSecond; t.BurnPerTick10 = d.Def.BurnPerSec; }
            }

            // 공격 유닛 이동/공격
            foreach (var a in Units)
            {
                if (a.Kind != UnitKind.Attacker || !a.Alive) continue;
                if (a.CooldownLeft > 0) a.CooldownLeft--;

                SimUnit target = a.CooldownTicks > 0 ? NearestEnemy(a, a.RangeMilli) : null;
                bool blocked = false;
                if (!a.Flying)
                {
                    var blocker = Blocker(a);
                    if (blocker != null)
                    {
                        blocked = true;
                        if (target == null && a.CooldownTicks > 0) target = blocker;
                    }
                }

                if (target != null && a.CooldownLeft == 0 && a.CooldownTicks > 0)
                {
                    a.CooldownLeft = a.CooldownTicks;
                    a.TargetId = target.Id;
                    Emit(SimEventType.Attack, a.Id, target.Id, a.Damage);
                    if (a.SplashMilli > 0) SplashDamage(a, target, a.Damage); else ApplyDamage(target, a.Damage, a.Id);
                }

                bool stop = blocked || (target != null && !a.Flying);
                if (stop) { a.State = UnitState.Attacking; continue; }

                a.State = UnitState.Walking;
                a.X += a.SpeedPerTick;
                if (a.X >= Milli(Cfg.Length))
                {
                    int leak = a.Atk.Leak + a.Atk.ExtraBaseDamage;
                    Leaked += leak;
                    a.State = UnitState.Dead;
                    Emit(SimEventType.Leak, a.Id, -1, leak);
                }
            }

            // 종료 판정
            bool anyAttacker = _spawnQueue.Count > 0;
            foreach (var u in Units) if (u.Kind == UnitKind.Attacker && u.Alive) { anyAttacker = true; break; }
            if (!anyAttacker) { IsOver = true; return; }
            if (Tick >= Cfg.MaxSeconds * Cfg.TicksPerSecond)
            {
                foreach (var u in Units)
                    if (u.Kind == UnitKind.Attacker && u.Alive)
                    {
                        int leak = (u.Atk.Leak + 1) / 2;
                        Leaked += leak; u.State = UnitState.Dead;
                        Emit(SimEventType.Timeout, u.Id, -1, leak);
                    }
                foreach (var (_, u) in _spawnQueue) { int leak = (u.Atk.Leak + 1) / 2; Leaked += leak; }
                _spawnQueue.Clear();
                IsOver = true;
            }
        }

        // ─────────────────────────── 보조 ───────────────────────────

        static long Dist2(SimUnit a, SimUnit b)
        {
            long dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        bool CanTarget(SimUnit from, SimUnit to)
        {
            if (!to.Alive || to.Kind == from.Kind) return false;
            if (to.Kind == UnitKind.Attacker && to.Flying && !from.AntiAir) return false;
            if (to.Kind == UnitKind.Attacker && to.X < 0) return false;
            return true;
        }

        SimUnit NearestEnemy(SimUnit from, int rangeMilli)
        {
            SimUnit best = null; long bestD = long.MaxValue;
            long r2 = (long)rangeMilli * rangeMilli;
            foreach (var u in Units)
            {
                if (!CanTarget(from, u)) continue;
                long d = Dist2(from, u);
                if (d <= r2 && (d < bestD || (d == bestD && u.Id < best.Id))) { best = u; bestD = d; }
            }
            return best;
        }

        /// <summary>같은 열, 바로 앞(1칸 이내)의 살아 있는 지상 방어 유닛.</summary>
        SimUnit Blocker(SimUnit a)
        {
            SimUnit best = null;
            foreach (var d in Units)
            {
                if (d.Kind != UnitKind.Defender || !d.Alive) continue;
                if (Math.Abs(d.Y - a.Y) >= 500) continue;
                int ahead = d.X - a.X;
                if (ahead > 0 && ahead <= 1000 && (best == null || d.X < best.X)) best = d;
            }
            return best;
        }

        SimUnit MostDamagedAlly(SimUnit healer)
        {
            SimUnit best = null; int bestMissing = 0;
            long r2 = (long)healer.RangeMilli * healer.RangeMilli;
            foreach (var u in Units)
            {
                if (u.Kind != healer.Kind || !u.Alive || u.Id == healer.Id) continue;
                if (healer.Kind == UnitKind.Defender && healer.Def.HealMachineOnly && u.Def.Tribe != DefTribe.Machine) continue;
                if (Dist2(healer, u) > r2) continue;
                int missing = u.MaxHp - u.Hp;
                if (missing > bestMissing) { best = u; bestMissing = missing; }
            }
            return best;
        }

        void SplashDamage(SimUnit from, SimUnit center, int dmg)
        {
            long r2 = (long)from.SplashMilli * from.SplashMilli;
            var hits = new List<SimUnit>();
            foreach (var u in Units)
                if (CanTarget(from, u) && Dist2(center, u) <= r2) hits.Add(u);
            foreach (var u in hits) ApplyDamage(u, dmg, from.Id);
        }

        void ApplyDamage(SimUnit target, int dmg, int sourceId)
        {
            if (!target.Alive || dmg <= 0) return;
            target.Hp -= dmg;
            Emit(SimEventType.Damage, sourceId, target.Id, dmg);
            if (target.Hp > 0) return;
            target.Hp = 0;
            target.State = UnitState.Dead;
            if (target.Kind == UnitKind.Attacker) Kills++; else DefendersLost++;
            Emit(SimEventType.Death, sourceId, target.Id, 0);
        }

        void Emit(SimEventType type, int a, int b, int value) => Events.Add(new SimEvent { Type = type, A = a, B = b, Value = value, Tick = Tick });

        /// <summary>결정론 검증용 요약.</summary>
        public string Hash()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Tick).Append('|').Append(Leaked).Append('|').Append(Kills).Append('|').Append(DefendersLost).Append('|');
            foreach (var u in Units) sb.Append(u.Id).Append(':').Append(u.X).Append(',').Append(u.Y).Append(',').Append(u.Hp).Append(' ');
            return sb.ToString();
        }
    }
}
