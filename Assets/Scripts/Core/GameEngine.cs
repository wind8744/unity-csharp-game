using System;
using System.Collections.Generic;

namespace LaneBattle.Core
{
    /// <summary>
    /// 순수 C# 게임 룰 엔진. Unity 의존 없음. 같은 (시드, 명령) 입력이면 항상 같은 결과.
    /// 사용법: new GameEngine(cfg, seed) → 1턴이 시작된 상태. 매 턴 ResolveTurn(commands) 호출.
    /// </summary>
    public sealed class GameEngine
    {
        public GameState State { get; }
        public GameConfig Config { get; }
        readonly List<string> _log = new List<string>();
        public IReadOnlyList<string> Log => _log;
        int _nextInstanceId = 1;

        public GameEngine(GameConfig config, ulong seed)
        {
            Config = config;
            State = new GameState(config, seed);
            foreach (var team in State.Teams)
                foreach (var ps in team.Players)
                {
                    ps.Deck = Catalog.DefaultDeck();
                    State.Rng.Shuffle(ps.Deck);
                    Draw(ps, config.StartHand);
                    ps.Mission = Catalog.Missions[State.Rng.Next(Catalog.Missions.Length)];
                }
            BeginTurn();
        }

        void L(string s) { if (Config.EnableLog) _log.Add($"[T{State.Turn}] {s}"); }

        // ─────────────────────────── 조회 헬퍼 ───────────────────────────

        public TeamState Team(int t) => State.Teams[t];
        public PlayerState Player(int t, int p) => State.Teams[t].Players[p];
        public List<UnitInstance> LaneUnits(int team, Lane lane) => State.Teams[team].Lanes[(int)lane];

        public bool TeamHas(int team, AugmentId a)
        {
            foreach (var p in State.Teams[team].Players) if (p.Has(a)) return true;
            return false;
        }

        public int TribeCount(int team, Tribe tribe)
        {
            int n = 0;
            foreach (var lane in State.Teams[team].Lanes)
                foreach (var u in lane) if (u.Def.Tribe == tribe) n++;
            return n;
        }

        public int TribeThreshold(int team, int baseThreshold) => baseThreshold - (TeamHas(team, AugmentId.Bond) ? 1 : 0);

        public bool HasTribe(int team, Tribe tribe, int baseThreshold) => TribeCount(team, tribe) >= TribeThreshold(team, baseThreshold);

        public int JobCountInLane(int team, Lane lane, Job job)
        {
            int n = 0;
            foreach (var u in LaneUnits(team, lane)) if (u.Def.Job == job) n++;
            return n;
        }

        public int LaneCapacity(Lane lane) => State.RuleAt(lane) == LaneRuleId.Canyon ? Config.CanyonCap : Config.LaneCap;

        public int EffectiveAtk(UnitInstance u)
        {
            int atk = u.Def.Atk + u.BonusAtk;
            if (HasTribe(u.Team, Tribe.Fire, 3)) atk += 1;
            if (u.Def.Job == Job.Archer && JobCountInLane(u.Team, u.Lane, Job.Archer) >= 2) atk += 1;
            return atk;
        }

        public int EffectiveMaxHp(UnitInstance u)
        {
            int hp = u.Def.Hp;
            if (u.Def.Tribe == Tribe.Machine && HasTribe(u.Team, Tribe.Machine, 3)) hp += 1;
            if (u.Def.Job == Job.Warrior && JobCountInLane(u.Team, u.Lane, Job.Warrior) >= 2) hp += 2;
            return hp;
        }

        public int Hp(UnitInstance u) => EffectiveMaxHp(u) - u.Damage;

        public int CostOf(int team, int unitDefId, Lane lane)
        {
            var def = Catalog.Unit(unitDefId);
            int cost = def.Cost;
            if (def.Ability == Ability.GiantDiscount)
            {
                int machines = 0;
                foreach (var u in LaneUnits(team, lane)) if (u.Def.Tribe == Tribe.Machine) machines++;
                if (machines >= 2) cost -= 2;
            }
            if (def.Tribe == Tribe.Machine && HasTribe(team, Tribe.Machine, 5)) cost -= 1;
            return Math.Max(0, cost);
        }

        public bool CanPlace(int team, int player, int unitDefId, Lane lane)
        {
            var ps = Player(team, player);
            if (!ps.Hand.Contains(unitDefId)) return false;
            if (LaneUnits(team, lane).Count >= LaneCapacity(lane)) return false;
            return CostOf(team, unitDefId, lane) <= ps.Mana;
        }

        /// <summary>상대 입장에서 보이는 유닛 (안개로 숨은 유닛 제외).</summary>
        public IEnumerable<UnitInstance> VisibleUnits(int team, Lane lane)
        {
            foreach (var u in LaneUnits(team, lane)) if (!u.Hidden) yield return u;
        }

        // ─────────────────────────── 턴 시작 ───────────────────────────

        void BeginTurn()
        {
            State.Turn++;
            State.TowerDamageThisTurn = new int[2, 3];

            for (int t = 0; t < 2; t++)
            {
                var team = State.Teams[t];
                foreach (var lane in team.Lanes)
                    foreach (var u in lane) { u.Hidden = false; u.Protected = false; u.NoAttackThisTurn = false; }

                if (HasTribe(t, Tribe.Forest, 5))
                    for (int l = 0; l < 3; l++) if (!team.TowerDestroyed[l]) team.TowerHp[l] += 1;

                foreach (var ps in team.Players)
                {
                    ps.Mana = State.Turn;
                    if (ps.Has(AugmentId.Overload)) ps.Mana += 1;
                    if (ps.Has(AugmentId.Gambler) && State.Rng.Coin()) ps.Mana += 2;
                    ps.Mana += ps.PendingBonusMana;
                    ps.PendingBonusMana = 0;
                    ps.ManaSpentThisTurn = 0;
                    ps.RecallUsedThisTurn = false;
                    ps.LanesPlacedThisTurn.Clear();

                    Draw(ps, 1 + (ps.Has(AugmentId.Abundance) ? 1 : 0));

                    ps.Offers.Clear();
                    if (State.Turn == 2 || State.Turn == 5)
                    {
                        var pool = new List<AugmentId>();
                        foreach (var a in Catalog.Augments) if (!ps.Has(a)) pool.Add(a);
                        State.Rng.Shuffle(pool);
                        for (int i = 0; i < 3 && i < pool.Count; i++) ps.Offers.Add(pool[i]);
                    }
                }
            }

            if (State.Turn == 3 || State.Turn == 5)
            {
                var freeLanes = new List<int>();
                for (int l = 0; l < 3; l++) if (State.LaneRules[l] == null) freeLanes.Add(l);
                var rules = new List<LaneRuleId>();
                foreach (var r in Catalog.LaneRules) if (!State.UsedLaneRules.Contains(r)) rules.Add(r);
                if (freeLanes.Count > 0 && rules.Count > 0)
                {
                    int lane = freeLanes[State.Rng.Next(freeLanes.Count)];
                    var rule = rules[State.Rng.Next(rules.Count)];
                    State.LaneRules[lane] = rule;
                    State.UsedLaneRules.Add(rule);
                    L($"라인 규칙 공개: {(Lane)lane} = {rule}");
                }
            }
        }

        public void Draw(PlayerState ps, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (ps.Deck.Count == 0) return;
                int id = ps.Deck[ps.Deck.Count - 1];
                ps.Deck.RemoveAt(ps.Deck.Count - 1);
                if (ps.Hand.Count < Config.HandMax) ps.Hand.Add(id);
                else L($"T{ps.Team}P{ps.Index} 손패 가득: {Catalog.Unit(id).Name} 소각");
            }
        }

        // ─────────────────────────── 턴 해결 ───────────────────────────

        public void ResolveTurn(TurnCommands cmds)
        {
            if (State.IsOver) throw new InvalidOperationException("game is over");

            // 1. 증강 선택
            for (int t = 0; t < 2; t++)
                for (int p = 0; p < Config.PlayersPerTeam; p++)
                {
                    var ps = Player(t, p);
                    if (ps.Offers.Count == 0) continue;
                    var cmd = cmds.Get(t, p);
                    int idx = Math.Max(0, Math.Min(cmd.AugmentChoice, ps.Offers.Count - 1));
                    ApplyAugment(ps, ps.Offers[idx]);
                    ps.Offers.Clear();
                }
            if (State.IsOver) return; // 과부하로 타워가 터질 수 있음

            // 2. 후퇴 명령
            for (int t = 0; t < 2; t++)
                for (int p = 0; p < Config.PlayersPerTeam; p++)
                {
                    var cmd = cmds.Get(t, p);
                    if (cmd.RecallInstanceId >= 0) TryRecall(t, p, cmd.RecallInstanceId);
                }

            // 3. 배치 (확정 순서: 팀0 P0, P1, 팀1 P0, P1)
            var placed = new List<UnitInstance>();
            for (int t = 0; t < 2; t++)
                for (int p = 0; p < Config.PlayersPerTeam; p++)
                    foreach (var pl in cmds.Get(t, p).Placements)
                        TryPlace(t, p, pl.UnitDefId, pl.Lane, placed);

            // 4. 배치 효과 (공개 후, 배치 순서대로)
            foreach (var u in placed)
            {
                if (!LaneUnits(u.Team, u.Lane).Contains(u)) continue; // 앞선 폭발로 이미 죽었을 수 있음
                switch (u.Def.Ability)
                {
                    case Ability.DrawOnPlace:
                        Draw(Player(u.Team, u.Owner), 1);
                        break;
                    case Ability.SpiritBurst:
                        foreach (var e in LaneUnits(1 - u.Team, u.Lane))
                            if (!e.Hidden && !e.Protected) e.Damage += 1;
                        L($"{u.Def.Name} 폭발: {u.Lane}");
                        break;
                }
            }
            ProcessDeaths();
            if (State.IsOver) return;

            // 5. 전투
            for (int l = 0; l < 3; l++)
            {
                ResolveCombat((Lane)l);
                if (State.IsOver) return;
            }

            // 6. 턴 종료 효과
            EndOfTurnEffects();
            if (State.IsOver) return;

            // 7. 미션
            CheckMissions();

            // 8. 경기 종료 판정
            if (State.Turn >= Config.Turns) { FinalScore(); return; }

            BeginTurn();
        }

        void ApplyAugment(PlayerState ps, AugmentId a)
        {
            ps.Augments.Add(a);
            State.Teams[ps.Team].AugmentsPicked.Add(a);
            var team = State.Teams[ps.Team];
            switch (a)
            {
                case AugmentId.Overload:
                    ps.Mana += 1;
                    for (int l = 0; l < 3; l++) if (!team.TowerDestroyed[l]) { team.TowerHp[l] -= Config.OverloadTowerCost; if (team.TowerHp[l] <= 0) DestroyTower(ps.Team, (Lane)l); }
                    break;
                case AugmentId.TopKeeper:
                    if (!team.TowerDestroyed[(int)Lane.Top]) team.TowerHp[(int)Lane.Top] += 5;
                    break;
                case AugmentId.Twins:
                    ps.TwinsArmed = true;
                    break;
            }
            L($"T{ps.Team}P{ps.Index} 증강: {a}");
        }

        void TryRecall(int team, int player, int instanceId)
        {
            var ps = Player(team, player);
            if (!ps.Has(AugmentId.Retreat) || ps.RecallUsedThisTurn) return;
            if (ps.Hand.Count >= Config.HandMax) return;
            foreach (var lane in State.Teams[team].Lanes)
                for (int i = 0; i < lane.Count; i++)
                {
                    var u = lane[i];
                    if (u.InstanceId != instanceId || u.Owner != player) continue;
                    lane.RemoveAt(i);
                    ps.Hand.Add(u.Def.Id);
                    ps.RecallUsedThisTurn = true;
                    L($"T{team}P{player} 후퇴: {u.Def.Name}");
                    ProcessDeaths(); // 시너지 붕괴로 죽는 유닛 처리
                    return;
                }
        }

        bool TryPlace(int team, int player, int unitDefId, Lane lane, List<UnitInstance> placed)
        {
            if (!CanPlace(team, player, unitDefId, lane)) return false;
            var ps = Player(team, player);
            int cost = CostOf(team, unitDefId, lane);
            ps.Mana -= cost;
            ps.ManaSpentThisTurn += cost;
            ps.Hand.Remove(unitDefId);
            ps.LanesPlacedThisTurn.Add(lane);
            var def = Catalog.Unit(unitDefId);
            var u = Spawn(def, team, player, lane, placed);
            if (ps.TwinsArmed && def.Cost == 1 && LaneUnits(team, lane).Count < LaneCapacity(lane))
            {
                ps.TwinsArmed = false;
                Spawn(def, team, player, lane, placed);
                L($"쌍둥이 발동: {def.Name}");
            }
            CheckForestBonus(team);
            return true;
        }

        UnitInstance Spawn(UnitDef def, int team, int player, Lane lane, List<UnitInstance> placed)
        {
            var ps = Player(team, player);
            var rule = State.RuleAt(lane);
            var u = new UnitInstance
            {
                InstanceId = _nextInstanceId++,
                Def = def, Team = team, Owner = player, Lane = lane, PlacedTurn = State.Turn,
                Hidden = rule == LaneRuleId.Fog,
                NoAttackThisTurn = rule == LaneRuleId.Swamp,
                Protected = ps.Has(AugmentId.Ambush),
            };
            LaneUnits(team, lane).Add(u);
            State.Teams[team].UnitsPlaced.Add(def.Id);
            placed.Add(u);
            L($"T{team}P{player} 배치: {def.Name} → {lane}");
            return u;
        }

        void CheckForestBonus(int team)
        {
            var ts = State.Teams[team];
            if (ts.ForestBonusGiven || !HasTribe(team, Tribe.Forest, 3)) return;
            ts.ForestBonusGiven = true;
            for (int l = 0; l < 3; l++) if (!ts.TowerDestroyed[l]) ts.TowerHp[l] += 3;
            L($"T{team} 숲 3 시너지: 타워 +3");
        }

        // ─────────────────────────── 전투 ───────────────────────────

        void ResolveCombat(Lane lane)
        {
            var chain = new int[2];
            var towerHits = new List<int>[] { new List<int>(), new List<int>() };
            var targets = new List<UnitInstance>[2];
            var guards = new int[2];

            for (int side = 0; side < 2; side++)
            {
                int enemy = 1 - side;
                targets[enemy] = new List<UnitInstance>();
                foreach (var e in LaneUnits(enemy, lane)) if (!e.Hidden && !e.Protected) targets[enemy].Add(e);
                foreach (var e in LaneUnits(enemy, lane)) if (e.Def.Ability == Ability.TowerGuard && !e.Hidden) guards[enemy]++;
            }

            for (int side = 0; side < 2; side++)
            {
                int enemy = 1 - side;
                foreach (var u in LaneUnits(side, lane))
                {
                    if (u.Hidden || u.NoAttackThisTurn) continue;
                    int atk = EffectiveAtk(u);
                    if (atk <= 0) continue;
                    if (u.Def.Ability == Ability.TowerSniper || targets[enemy].Count == 0) towerHits[side].Add(atk);
                    else chain[side] += atk;
                }
            }

            // 동시 적용: 전투 시작 시점 스냅샷 기준
            for (int side = 0; side < 2; side++)
            {
                int enemy = 1 - side;
                int remaining = chain[side];
                foreach (var e in targets[enemy])
                {
                    if (remaining <= 0) break;
                    int hp = Hp(e);
                    if (hp <= 0) continue;
                    int dealt = Math.Min(hp, remaining);
                    e.Damage += dealt;
                    remaining -= dealt;
                }
                int towerTotal = 0;
                foreach (var hit in towerHits[side]) towerTotal += Math.Max(0, hit - guards[enemy]);
                if (towerTotal > 0) DamageTower(enemy, lane, towerTotal);
            }

            ProcessDeaths();
        }

        /// <summary>타워 피해. 고지 규칙(턴당 최대치)을 적용한다.</summary>
        public void DamageTower(int team, Lane lane, int amount)
        {
            var ts = State.Teams[team];
            int l = (int)lane;
            if (ts.TowerDestroyed[l] || amount <= 0) return;
            if (State.RuleAt(lane) == LaneRuleId.HighGround)
            {
                int room = Config.HighGroundCap - State.TowerDamageThisTurn[team, l];
                amount = Math.Max(0, Math.Min(amount, room));
                if (amount == 0) return;
            }
            State.TowerDamageThisTurn[team, l] += amount;
            ts.TowerHp[l] -= amount;
            L($"T{team} {lane} 타워 -{amount} → {ts.TowerHp[l]}");
            if (ts.TowerHp[l] <= 0) DestroyTower(team, lane);
        }

        void DestroyTower(int team, Lane lane)
        {
            var ts = State.Teams[team];
            int l = (int)lane;
            if (ts.TowerDestroyed[l]) return;
            ts.TowerHp[l] = 0;
            ts.TowerDestroyed[l] = true;
            L($"T{team} {lane} 타워 파괴");

            int attacker = 1 - team;
            if (lane == Lane.Mid)
                foreach (var ps in State.Teams[attacker].Players)
                    if (ps.Mission == MissionId.MidFocus && !ps.MissionDone) CompleteMission(ps);

            if (State.RuleAt(lane) == LaneRuleId.Herald) EndGame(attacker, "전령 타워 파괴");
            else if (ts.TowersDestroyedCount() >= 2) EndGame(attacker, "타워 2개 파괴");
        }

        void ProcessDeaths()
        {
            for (int guard = 0; guard < 64; guard++)
            {
                var dead = new List<UnitInstance>();
                for (int t = 0; t < 2; t++)
                    foreach (var lane in State.Teams[t].Lanes)
                        foreach (var u in lane) if (Hp(u) <= 0) dead.Add(u);
                if (dead.Count == 0) return;

                foreach (var d in dead)
                {
                    var lane = LaneUnits(d.Team, d.Lane);
                    if (!lane.Remove(d)) continue;
                    State.TotalDeaths++;
                    L($"사망: T{d.Team} {d.Def.Name} @{d.Lane}");
                    int enemy = 1 - d.Team;

                    if (d.Def.Ability == Ability.ImpDeathSting)
                        foreach (var e in LaneUnits(enemy, d.Lane))
                            if (!e.Hidden && !e.Protected) { e.Damage += 1; break; }

                    if (HasTribe(enemy, Tribe.Fire, 5)) DamageTower(d.Team, d.Lane, 1);

                    if (State.RuleAt(d.Lane) == LaneRuleId.Jungle)
                    {
                        PlayerState best = null;
                        foreach (var p in State.Teams[enemy].Players)
                            if (best == null || p.Hand.Count < best.Hand.Count) best = p;
                        Draw(best, 1);
                    }
                    if (State.IsOver) return;
                }
            }
        }

        // ─────────────────────────── 턴 종료 ───────────────────────────

        void EndOfTurnEffects()
        {
            for (int t = 0; t < 2; t++)
                foreach (var lane in State.Teams[t].Lanes)
                    foreach (var u in lane) if (u.Def.Ability == Ability.Ramp) u.BonusAtk += 1;

            for (int t = 0; t < 2; t++)
                for (int l = 0; l < 3; l++)
                {
                    var lane = State.Teams[t].Lanes[l];
                    if (lane.Count == 0) continue;
                    int drones = 0;
                    foreach (var u in lane) if (u.Def.Ability == Ability.RepairDrone) drones++;
                    if (drones > 0) lane[0].Damage = Math.Max(0, lane[0].Damage - drones);
                }

            for (int t = 0; t < 2; t++)
                for (int l = 0; l < 3; l++)
                    if (JobCountInLane(t, (Lane)l, Job.Mage) >= 2)
                        foreach (var e in State.Teams[1 - t].Lanes[l]) if (!e.Hidden) e.Damage += 1;

            for (int l = 0; l < 3; l++)
                if (State.LaneRules[l] == LaneRuleId.DragonNest)
                    for (int t = 0; t < 2; t++)
                        foreach (var u in State.Teams[t].Lanes[l]) if (!u.Hidden) u.Damage += 1;

            ProcessDeaths();
        }

        void CheckMissions()
        {
            for (int t = 0; t < 2; t++)
                foreach (var ps in State.Teams[t].Players)
                {
                    var lanes = ps.LanesPlacedThisTurn;
                    if (lanes.Count == 1)
                    {
                        Lane only = Lane.Top;
                        foreach (var l in lanes) only = l;
                        if (ps.LaneStreak > 0 && ps.StreakLane == only) ps.LaneStreak++;
                        else { ps.StreakLane = only; ps.LaneStreak = 1; }
                    }
                    else ps.LaneStreak = 0;

                    if (ps.MissionDone) continue;
                    bool done = false;
                    switch (ps.Mission)
                    {
                        case MissionId.Disguise: done = ps.LaneStreak >= 3; break;
                        case MissionId.Balance: done = lanes.Count == 3; break;
                        case MissionId.Restraint: done = State.Turn >= 3 && ps.ManaSpentThisTurn == 0; break;
                        case MissionId.Attrition: done = State.TotalDeaths >= Config.AttritionDeaths; break;
                        case MissionId.Purebred:
                            foreach (var lane in State.Teams[t].Lanes)
                            {
                                var count = new int[3];
                                foreach (var u in lane) count[(int)u.Def.Tribe]++;
                                foreach (var c in count) if (c >= 4) done = true;
                            }
                            break;
                    }
                    if (done) CompleteMission(ps);
                }
        }

        void CompleteMission(PlayerState ps)
        {
            ps.MissionDone = true;
            ps.MissionDoneTurn = State.Turn;
            ps.PendingBonusMana += 3;
            var ts = State.Teams[ps.Team];
            for (int l = 0; l < 3; l++) if (!ts.TowerDestroyed[l]) ts.TowerHp[l] += 2;
            L($"T{ps.Team}P{ps.Index} 미션 달성: {ps.Mission}");
        }

        void FinalScore()
        {
            var a = State.Teams[0]; var b = State.Teams[1];
            int da = b.TowersDestroyedCount(), db = a.TowersDestroyedCount(); // 각 팀이 파괴한 상대 타워 수
            if (da != db) { EndGame(da > db ? 0 : 1, "파괴한 타워 수"); return; }
            int ha = a.TowerHpSum(), hb = b.TowerHpSum();
            if (ha != hb) { EndGame(ha > hb ? 0 : 1, "남은 타워 체력 합"); return; }
            int ma = a.TowerHp[(int)Lane.Mid], mb = b.TowerHp[(int)Lane.Mid];
            if (ma != mb) { EndGame(ma > mb ? 0 : 1, "미드 타워 체력"); return; }
            EndGame(-1, "무승부");
        }

        void EndGame(int winner, string reason)
        {
            if (State.IsOver) return;
            State.IsOver = true;
            State.Winner = winner;
            State.EndReason = reason;
            L($"경기 종료: 승자 {winner} ({reason})");
        }

        // ─────────────────────────── 테스트 훅 ───────────────────────────

        /// <summary>테스트용: 손패를 강제로 설정.</summary>
        public void DebugSetHand(int team, int player, params int[] unitDefIds)
        {
            var ps = Player(team, player);
            ps.Hand.Clear();
            ps.Hand.AddRange(unitDefIds);
        }

        /// <summary>테스트용: 마나를 강제로 설정.</summary>
        public void DebugSetMana(int team, int player, int mana) => Player(team, player).Mana = mana;

        /// <summary>테스트용: 비용/손패 검사 없이 유닛을 라인에 올린다.</summary>
        public UnitInstance DebugSpawn(int team, int player, int unitDefId, Lane lane)
        {
            var placed = new List<UnitInstance>();
            var u = Spawn(Catalog.Unit(unitDefId), team, player, lane, placed);
            u.Hidden = false; u.Protected = false; u.NoAttackThisTurn = false;
            CheckForestBonus(team);
            return u;
        }

        public void DebugSetLaneRule(Lane lane, LaneRuleId? rule)
        {
            State.LaneRules[(int)lane] = rule;
            if (rule.HasValue) State.UsedLaneRules.Add(rule.Value);
        }

        public void DebugGrantAugment(int team, int player, AugmentId a) => ApplyAugment(Player(team, player), a);
    }
}
