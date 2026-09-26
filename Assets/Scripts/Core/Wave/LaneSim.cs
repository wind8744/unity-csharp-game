using System;
using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    /// <summary>라인(맵) 하나의 설정. 좌표는 밀리칸(1칸 = 1000).</summary>
    public sealed class LaneConfig
    {
        public MapDef Map = MapCatalog.OneVsOne;
        public int TicksPerSecond = 20;
        public int MatchSeconds = 600;
        public int FirstWaveSeconds = 30;
        public int WaveIntervalSeconds = 30;
        public int WaveScalePercent = 100;
        public int LateWaveIndex = 9;       // 이 번호(0부터)부터 기본 웨이브 체력이 웨이브마다 LateWaveStepPercent 씩 오른다 (후반 압박)
        public int LateWaveStepPercent = 8;
        public int BuildSeconds = 2;
        public int BaseHp = 30;
        public int SpawnGapTicks = 6;       // 같은 무리 안 출발 간격
        public bool AutoWaves = true;
        public int CreepSpeedPercent = 50;  // 밸런스 전역 배율 (v0.5: 50%)
        public int TowerDamagePercent = 100;
        public int PathLengthMilli => Map.LengthMilli;
    }

    public sealed class Tower
    {
        public int Id; public TowerDef Def; public int Cx, Cy, Owner;   // 칸 좌표
        public int X, Y;                    // 칸 중심 (밀리)
        public bool Upgraded;
        public int Star = 1;                // 같은 타워 3개 → ★2, ★2 3개 → ★3
        public int UpgradePercent = 50;     // 정예 증강이면 80
        public bool ForceAntiAir;           // 대공망 증강
        public int SlowedLeft, SlowedPercent; // 저주: 공속 감소
        public int BuildLeft, CooldownLeft, SilenceLeft;
        public bool JobAdj;                 // 직업 시너지: 8방향 이웃에 같은 직업 타워
        public bool Alive = true;
        public bool Ready => Alive && BuildLeft == 0 && SilenceLeft == 0;
        public int TargetId = -1;
    }

    public sealed class Creep
    {
        public int Id; public AttackerDef Def;
        public int Dist = -1;               // 경로 진행도 (밀리). -1 = 아직 출발 전
        public int X, Y;                    // 현재 위치 (밀리)
        public int Hp, MaxHp;
        public int SpeedPerTick;
        public int SlowLeft, SlowPercent, StealthLeft, BurnLeft, BurnPerSec, BurnAcc, HealAcc, PeriodicLeft;
        public bool Boss, FromEnemy;
        public int GroupId;
        public bool NoKillGold;             // 부식
        public bool SpawnCurse;             // 저주
        public int ExtraLeak;               // 무리 시너지(거인)
        public bool GroupNameBonus, GroupTribeBonus;
        public (int range10, int tenths)? SpawnSilenceOverride; // 침묵탄
        public bool Alive = true;
        public bool Spawned => Dist >= 0;
        public bool Stealthed => StealthLeft > 0;
    }

    public enum SimEventType { WaveStart, Spawn, Built, Sold, Upgraded, Attack, Damage, Death, Leak, Heal, Silence, Slow, MatchEnd, Merged, Fused }

    public struct SimEvent
    {
        public SimEventType Type;
        public int A, B, Value;
        public int Tick;
    }

    /// <summary>
    /// 맵 하나의 결정론 실시간 시뮬레이션. 정수만 쓴다. 타워는 공격만 하고 맞지 않으며, 유닛은 경로를 따라 걷기만 한다.
    /// 명령(짓기·판매·강화·합치기·합성·보내기)은 어느 틱에든 들어올 수 있고, 같은 (설정, 시드, 틱별 명령)이면 결과가 같다.
    /// </summary>
    public sealed class LaneSim
    {
        public LaneConfig Cfg { get; }
        public MapDef Map => Cfg.Map;
        public int Tick { get; private set; }
        public int BaseHp { get; private set; }
        public int Leaked { get; private set; }
        public int Kills { get; private set; }
        public int GoldEarned { get; private set; }
        public bool IsOver { get; private set; }
        public int NextWaveIndex { get; private set; }
        public int GoldKills { get; private set; }     // 처치 골드 대상 (부식 제외)
        public LaneModifiers Mod;                      // 이벤트 시간대
        // 시너지 (매 틱 재계산)
        public int[] TribeCount { get; } = new int[3];
        public int[] JobPairs { get; } = new int[3];   // 직업 시너지가 켜진 타워 수 (전사, 궁수, 마법사)
        public bool Forest3, Forest5, Fire3, Fire5, Machine3, Machine5;
        public int LastBossKillDist { get; private set; } = -1;
        public readonly List<Tower> Towers = new List<Tower>();
        public readonly List<Creep> Creeps = new List<Creep>();
        public readonly List<SimEvent> Events = new List<SimEvent>();
        readonly List<(int tick, Creep creep)> _queue = new List<(int, Creep)>();
        readonly Rng _rng;
        int _nextId = 1;
        int _queueTail;

        public LaneSim(LaneConfig cfg, ulong seed)
        {
            Cfg = cfg;
            _rng = new Rng(seed);
            BaseHp = cfg.BaseHp;
        }

        public int Seconds => Tick / Cfg.TicksPerSecond;
        public void AddBaseHp(int delta) { BaseHp = Math.Max(0, BaseHp + delta); }
        public void SilenceAll(int ticks) { foreach (var t in Towers) if (t.Alive) t.SilenceLeft = Math.Max(t.SilenceLeft, ticks); }
        public int TicksToNextWave => Math.Max(0, NextWaveTick() - Tick);
        int NextWaveTick() => (Cfg.FirstWaveSeconds + NextWaveIndex * Cfg.WaveIntervalSeconds) * Cfg.TicksPerSecond;

        public Creep FindCreep(int id)
        {
            foreach (var c in Creeps) if (c.Id == id) return c;
            foreach (var (_, c) in _queue) if (c.Id == id) return c;
            return null;
        }

        public int CreepsAlive()
        {
            int n = 0;
            foreach (var c in Creeps) if (c.Alive) n++;
            return n + _queue.Count;
        }

        // ─────────────────────────── 명령 ───────────────────────────

        public Tower TowerAt(int id)
        {
            foreach (var t in Towers) if (t.Alive && t.Id == id) return t;
            return null;
        }

        public Tower TowerAtCell(int x, int y)
        {
            foreach (var t in Towers) if (t.Alive && t.Cx == x && t.Cy == y) return t;
            return null;
        }

        public bool CanBuildAt(int x, int y) => Map.IsSlot(x, y) && TowerAtCell(x, y) == null;

        public Tower Build(TowerDef def, int x, int y, int owner = 0)
        {
            if (!CanBuildAt(x, y)) return null;
            var (px, py) = MapDef.Center((x, y));
            var t = new Tower
            {
                Id = _nextId++, Def = def, Cx = x, Cy = y, Owner = owner, X = px, Y = py,
                BuildLeft = Cfg.BuildSeconds * Cfg.TicksPerSecond,
            };
            Towers.Add(t);
            Emit(SimEventType.Built, t.Id, -1, def.Id);
            return t;
        }

        public bool Sell(int towerId)
        {
            var t = FindTower(towerId);
            if (t == null) return false;
            t.Alive = false;
            Emit(SimEventType.Sold, t.Id, -1, 0);
            return true;
        }

        public bool Upgrade(int towerId)
        {
            var t = FindTower(towerId);
            if (t == null || t.Upgraded) return false;
            t.Upgraded = true;
            Emit(SimEventType.Upgraded, t.Id, -1, 0);
            return true;
        }

        Tower FindTower(int id)
        {
            foreach (var t in Towers) if (t.Alive && t.Id == id) return t;
            return null;
        }

        /// <summary>같은 정의·같은 별 타워 3개를 하나로 합쳐 별을 올린다. 결과는 target 자리에, 즉시 완성. 재료 중 하나라도 강화면 강화 유지.</summary>
        public Tower MergeStar(int targetId, int otherId1, int otherId2)
        {
            var a = FindTower(targetId); var b = FindTower(otherId1); var c = FindTower(otherId2);
            if (a == null || b == null || c == null || a.Id == b.Id || a.Id == c.Id || b.Id == c.Id) return null;
            if (a.Def.Id != b.Def.Id || a.Def.Id != c.Def.Id || a.Star != b.Star || a.Star != c.Star || a.Star >= 3) return null;
            var r = new Tower
            {
                Id = _nextId++, Def = a.Def, Cx = a.Cx, Cy = a.Cy, Owner = a.Owner, X = a.X, Y = a.Y,
                Star = a.Star + 1, Upgraded = a.Upgraded || b.Upgraded || c.Upgraded,
                UpgradePercent = Math.Max(a.UpgradePercent, Math.Max(b.UpgradePercent, c.UpgradePercent)),
                ForceAntiAir = a.ForceAntiAir || b.ForceAntiAir || c.ForceAntiAir,
            };
            a.Alive = b.Alive = c.Alive = false;
            Emit(SimEventType.Sold, b.Id, -1, 0); Emit(SimEventType.Sold, c.Id, -1, 0); Emit(SimEventType.Sold, a.Id, -1, 0);
            Towers.Add(r);
            Emit(SimEventType.Merged, r.Id, a.Id, r.Star);
            return r;
        }

        /// <summary>레시피에 맞는 타워 두 개를 합성 타워로 바꾼다. 결과는 a 자리에, 즉시 완성, ★1. 강화는 둘 중 하나라도 있으면 유지.</summary>
        public Tower Fuse(int aId, int bId)
        {
            var a = FindTower(aId); var b = FindTower(bId);
            if (a == null || b == null || a.Id == b.Id) return null;
            var def = WaveCatalog.FindRecipe(a.Def.Id, b.Def.Id);
            if (def == null) return null;
            var r = new Tower
            {
                Id = _nextId++, Def = def, Cx = a.Cx, Cy = a.Cy, Owner = a.Owner, X = a.X, Y = a.Y,
                Star = 1, Upgraded = a.Upgraded || b.Upgraded, UpgradePercent = Math.Max(a.UpgradePercent, b.UpgradePercent),
                ForceAntiAir = a.ForceAntiAir || b.ForceAntiAir,
            };
            a.Alive = b.Alive = false;
            Emit(SimEventType.Sold, b.Id, -1, 0); Emit(SimEventType.Sold, a.Id, -1, 0);
            Towers.Add(r);
            Emit(SimEventType.Fused, r.Id, a.Id, def.Id);
            return r;
        }

        /// <summary>유닛을 이 맵에 보낸다 (상대가 보낸 것, 또는 기본 웨이브). 무리는 출발 간격을 두고 줄지어 나온다.</summary>
        public Creep Send(AttackerDef def, bool fromEnemy = true, int groupId = 0, int hpMult = 1, int hpPercent = 100)
        {
            int hp = def.Hp * hpMult * hpPercent / 100;
            var start = MapDef.Center(Map.Start);
            var c = new Creep
            {
                Id = _nextId++, Def = def, Dist = -1, X = start.X, Y = start.Y,
                MaxHp = hp, Hp = hp, SpeedPerTick = Math.Max(1, def.SpeedMilliPerSec * Cfg.CreepSpeedPercent / 100 / Cfg.TicksPerSecond),
                Boss = hpMult > 1 || def.Rarity == Rarity.Hero, FromEnemy = fromEnemy, GroupId = groupId,
                StealthLeft = def.StealthSeconds * Cfg.TicksPerSecond,
                PeriodicLeft = def.PeriodicSilenceEverySec * Cfg.TicksPerSecond,
            };
            int at = Math.Max(Tick, _queueTail);
            _queue.Add((at, c));
            _queueTail = at + Cfg.SpawnGapTicks;
            return c;
        }

        public void SpawnBaseWave(int index)
        {
            if (index < 0 || index >= WaveCatalog.BaseWaves.Length) return;
            Emit(SimEventType.WaveStart, index, -1, 0);
            int hpPercent = 100 + Math.Max(0, index - Cfg.LateWaveIndex) * Cfg.LateWaveStepPercent;
            foreach (var (id, count, mult) in WaveCatalog.BaseWaves[index])
            {
                int n = Math.Max(1, count * Cfg.WaveScalePercent / 100);
                for (int i = 0; i < n; i++) Send(WaveCatalog.Attacker(id), false, -1, mult, hpPercent);
            }
        }

        // ─────────────────────────── 진행 ───────────────────────────

        public void Step()
        {
            if (IsOver) return;
            Events.Clear();
            Tick++;

            if (Cfg.AutoWaves && NextWaveIndex < WaveCatalog.BaseWaves.Length && Tick >= NextWaveTick())
            {
                SpawnBaseWave(NextWaveIndex);
                NextWaveIndex++;
            }
            RecomputeSynergy();
            if (Forest5 && Tick % (30 * Cfg.TicksPerSecond) == 0) AddBaseHp(1);

            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].tick <= Tick)
                {
                    var c = _queue[i].creep;
                    c.Dist = 0;
                    (c.X, c.Y) = Map.PosAt(0);
                    Creeps.Add(c);
                    Emit(SimEventType.Spawn, c.Id, -1, 0);
                    if (c.Def.SpawnSilenceTenths > 0) SilenceTowersNear(c, c.Def.SpawnSilenceRange10 * 100, c.Def.SpawnSilenceTenths * Cfg.TicksPerSecond / 10);
                    if (c.SpawnSilenceOverride.HasValue) SilenceTowersNear(c, c.SpawnSilenceOverride.Value.range10 * 100, c.SpawnSilenceOverride.Value.tenths * Cfg.TicksPerSecond / 10);
                    if (c.SpawnCurse) foreach (var t in Towers) if (t.Alive && Dist2(t.X, t.Y, c.X, c.Y) <= Sq(2000)) { t.SlowedLeft = 3 * Cfg.TicksPerSecond; t.SlowedPercent = 20; }
                    _queue.RemoveAt(i); i--;
                }

            // 유닛 상태 (화상, 치유, 은신, 감속, 주기 침묵)
            foreach (var c in Creeps)
            {
                if (!c.Alive) continue;
                if (c.StealthLeft > 0) c.StealthLeft--;
                if (c.SlowLeft > 0) c.SlowLeft--;
                if (c.BurnLeft > 0)
                {
                    c.BurnLeft--;
                    c.BurnAcc += c.BurnPerSec;
                    int dmg = c.BurnAcc / Cfg.TicksPerSecond;
                    if (dmg > 0) { c.BurnAcc -= dmg * Cfg.TicksPerSecond; ApplyDamage(c, dmg, -1); }
                }
                if (c.Def.HealPerSec > 0 && c.Alive)
                {
                    c.HealAcc += c.Def.HealPerSec;
                    int heal = c.HealAcc / Cfg.TicksPerSecond;
                    if (heal > 0)
                    {
                        c.HealAcc -= heal * Cfg.TicksPerSecond;
                        var t = MostDamagedAlly(c, c.Def.HealRange10 * 100);
                        if (t != null) { t.Hp = Math.Min(t.MaxHp, t.Hp + heal); Emit(SimEventType.Heal, c.Id, t.Id, heal); }
                    }
                }
                if (c.Def.PeriodicSilenceEverySec > 0 && c.Alive)
                {
                    c.PeriodicLeft--;
                    if (c.PeriodicLeft <= 0)
                    {
                        c.PeriodicLeft = c.Def.PeriodicSilenceEverySec * Cfg.TicksPerSecond;
                        var t = NearestTower(c);
                        if (t != null) Silence(t, c.Def.PeriodicSilenceTenths * Cfg.TicksPerSecond / 10, c.Id);
                    }
                }
            }

            // 타워 사격
            foreach (var t in Towers)
            {
                if (!t.Alive) continue;
                if (t.SlowedLeft > 0) t.SlowedLeft--;
                if (t.BuildLeft > 0) { t.BuildLeft--; continue; }
                if (t.SilenceLeft > 0) { t.SilenceLeft--; continue; }
                if (t.Def.IsSupport) continue;
                if (t.CooldownLeft > 0) { t.CooldownLeft--; continue; }
                int range = EffectiveRange(t);
                var target = ClosestToBase(t, range);
                if (target == null) { t.TargetId = -1; continue; }
                t.TargetId = target.Id;
                t.CooldownLeft = EffectiveCooldown(t);
                int dmg = EffectiveDamage(t, target);
                Emit(SimEventType.Attack, t.Id, target.Id, dmg);
                if (SplashRadius10(t) > 0) Splash(t, target, dmg); else Hit(t, target, dmg);
                if (t.Def.ChainCount > 0) Chain(t, target, dmg);
            }

            FlushFireBursts();

            // 유닛 이동 (경로 진행도) → 누수
            int length = Map.LengthMilli;
            foreach (var c in Creeps)
            {
                if (!c.Alive) continue;
                int speed = c.SpeedPerTick * (100 + Mod.CreepSpeedBonusPercent) / 100;
                if (c.SlowLeft > 0 && !c.Def.SlowImmune) speed = speed * (100 - c.SlowPercent) / 100;
                c.Dist += Math.Max(1, speed);
                if (c.Dist >= length)
                {
                    c.Alive = false;
                    int leak = c.Def.Leak + c.ExtraLeak;
                    Leaked += leak;
                    BaseHp -= leak;
                    Emit(SimEventType.Leak, c.Id, -1, leak);
                }
                else (c.X, c.Y) = Map.PosAt(c.Dist);
            }
            Creeps.RemoveAll(c => !c.Alive);

            if (BaseHp <= 0) { BaseHp = 0; End(); return; }
            if (Tick >= Cfg.MatchSeconds * Cfg.TicksPerSecond) End();
        }

        void End() { IsOver = true; Emit(SimEventType.MatchEnd, -1, -1, BaseHp); }

        /// <summary>테스트용: 살아 있는 유닛이 없을 때까지 진행 (자동 웨이브가 꺼져 있을 때 쓴다).</summary>
        public void RunUntilQuiet(int maxTicks = 20000)
        {
            int guard = 0;
            do { Step(); } while (!IsOver && CreepsAlive() > 0 && guard++ < maxTicks);
        }

        // ─────────────────────────── 계산 ───────────────────────────

        public int EffectiveRange(Tower t)
        {
            int r10 = t.Def.Range10 + WaveCatalog.StarRangeBonus10(t.Star);
            if (Forest3) r10 += 5;
            if (t.Def.Job == DefJob.Archer && t.JobAdj) r10 += 10;
            r10 += Mod.TowerRangeDelta10;
            return Math.Max(10, r10) * 100;
        }

        bool IsAntiAir(Tower t) => t.Def.AntiAir || (t.ForceAntiAir && t.Def.Job == DefJob.Archer);

        /// <summary>오라 수치도 별을 따라 오른다 (★2 ×1.5, ★3 ×2).</summary>
        static int AuraPercent(Tower s, int percent) => s.Star <= 1 ? percent : s.Star == 2 ? percent * 150 / 100 : percent * 2;
        int MageBoost(Tower t, int percent) => t.Def.Job == DefJob.Mage && t.JobAdj ? percent * 150 / 100 : percent;
        int SplashRadius10(Tower t) => t.Def.SplashRadius10 == 0 ? 0 : (t.Def.Job == DefJob.Mage && t.JobAdj ? t.Def.SplashRadius10 * 150 / 100 : t.Def.SplashRadius10);

        int EffectiveCooldown(Tower t)
        {
            int per10s = t.Def.AttacksPer10s;
            if (t.Upgraded) per10s = per10s * (100 + t.UpgradePercent) / 100;
            if (t.Def.Tribe == DefTribe.Machine)
            {
                if (Machine5) per10s = per10s * 120 / 100;
                foreach (var s in Towers)
                    if (s.Alive && s.Ready && s.Def.AuraSpeedPercentMachine > 0 && s.Id != t.Id && Dist2(s.X, s.Y, t.X, t.Y) <= Sq(s.Def.Range10 * 100))
                    { per10s = per10s * (100 + MageBoost(s, AuraPercent(s, s.Def.AuraSpeedPercentMachine))) / 100; break; }
            }
            if (t.SlowedLeft > 0) per10s = per10s * (100 - t.SlowedPercent) / 100;
            return Math.Max(1, Cfg.TicksPerSecond * 10 / Math.Max(1, per10s));
        }

        int EffectiveDamage(Tower t, Creep target)
        {
            int dmg = t.Def.Atk * Cfg.TowerDamagePercent / 100;
            dmg = dmg * WaveCatalog.StarPercent(t.Star) / 100;
            if (t.Upgraded) dmg = dmg * (100 + t.UpgradePercent) / 100;
            if (Fire3) dmg = dmg * 110 / 100;
            if (t.Def.Job == DefJob.Warrior && t.JobAdj) dmg = dmg * 120 / 100;
            foreach (var s in Towers)
                if (s.Alive && s.Ready && s.Def.AuraAtkPercent > 0 && s.Id != t.Id && Dist2(s.X, s.Y, t.X, t.Y) <= Sq(s.Def.Range10 * 100))
                { dmg = dmg * (100 + MageBoost(s, AuraPercent(s, s.Def.AuraAtkPercent))) / 100; break; }
            if (target.Def.Flying) dmg *= t.Def.AirMultiplier;
            if (target.Def.Tribe == AtkTribe.Giant) dmg *= t.Def.GiantMultiplier;
            return dmg;
        }

        bool CanTarget(Tower t, Creep c) => c.Alive && c.Spawned && !c.Stealthed && (!c.Def.Flying || IsAntiAir(t));

        /// <summary>사거리 안에서 기지에 가장 가까운(진행도가 가장 큰) 유닛.</summary>
        Creep ClosestToBase(Tower t, int rangeMilli)
        {
            Creep best = null;
            long r2 = Sq(rangeMilli);
            foreach (var c in Creeps)
            {
                if (!CanTarget(t, c) || Dist2(t.X, t.Y, c.X, c.Y) > r2) continue;
                if (best == null || c.Dist > best.Dist || (c.Dist == best.Dist && c.Id < best.Id)) best = c;
            }
            return best;
        }

        Tower NearestTower(Creep c)
        {
            Tower best = null; long bestD = long.MaxValue;
            foreach (var t in Towers)
            {
                if (!t.Alive) continue;
                long d = Dist2(t.X, t.Y, c.X, c.Y);
                if (d < bestD) { best = t; bestD = d; }
            }
            return best;
        }

        Creep MostDamagedAlly(Creep healer, int rangeMilli)
        {
            Creep best = null; int bestMissing = 0;
            long r2 = Sq(rangeMilli);
            foreach (var c in Creeps)
            {
                if (!c.Alive || c.Id == healer.Id || Dist2(healer.X, healer.Y, c.X, c.Y) > r2) continue;
                int missing = c.MaxHp - c.Hp;
                if (missing > bestMissing) { best = c; bestMissing = missing; }
            }
            return best;
        }

        void Hit(Tower t, Creep c, int dmg)
        {
            ApplyDamage(c, dmg, t.Id);
            if (!c.Alive) return;
            if (t.Def.BurnPerSec > 0) { c.BurnLeft = t.Def.BurnSeconds * Cfg.TicksPerSecond; c.BurnPerSec = t.Def.BurnPerSec; }
            if (t.Def.SlowPercent > 0 && !c.Def.SlowImmune) { c.SlowLeft = t.Def.SlowSeconds * Cfg.TicksPerSecond; c.SlowPercent = t.Def.SlowPercent; Emit(SimEventType.Slow, t.Id, c.Id, t.Def.SlowPercent); }
        }

        void Splash(Tower t, Creep center, int dmg)
        {
            long r2 = Sq(SplashRadius10(t) * 100);
            var hits = new List<Creep>();
            foreach (var c in Creeps) if (CanTarget(t, c) && Dist2(center.X, center.Y, c.X, c.Y) <= r2) hits.Add(c);
            foreach (var c in hits) Hit(t, c, dmg);
        }

        /// <summary>연쇄: 맞은 유닛에서 가까운 다른 유닛으로 최대 N번 튄다 (기지에 가까운 순, 피해 %).</summary>
        void Chain(Tower t, Creep first, int dmg)
        {
            var hit = new List<Creep> { first };
            var cur = first;
            long r2 = Sq(t.Def.ChainRange10 * 100);
            for (int k = 0; k < t.Def.ChainCount; k++)
            {
                Creep next = null;
                foreach (var c in Creeps)
                {
                    if (hit.Contains(c) || !CanTarget(t, c) || Dist2(cur.X, cur.Y, c.X, c.Y) > r2) continue;
                    if (next == null || c.Dist > next.Dist || (c.Dist == next.Dist && c.Id < next.Id)) next = c;
                }
                if (next == null) break;
                hit.Add(next);
                Emit(SimEventType.Attack, t.Id, next.Id, dmg * t.Def.ChainPercent / 100);
                Hit(t, next, Math.Max(1, dmg * t.Def.ChainPercent / 100));
                cur = next;
            }
        }

        void ApplyDamage(Creep c, int dmg, int sourceId)
        {
            if (!c.Alive || dmg <= 0) return;
            c.Hp -= dmg;
            Emit(SimEventType.Damage, sourceId, c.Id, dmg);
            if (c.Hp > 0) return;
            c.Hp = 0; c.Alive = false;
            Kills++; GoldEarned++;
            if (!c.NoKillGold) GoldKills++;
            if (c.Boss) LastBossKillDist = c.Dist;
            Emit(SimEventType.Death, sourceId, c.Id, 0);
            if (Fire5) _fireBursts.Add(c);
        }

        readonly List<Creep> _fireBursts = new List<Creep>();

        /// <summary>불 5 시너지: 죽은 유닛 주변 1칸에 5 피해. 연쇄는 같은 틱 안에서 한 번만.</summary>
        void FlushFireBursts()
        {
            if (_fireBursts.Count == 0) return;
            var centers = new List<Creep>(_fireBursts);
            _fireBursts.Clear();
            foreach (var center in centers)
                foreach (var c in Creeps)
                    if (c.Alive && c.Spawned && Dist2(center.X, center.Y, c.X, c.Y) <= Sq(1000)) ApplyDamage(c, 5, -2);
            _fireBursts.Clear();
        }

        void RecomputeSynergy()
        {
            for (int i = 0; i < 3; i++) { TribeCount[i] = 0; JobPairs[i] = 0; }
            foreach (var t in Towers)
            {
                if (!t.Alive || t.BuildLeft > 0) continue;
                TribeCount[(int)t.Def.Tribe]++;
            }
            Forest3 = TribeCount[0] >= 3; Forest5 = TribeCount[0] >= 5;
            Fire3 = TribeCount[1] >= 3; Fire5 = TribeCount[1] >= 5;
            Machine3 = TribeCount[2] >= 3; Machine5 = TribeCount[2] >= 5;
            // 직업 시너지: 8방향 이웃 칸에 같은 직업의 완성된 타워가 있으면 켜진다
            foreach (var t in Towers)
            {
                t.JobAdj = false;
                if (!t.Alive || t.BuildLeft > 0) continue;
                foreach (var o in Towers)
                {
                    if (!o.Alive || o.BuildLeft > 0 || o.Id == t.Id || o.Def.Job != t.Def.Job) continue;
                    if (Math.Abs(o.Cx - t.Cx) <= 1 && Math.Abs(o.Cy - t.Cy) <= 1) { t.JobAdj = true; break; }
                }
                if (t.JobAdj) JobPairs[(int)t.Def.Job]++;
            }
        }

        void SilenceTowersNear(Creep c, int rangeMilli, int ticks)
        {
            long r2 = Sq(rangeMilli);
            foreach (var t in Towers) if (t.Alive && Dist2(t.X, t.Y, c.X, c.Y) <= r2) Silence(t, ticks, c.Id);
        }

        void Silence(Tower t, int ticks, int sourceId)
        {
            t.SilenceLeft = Math.Max(t.SilenceLeft, ticks);
            Emit(SimEventType.Silence, sourceId, t.Id, ticks);
        }

        static long Sq(long v) => v * v;
        static long Dist2(int x1, int y1, int x2, int y2) { long dx = x1 - x2, dy = y1 - y2; return dx * dx + dy * dy; }
        void Emit(SimEventType type, int a, int b, int value) => Events.Add(new SimEvent { Type = type, A = a, B = b, Value = value, Tick = Tick });

        public string Hash()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Tick).Append('|').Append(BaseHp).Append('|').Append(Leaked).Append('|').Append(Kills).Append('|');
            foreach (var c in Creeps) sb.Append(c.Id).Append(':').Append(c.Dist).Append(',').Append(c.Hp).Append(' ');
            foreach (var t in Towers) if (t.Alive) sb.Append('T').Append(t.Id).Append(':').Append(t.Def.Id).Append('s').Append(t.Star).Append(':').Append(t.CooldownLeft).Append(' ');
            return sb.ToString();
        }
    }
}
