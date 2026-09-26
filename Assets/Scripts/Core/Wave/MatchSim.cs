using System;
using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    public sealed class MatchConfig
    {
        public int PlayersPerTeam = 1;
        public int TicksPerSecond = 20;
        public int MatchSeconds = 480;          // v0.9: 10분 → 8분 (템포)
        public int StartGold = 50;
        public int BaseIncomePerPlayer = 10;
        public int IncomeIntervalSeconds = 15;
        public int DrawCost = 4;
        public int HandMax = 8;
        public int KillGold = 1;
        public int TowerDamagePercent = 160;   // 밸런스 전역 배율 (봇 대전 스윕으로 결정, 문서 v0.4 16절)
        public int SendCostPercent = 85;       // 보내기 비용 배율
        public int Tier2DamagePercent = 85, Tier3DamagePercent = 75;     // 합성 타워 공격 배율 (문서 v0.4 16절)
        public int SendIncomePercent = 50;     // 보낼 때 오르는 인컴 배율 (정수 나눗셈, 최소 1)
        public int BaseHpOverride = 0;          // 0 이면 인원수 기본값
        public int WaveScaleOverride = 0;       // 0 이면 인원수 기본값
        public int LateWaveStepPercent = 8;     // 10번째 웨이브부터 웨이브마다 기본 웨이브 체력 +8%
        public int CreepSpeedPercent = 65;      // 유닛 속도 배율 (v0.9: 50 → 65)
        public int FirstWaveSeconds = 20;       // 기본 웨이브 시간표 (v0.9: 30/30 → 20/24)
        public int WaveIntervalSeconds = 24;
        public int BuildSeconds = 1;            // 건설 시간 (v0.9: 2 → 1)
        public MapDef MapOverride;              // null 이면 인원수 기본 맵 (MapCatalog)
        public int[] AugmentLevels = { 1, 3, 6, 9 };  // 이 레벨에 닿으면 증강 3장 중 1장 (개인별, 게임은 계속). 문서 v0.4 15절
        public int AugmentChoiceSeconds = 20;         // 안 고르면 자동 선택
        public int AugmentPauseSeconds = 0;           // 0 = 정지 없음 (예전 방식은 시간표+정지였다)
        public int[] EventSeconds = { 210, 360 };     // 이벤트 시간대 시작
        public int EventDurationSeconds = 45;
        public int EventWarnSeconds = 30;
        public bool FunLayer = true;                  // 증강·이벤트·미션·시너지 켜기
        public bool SendUpgradesEnabled = true;       // 돌격 강화 허용
        public bool HandFusion = true;                // 손패 조합 → 히든 유닛
        public HashSet<AugmentId> AllowedSecretAugments; // 해금한 비밀 증강 (null 이면 없음), 모든 자리에 적용
        public HashSet<AugmentId>[] SecretAugmentsPerSlot; // 온라인: 슬롯(팀×인원+자리)별 각자 해금한 것. 있으면 위 값보다 우선

        // 인원수별 기본값 (봇 스윕으로 결정, 문서 v0.4 3·8절): 기지 40/120/360, 웨이브 100/130/130%
        public MapDef Map => MapOverride ?? MapCatalog.ForPlayers(PlayersPerTeam);
        public int BaseHp => BaseHpOverride > 0 ? BaseHpOverride : PlayersPerTeam switch { 1 => 60, 2 => 200, _ => 350 };
        public int WaveScalePercent => WaveScaleOverride > 0 ? WaveScaleOverride : PlayersPerTeam switch { 1 => 100, _ => 130 };

        public LaneConfig MakeLaneConfig() => new LaneConfig
        {
            Map = Map, BaseHp = BaseHp, WaveScalePercent = WaveScalePercent,
            TicksPerSecond = TicksPerSecond, MatchSeconds = MatchSeconds, TowerDamagePercent = TowerDamagePercent,
            LateWaveStepPercent = LateWaveStepPercent, CreepSpeedPercent = CreepSpeedPercent, Tier2DamagePercent = Tier2DamagePercent, Tier3DamagePercent = Tier3DamagePercent,
            FirstWaveSeconds = FirstWaveSeconds, WaveIntervalSeconds = WaveIntervalSeconds, BuildSeconds = BuildSeconds,
        };
    }

    public sealed class PlayerEcon
    {
        public int Team, Index;
        public int Gold;
        public List<int> Hand = new List<int>();   // 공격 유닛 id
        public Rng DrawRng;
        public int Drawn, Sent, IncomeReceived;
        public int LastSendTick = -1000;
        public HashSet<AugmentId> Augments = new HashSet<AugmentId>();
        public List<AugmentId> Offers = new List<AugmentId>();
        public MissionId Mission;
        public bool MissionDone;
        public int FreeSends;
        public int OfferDeadlineTick = -1;           // 증강을 골라야 하는 마감 (경기 틱). -1 = 없음
        public int AugmentPicks;                     // 고른 증강 수 (자동 포함)
        public HashSet<int> OfferedLevels = new HashSet<int>();
        public int Level = 1, Xp;                    // 뽑기 등급 확률을 정하는 개인 레벨 (경험치: 수입 때 자동 + 골드로 구매)
        public int MaxStar = 1;                      // 해금 조건용
        public HashSet<int> FusedKinds = new HashSet<int>();
        public HashSet<int> HiddenMade = new HashSet<int>();  // 손패 조합으로 만든 히든 유닛 종류
        public List<int> RecentSendTicks = new List<int>();
        public List<int> RecentHeroSendTicks = new List<int>();
        public bool Has(AugmentId a) => Augments.Contains(a);
        public int DrawCost(MatchConfig cfg) => Math.Max(1, (Has(AugmentId.Merchant) ? 3 : cfg.DrawCost) - (Has(AugmentId.Veteran) ? 1 : 0));
        public int HandMax(MatchConfig cfg) => cfg.HandMax + (Has(AugmentId.Veteran) ? 1 : 0);
    }

    public sealed class TeamEcon
    {
        public int Index;
        public int Income;
        public int SendLevel;                          // 돌격 강화 (팀 공유): 보내는 유닛 체력 +SendLevelHpPercent·속도 +2% /레벨
        public int KillGoldCursor;
        public int TotalKills;
        public int LastLeakTick;
        public List<int> KillTicks = new List<int>();
        public List<(int tick, int defId, int creepId)> RecentSends = new List<(int, int, int)>();
    }

    public enum CommandType { Build, Upgrade, Sell, Draw, Send, Transfer, PickAugment, Merge, Fuse, UpgradeSends, BuyXp, HandFuse }

    /// <summary>플레이어 명령. 락스텝에서 틱 번호와 함께 교환되는 유일한 입력.</summary>
    public struct MatchCommand
    {
        public int Team, Player;
        public CommandType Type;
        public int A, B, C;     // Build: 타워 id, 열, 줄 / Upgrade·Sell: 타워 id / Send: 손패 인덱스 / Transfer: 받는 플레이어, 금액

        public static MatchCommand Build(int team, int player, int towerId, int col, int row) => new MatchCommand { Team = team, Player = player, Type = CommandType.Build, A = towerId, B = col, C = row };
        public static MatchCommand Upgrade(int team, int player, int towerId) => new MatchCommand { Team = team, Player = player, Type = CommandType.Upgrade, A = towerId };
        public static MatchCommand Sell(int team, int player, int towerId) => new MatchCommand { Team = team, Player = player, Type = CommandType.Sell, A = towerId };
        public static MatchCommand Draw(int team, int player) => new MatchCommand { Team = team, Player = player, Type = CommandType.Draw };
        public static MatchCommand Send(int team, int player, int handIndex) => new MatchCommand { Team = team, Player = player, Type = CommandType.Send, A = handIndex };
        public static MatchCommand Transfer(int team, int player, int toPlayer, int amount) => new MatchCommand { Team = team, Player = player, Type = CommandType.Transfer, A = toPlayer, B = amount };
        public static MatchCommand PickAugment(int team, int player, int offerIndex) => new MatchCommand { Team = team, Player = player, Type = CommandType.PickAugment, A = offerIndex };
        /// <summary>손패 합성: 완성된 조합(있으면 첫 번째, A>0 이면 그 결과 id 의 조합)을 히든 카드로 합친다.</summary>
        public static MatchCommand HandFuse(int team, int player, int resultId = 0) => new MatchCommand { Team = team, Player = player, Type = CommandType.HandFuse, A = resultId };
        /// <summary>경험치 사기: XpBuyCost 골드 → XpBuyAmount 경험치.</summary>
        public static MatchCommand BuyXp(int team, int player) => new MatchCommand { Team = team, Player = player, Type = CommandType.BuyXp };
        /// <summary>돌격 강화: 팀의 보내기 레벨을 1 올린다 (비용은 UpgradeSendsCost).</summary>
        public static MatchCommand UpgradeSends(int team, int player) => new MatchCommand { Team = team, Player = player, Type = CommandType.UpgradeSends };
        /// <summary>별 합치기: 타워 A 와 같은 정의·별인 내 타워 두 개(자동: id 낮은 순)를 골라 A 자리에 ★+1.</summary>
        public static MatchCommand Merge(int team, int player, int towerId) => new MatchCommand { Team = team, Player = player, Type = CommandType.Merge, A = towerId };
        /// <summary>별 합치기: 소모할 두 타워를 직접 고른다.</summary>
        public static MatchCommand MergeWith(int team, int player, int towerId, int mate1, int mate2) => new MatchCommand { Team = team, Player = player, Type = CommandType.Merge, A = towerId, B = mate1, C = mate2 };
        /// <summary>합성: 타워 A 와 타워 B (레시피) → A 자리에 합성 타워.</summary>
        public static MatchCommand Fuse(int team, int player, int towerA, int towerB) => new MatchCommand { Team = team, Player = player, Type = CommandType.Fuse, A = towerA, B = towerB };
    }

    public enum MatchEventType { Income, Drew, Sent, Built, Upgraded, Sold, KillGold, Rejected, MatchEnd, AugmentOffer, AugmentPicked, PauseEnd, EventWarn, EventStart, EventEnd, MissionDone, GroupSynergy, Merged, Fused, SendsUpgraded, LevelUp, HandFused }

    public struct MatchEvent
    {
        public MatchEventType Type;
        public int Team, Player, A, B;
        public int Tick;
    }

    /// <summary>
    /// 한 판 전체: 라인 두 개(팀별), 골드·인컴·손패, 시간표, 승패. 결정론.
    /// Lanes[t] 는 팀 t 의 라인 = 팀 t 의 타워가 서 있고, 팀 (1-t) 가 보낸 유닛과 기본 웨이브가 팀 t 기지로 걸어오는 곳.
    /// </summary>
    public sealed class MatchSim
    {
        public MatchConfig Cfg { get; }
        public LaneSim[] Lanes { get; } = new LaneSim[2];
        public TeamEcon[] Teams { get; } = new TeamEcon[2];
        public PlayerEcon[][] Players { get; } = new PlayerEcon[2][];
        public int Tick { get; private set; }
        public bool IsOver { get; private set; }
        public int Winner { get; private set; } = -1;
        public string EndReason { get; private set; } = "";
        public readonly List<MatchEvent> Events = new List<MatchEvent>();
        readonly int[] _lastKills = new int[2];
        readonly Rng _rng;
        public int PauseLeft { get; private set; }
        public int AugmentRound { get; private set; }
        public EventId? ActiveEvent { get; private set; }
        public EventId? NextEvent { get; private set; }
        public int EventEndTick { get; private set; }
        public int EventStartTick { get; private set; }
        public int EventRound { get; private set; }
        readonly List<EventId> _eventPool = new List<EventId>();
        public bool IsPaused => PauseLeft > 0;

        public MatchSim(MatchConfig cfg, ulong seed)
        {
            Cfg = cfg;
            _rng = new Rng(seed * 977 + 3);
            foreach (var e in FunCatalog.Events) _eventPool.Add(e);
            for (int t = 0; t < 2; t++)
            {
                Lanes[t] = new LaneSim(cfg.MakeLaneConfig(), seed * 7 + (ulong)t + 1) { AutoClearEvents = false };
                Teams[t] = new TeamEcon { Index = t, Income = cfg.BaseIncomePerPlayer * cfg.PlayersPerTeam };
                Players[t] = new PlayerEcon[cfg.PlayersPerTeam];
                for (int p = 0; p < cfg.PlayersPerTeam; p++)
                    Players[t][p] = new PlayerEcon { Team = t, Index = p, Gold = cfg.StartGold, DrawRng = new Rng(seed * 131 + (ulong)(t * 10 + p) + 17), Mission = FunCatalog.Missions[_rng.Next(FunCatalog.Missions.Length)] };
            }
            ScheduleNextEvent();
        }

        void ScheduleNextEvent()
        {
            if (!Cfg.FunLayer || EventRound >= Cfg.EventSeconds.Length || _eventPool.Count == 0) { NextEvent = null; return; }
            NextEvent = _eventPool[_rng.Next(_eventPool.Count)];
            _eventPool.Remove(NextEvent.Value);
        }

        public int GameTick => Lanes[0].Tick;
        public int GameSeconds => GameTick / Cfg.TicksPerSecond;

        public int Seconds => GameSeconds;
        public PlayerEcon Player(int team, int player) => Players[team][player];
        public LaneSim OwnLane(int team) => Lanes[team];
        public LaneSim EnemyLane(int team) => Lanes[1 - team];

        // ─────────────────────────── 진행 ───────────────────────────

        public void Step(IList<MatchCommand> commands = null)
        {
            if (IsOver) return;
            Events.Clear();
            for (int t = 0; t < 2; t++) Lanes[t].Events.Clear();   // 이번 틱의 명령 이벤트(판매·합성·합치기)가 라인 진행 이벤트와 함께 남도록 여기서만 비운다
            Tick++;

            if (commands != null) foreach (var c in commands) Apply(c);

            if (PauseLeft > 0)
            {
                PauseLeft--;
                if (PauseLeft == 0) EndPause();
                return;
            }

            ApplyEventModifiers();
            for (int t = 0; t < 2; t++) Lanes[t].Step();
            int gtick = GameTick;

            if (Cfg.FunLayer)
            {
                if (gtick == 1) for (int t = 0; t < 2; t++) foreach (var p in Players[t]) OfferForLevel(p);
                for (int t = 0; t < 2; t++)
                    foreach (var p in Players[t])
                        if (p.Offers.Count > 0 && p.OfferDeadlineTick >= 0 && gtick >= p.OfferDeadlineTick) GrantAugment(p, 0);
                if (NextEvent.HasValue && EventRound < Cfg.EventSeconds.Length)
                {
                    int startTick = Cfg.EventSeconds[EventRound] * Cfg.TicksPerSecond;
                    if (gtick == startTick - Cfg.EventWarnSeconds * Cfg.TicksPerSecond) Emit(MatchEventType.EventWarn, -1, -1, (int)NextEvent.Value, Cfg.EventWarnSeconds);
                    if (gtick == startTick) BeginEvent(NextEvent.Value, startTick);
                }
                if (ActiveEvent.HasValue && gtick >= EventEndTick) { Emit(MatchEventType.EventEnd, -1, -1, (int)ActiveEvent.Value, 0); ActiveEvent = null; }
                if (gtick % Cfg.TicksPerSecond == 0) CheckMissions();
            }

            for (int t = 0; t < 2; t++)
            {
                int kills = Lanes[t].GoldKills - _lastKills[t];
                _lastKills[t] = Lanes[t].GoldKills;
                int goldEach = Cfg.KillGold * (ActiveEvent == EventId.GoldenAge ? 2 : 1);
                for (int k = 0; k < kills; k++)
                {
                    var team = Teams[t];
                    var p = Players[t][team.KillGoldCursor % Cfg.PlayersPerTeam];
                    team.KillGoldCursor++;
                    team.TotalKills++;
                    team.KillTicks.Add(gtick);
                    p.Gold += goldEach;
                    Emit(MatchEventType.KillGold, t, p.Index, goldEach, 0);
                }
                foreach (var e in Lanes[t].Events) if (e.Type == SimEventType.Leak) { Teams[t].LastLeakTick = gtick; break; }
            }

            if (gtick % (Cfg.IncomeIntervalSeconds * Cfg.TicksPerSecond) == 0) PayIncome();

            for (int t = 0; t < 2; t++)
                if (Lanes[t].BaseHp <= 0) { End(1 - t, "기지 파괴"); return; }
            if (gtick >= Cfg.MatchSeconds * Cfg.TicksPerSecond) FinalScore();
        }

        // ─────────────────────────── 증강 ───────────────────────────

        /// <summary>레벨이 증강 레벨(1·3·6·9)에 닿았으면 그 사람에게 증강 3장을 준다. 한 레벨에 한 번.</summary>
        void OfferForLevel(PlayerEcon p)
        {
            if (!Cfg.FunLayer || Array.IndexOf(Cfg.AugmentLevels, p.Level) < 0 || p.OfferedLevels.Contains(p.Level)) return;
            p.OfferedLevels.Add(p.Level);
            if (p.Offers.Count > 0) GrantAugment(p, 0);   // 이전 것을 아직 안 골랐으면 자동 선택하고 새로 준다
            OfferAugments(p);
            AugmentRound = Math.Max(AugmentRound, p.OfferedLevels.Count);
        }

        void OfferAugments(PlayerEcon p)
        {
            p.Offers.Clear();
            p.OfferDeadlineTick = GameTick + Cfg.AugmentChoiceSeconds * Cfg.TicksPerSecond;
            var pool = new List<AugmentId>();
            foreach (var a in FunCatalog.Augments)
            {
                if (p.Has(a)) continue;
                if (FunCatalog.IsSecret(a) && !SecretAllowed(p, a)) continue;
                pool.Add(a);
            }
            for (int i = 0; i < 3 && pool.Count > 0; i++) { var a = pool[_rng.Next(pool.Count)]; pool.Remove(a); p.Offers.Add(a); }
            Emit(MatchEventType.AugmentOffer, p.Team, p.Index, p.Offers.Count, 0);
        }

        bool SecretAllowed(PlayerEcon p, AugmentId a)
        {
            int slot = p.Team * Cfg.PlayersPerTeam + p.Index;
            if (Cfg.SecretAugmentsPerSlot != null && slot < Cfg.SecretAugmentsPerSlot.Length)
                return Cfg.SecretAugmentsPerSlot[slot] != null && Cfg.SecretAugmentsPerSlot[slot].Contains(a);
            return Cfg.AllowedSecretAugments != null && Cfg.AllowedSecretAugments.Contains(a);
        }

        void EndPause()
        {
            for (int t = 0; t < 2; t++)
                foreach (var p in Players[t]) if (p.Offers.Count > 0) GrantAugment(p, 0);
            Emit(MatchEventType.PauseEnd, -1, -1, 0, 0);
        }

        void GrantAugment(PlayerEcon p, int index)
        {
            if (index < 0 || index >= p.Offers.Count) return;
            var a = p.Offers[index];
            p.Offers.Clear();
            p.OfferDeadlineTick = -1;
            p.AugmentPicks++;
            p.Augments.Add(a);
            var lane = Lanes[p.Team];
            switch (a)
            {
                case AugmentId.Legacy: p.Gold += 25; break;
                case AugmentId.Fortress: lane.AddBaseHp(8); break;
                case AugmentId.Elite: foreach (var t in lane.Towers) if (t.Alive && t.Owner == p.Index) t.UpgradePercent = 80; break;
                case AugmentId.AirNet: foreach (var t in lane.Towers) if (t.Alive && t.Owner == p.Index) t.ForceAntiAir = true; break;
                case AugmentId.StarBlessing: foreach (var t in lane.Towers) if (t.Alive && t.Owner == p.Index) t.StarBlessed = true; break;
            }
            Emit(MatchEventType.AugmentPicked, p.Team, p.Index, (int)a, 0);
        }

        // ─────────────────────────── 이벤트 시간대 ───────────────────────────

        void BeginEvent(EventId e, int startTick)
        {
            ActiveEvent = e; EventStartTick = startTick; EventEndTick = startTick + Cfg.EventDurationSeconds * Cfg.TicksPerSecond;
            EventRound++;
            if (e == EventId.Earthquake) for (int t = 0; t < 2; t++) Lanes[t].SilenceAll(3 * Cfg.TicksPerSecond);
            Emit(MatchEventType.EventStart, -1, -1, (int)e, Cfg.EventDurationSeconds);
            ScheduleNextEvent();
        }

        void ApplyEventModifiers()
        {
            var mod = new LaneModifiers { KillGoldMultiplier = 1 };
            switch (ActiveEvent)
            {
                case EventId.Night: mod.TowerRangeDelta10 = -10; break;
                case EventId.Express: mod.CreepSpeedBonusPercent = 30; break;
                case EventId.Fog: mod.FogHalfLane = true; break;
                case EventId.GoldenAge: mod.KillGoldMultiplier = 2; break;
            }
            for (int t = 0; t < 2; t++) Lanes[t].Mod = mod;
        }

        // ─────────────────────────── 미션 ───────────────────────────

        void CheckMissions()
        {
            int gtick = GameTick;
            for (int t = 0; t < 2; t++)
                foreach (var p in Players[t])
                {
                    if (p.MissionDone) continue;
                    var lane = Lanes[t];
                    bool done = false;
                    switch (p.Mission)
                    {
                        case MissionId.Horde: done = CountWithin(p.RecentSendTicks, gtick, 5) >= 5; if (done) p.Gold += 15; break;
                        case MissionId.IronWall: done = gtick >= 120 * Cfg.TicksPerSecond && gtick - Teams[t].LastLeakTick >= 120 * Cfg.TicksPerSecond; if (done) lane.AddBaseHp(4); break;
                        case MissionId.Miser: break;
                        case MissionId.Purebred:
                            for (int tr = 0; tr < 3 && !done; tr++)
                                if (lane.TribeCount[tr] >= 5)
                                {
                                    done = true;
                                    p.Gold += 20; lane.AddBaseHp(2);
                                }
                            break;
                        case MissionId.BossHunter: done = lane.LastBossKillDist >= 0 && lane.LastBossKillDist < lane.Map.LengthMilli / 3; if (done) Lanes[1 - t].AddBaseHp(-4); break;
                        case MissionId.Blitz: done = CountWithin(p.RecentHeroSendTicks, gtick, 5) >= 2; if (done) p.FreeSends += 3; break;
                        case MissionId.Frugal: done = gtick >= 240 * Cfg.TicksPerSecond && AliveTowers(lane) <= 3; if (done) OfferAugments(p); break;
                        case MissionId.Massacre: done = CountWithin(Teams[t].KillTicks, gtick, 30) >= 20; if (done) { p.Gold += 10; lane.AddBaseHp(1); } break;
                    }
                    if (done) { p.MissionDone = true; Emit(MatchEventType.MissionDone, t, p.Index, (int)p.Mission, 0); }
                }
        }

        static int CountWithin(List<int> ticks, int now, int seconds)
        {
            int n = 0;
            foreach (var t in ticks) if (now - t <= seconds * 20) n++;
            return n;
        }

        static int AliveTowers(LaneSim lane) { int n = 0; foreach (var t in lane.Towers) if (t.Alive) n++; return n; }

        /// <summary>손패에 완성된 조합이 있으면 (결과 id, 재료 인덱스). resultId 를 주면 그 조합만. 문서 v0.4 17절.</summary>
        public (int result, List<int> indices)? HandRecipeReady(PlayerEcon p, int resultId = 0)
        {
            if (!Cfg.HandFusion) return null;
            if (resultId <= 0) return WaveCatalog.FindHandRecipe(p.Hand);
            foreach (var (result, parts, _) in WaveCatalog.HandRecipes)
            {
                if (result != resultId) continue;
                var used = new List<int>();
                bool ok = true;
                foreach (var need in parts)
                {
                    int found = -1;
                    for (int i = 0; i < p.Hand.Count; i++) if (p.Hand[i] == need && !used.Contains(i)) { found = i; break; }
                    if (found < 0) { ok = false; break; }
                    used.Add(found);
                }
                if (ok) return (result, used);
            }
            return null;
        }

        /// <summary>손패 합성 (명령으로만, 사람이 R/버튼으로 고른다 — 재료를 따로 보낼 수도 있게).</summary>
        bool DoHandFusion(PlayerEcon p, int resultId)
        {
            var found = HandRecipeReady(p, resultId);
            if (found == null) return false;
            var (result, indices) = found.Value;
            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--) p.Hand.RemoveAt(indices[i]);
            p.Hand.Add(result);
            p.HiddenMade.Add(result);
            Emit(MatchEventType.HandFused, p.Team, p.Index, result, p.Hand.Count - 1);
            return true;
        }

        /// <summary>경험치를 주고 레벨업 처리 (넘치는 경험치는 이월).</summary>
        public void AddXp(PlayerEcon p, int amount)
        {
            if (p.Level >= WaveCatalog.MaxLevel) return;
            p.Xp += amount;
            while (p.Level < WaveCatalog.MaxLevel && p.Xp >= WaveCatalog.XpToNext(p.Level))
            {
                p.Xp -= WaveCatalog.XpToNext(p.Level);
                p.Level++;
                Emit(MatchEventType.LevelUp, p.Team, p.Index, p.Level, 0);
                OfferForLevel(p);
            }
            if (p.Level >= WaveCatalog.MaxLevel) p.Xp = 0;
        }

        void PayIncome()
        {
            for (int t = 0; t < 2; t++)
            {
                int n = Cfg.PlayersPerTeam;
                int share = Teams[t].Income / n, rem = Teams[t].Income % n;
                for (int p = 0; p < n; p++)
                {
                    var pe = Players[t][p];
                    int amount = share + (p < rem ? 1 : 0);
                    if (pe.Has(AugmentId.Interest)) amount += Math.Min(5, pe.Gold / 10);
                    if (!pe.MissionDone && pe.Mission == MissionId.Miser && pe.Gold >= 50)
                    { pe.MissionDone = true; Teams[t].Income += 4; Emit(MatchEventType.MissionDone, t, p, (int)MissionId.Miser, 0); }
                    pe.Gold += amount;
                    pe.IncomeReceived += amount;
                    Emit(MatchEventType.Income, t, p, amount, Teams[t].Income);
                    AddXp(pe, WaveCatalog.XpPerIncome);
                }
            }
        }

        void FinalScore()
        {
            int a = Lanes[0].BaseHp, b = Lanes[1].BaseHp;
            if (a != b) { End(a > b ? 0 : 1, "남은 기지 체력"); return; }
            if (Teams[0].Income != Teams[1].Income) { End(Teams[0].Income > Teams[1].Income ? 0 : 1, "팀 인컴"); return; }
            int ga = 0, gb = 0;
            foreach (var p in Players[0]) ga += p.Gold;
            foreach (var p in Players[1]) gb += p.Gold;
            if (ga != gb) { End(ga > gb ? 0 : 1, "팀 골드"); return; }
            End(-1, "무승부");
        }

        void End(int winner, string reason)
        {
            if (IsOver) return;
            IsOver = true; Winner = winner; EndReason = reason;
            Emit(MatchEventType.MatchEnd, winner, -1, 0, 0);
        }

        // ─────────────────────────── 명령 ───────────────────────────

        void Apply(MatchCommand c)
        {
            if (c.Team < 0 || c.Team > 1 || c.Player < 0 || c.Player >= Cfg.PlayersPerTeam) return;
            var p = Players[c.Team][c.Player];
            var lane = Lanes[c.Team];
            if (PauseLeft > 0 && c.Type != CommandType.PickAugment) { Reject(c); return; }
            switch (c.Type)
            {
                case CommandType.PickAugment:
                    if (p.Offers.Count == 0) { Reject(c); return; }
                    GrantAugment(p, Math.Max(0, Math.Min(c.A, p.Offers.Count - 1)));
                    break;
                case CommandType.Build:
                {
                    var def = WaveCatalog.Tower(c.A);
                    if (p.Gold < def.Cost || !lane.CanBuildAt(c.B, c.C)) { Reject(c); return; }
                    var t = lane.Build(def, c.B, c.C, c.Player);
                    if (t == null) { Reject(c); return; }
                    p.Gold -= def.Cost;
                    if (p.Has(AugmentId.Elite)) t.UpgradePercent = 80;
                    if (p.Has(AugmentId.AirNet)) t.ForceAntiAir = true;
                    if (p.Has(AugmentId.StarBlessing)) t.StarBlessed = true;
                    Emit(MatchEventType.Built, c.Team, c.Player, t.Id, def.Id);
                    break;
                }
                case CommandType.Upgrade:
                {
                    var t = lane.TowerAt(c.A);
                    if (t == null || t.Upgraded || t.Star < 3) { Reject(c); return; }   // 강화는 ★3 (더 못 올리는) 타워만
                    int upCost = UpgradeCostOf(c.Team, t);
                    if (p.Gold < upCost) { Reject(c); return; }
                    p.Gold -= upCost;
                    if (p.Has(AugmentId.Elite)) t.UpgradePercent = 80;
                    lane.Upgrade(t.Id);
                    Emit(MatchEventType.Upgraded, c.Team, c.Player, t.Id, upCost);
                    break;
                }
                case CommandType.Merge:
                {
                    var t = lane.TowerAt(c.A);
                    if (t == null || t.Owner != c.Player) { Reject(c); return; }
                    (int, int)? mates;
                    if (c.B > 0 && c.C > 0)
                    {
                        var m1 = lane.TowerAt(c.B); var m2 = lane.TowerAt(c.C);
                        bool ok = m1 != null && m2 != null && m1.Id != m2.Id && m1.Id != t.Id && m2.Id != t.Id && m1.Owner == c.Player && m2.Owner == c.Player
                                  && m1.Def.Id == t.Def.Id && m2.Def.Id == t.Def.Id && m1.Star == t.Star && m2.Star == t.Star;
                        mates = ok ? (c.B, c.C) : ((int, int)?)null;
                    }
                    else mates = MergeMates(c.Team, t);
                    if (mates == null) { Reject(c); return; }
                    var r = lane.MergeStar(t.Id, mates.Value.Item1, mates.Value.Item2);
                    if (r == null) { Reject(c); return; }
                    if (p.Has(AugmentId.Elite)) r.UpgradePercent = 80;
                    if (p.Has(AugmentId.AirNet)) r.ForceAntiAir = true;
                    if (p.Has(AugmentId.StarBlessing)) r.StarBlessed = true;
                    if (p.Has(AugmentId.Alchemy) && r.Star >= 3) r.Upgraded = true;   // 연금술: ★3 이 되는 순간 공짜 강화
                    if (r.Star > p.MaxStar) p.MaxStar = r.Star;
                    Emit(MatchEventType.Merged, c.Team, c.Player, r.Id, r.Star);
                    break;
                }
                case CommandType.Fuse:
                {
                    var a = lane.TowerAt(c.A); var b = lane.TowerAt(c.B);
                    if (a == null || b == null || a.Owner != c.Player || b.Owner != c.Player || a.Star != b.Star || WaveCatalog.FindRecipe(a.Def.Id, b.Def.Id) == null) { Reject(c); return; }
                    var r = lane.Fuse(a.Id, b.Id);
                    if (r == null) { Reject(c); return; }
                    if (p.Has(AugmentId.Elite)) r.UpgradePercent = 80;
                    if (p.Has(AugmentId.AirNet)) r.ForceAntiAir = true;
                    if (p.Has(AugmentId.StarBlessing)) r.StarBlessed = true;
                    if (p.Has(AugmentId.Alchemy) && r.Star >= 3) r.Upgraded = true;
                    p.FusedKinds.Add(r.Def.Id);
                    Emit(MatchEventType.Fused, c.Team, c.Player, r.Id, r.Def.Id);
                    break;
                }
                case CommandType.Sell:
                {
                    var t = lane.TowerAt(c.A);
                    if (t == null) { Reject(c); return; }
                    int refund = SellValueOf(t);
                    lane.Sell(t.Id);
                    p.Gold += refund;
                    Emit(MatchEventType.Sold, c.Team, c.Player, t.Id, refund);
                    break;
                }
                case CommandType.Draw:
                {
                    if (p.Gold < p.DrawCost(Cfg) || p.Hand.Count >= p.HandMax(Cfg)) { Reject(c); return; }
                    p.Gold -= p.DrawCost(Cfg);
                    var def = RollAttacker(p.DrawRng, p.Level);
                    p.Hand.Add(def.Id);
                    p.Drawn++;
                    Emit(MatchEventType.Drew, c.Team, c.Player, def.Id, p.Hand.Count - 1);
                    break;
                }
                case CommandType.Send:
                {
                    if (c.A < 0 || c.A >= p.Hand.Count) { Reject(c); return; }
                    var def = WaveCatalog.Attacker(p.Hand[c.A]);
                    if (ActiveEvent == EventId.Storm && def.Flying) { Reject(c); return; }
                    int cost = p.FreeSends > 0 ? 0 : SendCostOf(def);
                    if (p.Gold < cost) { Reject(c); return; }
                    p.Gold -= cost;
                    if (p.FreeSends > 0) p.FreeSends--;
                    p.Hand.RemoveAt(c.A);
                    Teams[c.Team].Income += Math.Max(1, def.Income * Cfg.SendIncomePercent / 100) + (p.Has(AugmentId.Mercenaries) ? 1 : 0);
                    p.Sent++;
                    p.LastSendTick = Tick;
                    int gtick2 = GameTick;
                    p.RecentSendTicks.Add(gtick2);
                    if (def.Rarity >= Rarity.Hero) p.RecentHeroSendTicks.Add(gtick2);
                    var team = Teams[c.Team];
                    bool firstOfGroup = team.RecentSends.Count == 0 || gtick2 - team.RecentSends[team.RecentSends.Count - 1].tick > 5 * Cfg.TicksPerSecond;
                    var creep = EnemyLane(c.Team).Send(def, true, c.Team * 100 + c.Player + 1);
                    int lv = Teams[c.Team].SendLevel;
                    if (lv > 0) { creep.MaxHp = creep.MaxHp * (100 + SendLevelHpPercent * lv) / 100; creep.Hp = creep.MaxHp; creep.SpeedPerTick = Math.Max(1, creep.SpeedPerTick * (100 + SendLevelSpeedPercent * lv) / 100); }
                    if (p.Has(AugmentId.Venom)) { creep.MaxHp = creep.MaxHp * 125 / 100; creep.Hp = creep.MaxHp; }
                    if (p.Has(AugmentId.Curse)) creep.SpawnCurse = true;
                    if (p.Has(AugmentId.Corrosion)) creep.NoKillGold = true;
                    if (p.Has(AugmentId.SilenceShell) && firstOfGroup) creep.SpawnSilenceOverride = (20, 15);
                    team.RecentSends.Add((gtick2, def.Id, creep.Id));
                    ApplyGroupSynergy(c.Team, creep, def);
                    Emit(MatchEventType.Sent, c.Team, c.Player, def.Id, creep.Id);
                    break;
                }
                case CommandType.HandFuse:
                {
                    if (!DoHandFusion(p, c.A)) { Reject(c); return; }
                    break;
                }
                case CommandType.BuyXp:
                {
                    if (p.Level >= WaveCatalog.MaxLevel || p.Gold < WaveCatalog.XpBuyCost) { Reject(c); return; }
                    p.Gold -= WaveCatalog.XpBuyCost;
                    AddXp(p, WaveCatalog.XpBuyAmount);
                    break;
                }
                case CommandType.UpgradeSends:
                {
                    var team = Teams[c.Team];
                    int cost = UpgradeSendsCost(team.SendLevel);
                    if (!Cfg.SendUpgradesEnabled || cost < 0 || p.Gold < cost) { Reject(c); return; }
                    p.Gold -= cost;
                    team.SendLevel++;
                    Emit(MatchEventType.SendsUpgraded, c.Team, c.Player, team.SendLevel, cost);
                    break;
                }
                case CommandType.Transfer:
                {
                    if (c.A < 0 || c.A >= Cfg.PlayersPerTeam || c.A == c.Player || c.B <= 0 || p.Gold < c.B) { Reject(c); return; }
                    p.Gold -= c.B;
                    Players[c.Team][c.A].Gold += c.B;
                    break;
                }
            }
        }

        void Reject(MatchCommand c) => Emit(MatchEventType.Rejected, c.Team, c.Player, (int)c.Type, c.A);

        public const int MaxSendLevel = 6;
        public const int SendLevelHpPercent = 6, SendLevelSpeedPercent = 2;
        /// <summary>돌격 강화 비용: 25, 40, 55 … (최대 6레벨, 그 뒤 -1).</summary>
        public static int UpgradeSendsCost(int level) => level >= MaxSendLevel ? -1 : 25 + level * 15;

        public int SendCostOf(AttackerDef def)
        {
            int cost = Math.Max(1, def.SendCost * Cfg.SendCostPercent / 100);
            if (ActiveEvent == EventId.Bazaar) cost = Math.Max(1, cost / 2);
            return cost;
        }

        /// <summary>판매 환불: 비용 × 3^(별-1) × (강화 2) × 80%.</summary>
        public static int SellValueOf(Tower t)
        {
            int value = t.Def.Cost;
            for (int i = 1; i < t.Star; i++) value *= 3;
            if (t.Upgraded) value *= 2;
            return value * 80 / 100;
        }

        /// <summary>타워 t 와 합칠 수 있는 같은 주인·같은 정의·같은 별 타워 두 개 (id 낮은 순). 없으면 null.</summary>
        public (int, int)? MergeMates(int team, Tower t)
        {
            if (t == null || t.Star >= 3) return null;
            var lane = Lanes[team];
            var ids = new List<int>();
            foreach (var o in lane.Towers)
                if (o.Alive && o.Id != t.Id && o.Owner == t.Owner && o.Def.Id == t.Def.Id && o.Star == t.Star) ids.Add(o.Id);
            if (ids.Count < 2) return null;
            ids.Sort();
            return (ids[0], ids[1]);
        }

        /// <summary>타워 t 와 합칠 수 있는 같은 주인·같은 정의·같은 별 타워 전부 (id 순). 2개 넘으면 사람이 고를 수 있다.</summary>
        public List<int> MergeCandidates(int team, Tower t)
        {
            var ids = new List<int>();
            if (t == null || t.Star >= 3) return ids;
            foreach (var o in Lanes[team].Towers)
                if (o.Alive && o.Id != t.Id && o.Owner == t.Owner && o.Def.Id == t.Def.Id && o.Star == t.Star) ids.Add(o.Id);
            ids.Sort();
            return ids;
        }

        /// <summary>타워 t 와 합쳐 result 가 되는 같은 주인 짝 타워 전부 (id 순).</summary>
        public List<int> FusePartners(int team, Tower t, int resultDefId)
        {
            var ids = new List<int>();
            if (t == null) return ids;
            foreach (var o in Lanes[team].Towers)
            {
                if (!o.Alive || o.Id == t.Id || o.Owner != t.Owner || o.Star != t.Star) continue;
                var def = WaveCatalog.FindRecipe(t.Def.Id, o.Def.Id);
                if (def != null && def.Id == resultDefId) ids.Add(o.Id);
            }
            ids.Sort();
            return ids;
        }

        /// <summary>타워 t 와 합성할 수 있는 같은 주인 타워들 (레시피 짝). (짝 타워 id, 결과 정의).</summary>
        public List<(int partnerId, TowerDef result)> FuseOptions(int team, Tower t)
        {
            var list = new List<(int, TowerDef)>();
            if (t == null) return list;
            var lane = Lanes[team];
            foreach (var o in lane.Towers)
            {
                if (!o.Alive || o.Id == t.Id || o.Owner != t.Owner || o.Star != t.Star) continue;   // 같은 별끼리만 합성
                var def = WaveCatalog.FindRecipe(t.Def.Id, o.Def.Id);
                if (def == null) continue;
                bool dup = false;
                foreach (var (_, d) in list) if (d.Id == def.Id) dup = true;
                if (!dup) list.Add((o.Id, def));
            }
            return list;
        }

        public int UpgradeCostOf(int team, Tower t)
        {
            if (t == null) return 0;
            int cost = t.Def.Cost;
            if (t.Def.Tribe == DefTribe.Machine && Lanes[team].Machine3) cost = cost * 70 / 100;
            return Math.Max(1, cost);
        }

        /// <summary>무리 시너지: 같은 팀이 5초 안에 연달아 보낸 유닛끼리. 새 유닛과 아직 살아 있는 무리 전원에게 적용.</summary>
        void ApplyGroupSynergy(int team, Creep newCreep, AttackerDef def)
        {
            var sends = Teams[team].RecentSends;
            int now = GameTick;
            var enemyLane = Lanes[1 - team];
            var group = new List<Creep>();
            int sameName = 0; var tribeCount = new int[4];
            for (int i = sends.Count - 1; i >= 0; i--)
            {
                if (now - sends[i].tick > 5 * Cfg.TicksPerSecond) break;
                var cdef = WaveCatalog.Attacker(sends[i].defId);
                if (cdef.Id == def.Id) sameName++;
                tribeCount[(int)cdef.Tribe]++;
                var creep = enemyLane.FindCreep(sends[i].creepId);
                if (creep != null) group.Add(creep);
            }
            if (sameName >= 3)
                foreach (var c in group) if (c.Def.Id == def.Id && !c.GroupNameBonus) { c.GroupNameBonus = true; int add = c.MaxHp * 20 / 100; c.MaxHp += add; c.Hp += add; Emit(MatchEventType.GroupSynergy, team, -1, c.Id, 1); }
            for (int tr = 0; tr < 4; tr++)
            {
                if (tribeCount[tr] < 3) continue;
                foreach (var c in group)
                {
                    if ((int)c.Def.Tribe != tr || c.GroupTribeBonus) continue;
                    c.GroupTribeBonus = true;
                    switch ((AtkTribe)tr)
                    {
                        case AtkTribe.Beast: c.SpeedPerTick = c.SpeedPerTick * 120 / 100; break;
                        case AtkTribe.Air: { int add = c.MaxHp * 15 / 100; c.MaxHp += add; c.Hp += add; break; }
                        case AtkTribe.Giant: c.ExtraLeak += 1; break;
                        case AtkTribe.Dark: c.StealthLeft += Cfg.TicksPerSecond; break;
                    }
                    Emit(MatchEventType.GroupSynergy, team, -1, c.Id, 2 + tr);
                }
            }
        }

        /// <summary>뽑기: 등급 확률은 플레이어 레벨에 따라 (WaveCatalog.DrawOdds), 등급 안에서 균등.</summary>
        public static AttackerDef RollAttacker(Rng rng, int level = 1)
        {
            var (c, r, h, _) = WaveCatalog.DrawOdds(level);
            int roll = rng.Next(100);
            var rarity = roll < c ? Rarity.Common : roll < c + r ? Rarity.Rare : roll < c + r + h ? Rarity.Hero : Rarity.Legend;
            var pool = new List<AttackerDef>();
            foreach (var a in WaveCatalog.Attackers) if (a.Rarity == rarity) pool.Add(a);
            return pool[rng.Next(pool.Count)];
        }

        void Emit(MatchEventType type, int team, int player, int a, int b) => Events.Add(new MatchEvent { Type = type, Team = team, Player = player, A = a, B = b, Tick = Tick });

        public string Hash()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Tick).Append('|').Append(Winner).Append('|');
            for (int t = 0; t < 2; t++)
            {
                sb.Append(Lanes[t].Hash()).Append('|').Append(Teams[t].Income).Append('L').Append(Teams[t].SendLevel).Append('|');
                foreach (var p in Players[t]) { sb.Append('g').Append(p.Gold).Append('v').Append(p.Level).Append('x').Append(p.Xp).Append('h'); foreach (var h in p.Hand) sb.Append(h).Append('.'); }
                sb.Append('|');
            }
            return sb.ToString();
        }
    }
}
