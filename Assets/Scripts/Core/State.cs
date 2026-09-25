using System.Collections.Generic;

namespace LaneBattle.Core
{
    public sealed class GameConfig
    {
        public int PlayersPerTeam = 2;
        public int Turns = 7;
        public int TowerHp = 15;
        public int LaneCap = 4;
        public int CanyonCap = 2;
        public int StartHand = 3;
        public int HandMax = 7;
        public int HighGroundCap = 5;
        public bool EnableLog = false;
    }

    public sealed class UnitInstance
    {
        public int InstanceId;
        public UnitDef Def;
        public int Team;
        public int Owner;        // 팀 안의 플레이어 인덱스
        public Lane Lane;
        public int Damage;       // 누적 피해
        public int BonusAtk;     // 톱니 궁수 누적
        public int PlacedTurn;
        public bool Hidden;      // 안개: 다음 턴까지 미공개
        public bool Protected;   // 기습: 이번 턴 상대 유닛의 공격을 받지 않음
        public bool NoAttackThisTurn; // 늪
    }

    public sealed class PlayerState
    {
        public int Team;
        public int Index;
        public List<int> Deck = new List<int>();
        public List<int> Hand = new List<int>();
        public int Mana;
        public int ManaSpentThisTurn;
        public int PendingBonusMana;
        public HashSet<AugmentId> Augments = new HashSet<AugmentId>();
        public List<AugmentId> Offers = new List<AugmentId>();
        public MissionId Mission;
        public bool MissionDone;
        public int MissionDoneTurn = -1;
        public bool TwinsArmed;
        public bool RecallUsedThisTurn;
        public HashSet<Lane> LanesPlacedThisTurn = new HashSet<Lane>();
        public Lane StreakLane;
        public int LaneStreak;

        public bool Has(AugmentId a) => Augments.Contains(a);
    }

    public sealed class TeamState
    {
        public int Index;
        public PlayerState[] Players;
        public int[] TowerHp = new int[3];
        public bool[] TowerDestroyed = new bool[3];
        public List<UnitInstance>[] Lanes = { new List<UnitInstance>(), new List<UnitInstance>(), new List<UnitInstance>() };
        public bool ForestBonusGiven;

        // 통계용
        public List<int> UnitsPlaced = new List<int>();
        public List<AugmentId> AugmentsPicked = new List<AugmentId>();

        public int TowersDestroyedCount()
        {
            int n = 0;
            for (int i = 0; i < 3; i++) if (TowerDestroyed[i]) n++;
            return n;
        }

        public int TowerHpSum()
        {
            int n = 0;
            for (int i = 0; i < 3; i++) n += TowerHp[i];
            return n;
        }
    }

    public sealed class GameState
    {
        public int Turn;                 // 0 = 시작 전, 1..Turns
        public TeamState[] Teams = new TeamState[2];
        public LaneRuleId?[] LaneRules = new LaneRuleId?[3];
        public HashSet<LaneRuleId> UsedLaneRules = new HashSet<LaneRuleId>();
        public int[,] TowerDamageThisTurn = new int[2, 3];
        public int TotalDeaths;
        public bool IsOver;
        public int Winner = -1;          // -1 무승부, 0/1 팀
        public string EndReason = "";
        public Rng Rng;

        public GameState(GameConfig cfg, ulong seed)
        {
            Rng = new Rng(seed);
            for (int t = 0; t < 2; t++)
            {
                var team = new TeamState { Index = t, Players = new PlayerState[cfg.PlayersPerTeam] };
                for (int p = 0; p < cfg.PlayersPerTeam; p++)
                    team.Players[p] = new PlayerState { Team = t, Index = p };
                for (int l = 0; l < 3; l++) team.TowerHp[l] = cfg.TowerHp;
                Teams[t] = team;
            }
        }

        public LaneRuleId? RuleAt(Lane lane) => LaneRules[(int)lane];
    }

    public sealed class Placement
    {
        public int UnitDefId;
        public Lane Lane;
        public Placement(int unitDefId, Lane lane) { UnitDefId = unitDefId; Lane = lane; }
    }

    public sealed class PlayerCommand
    {
        public List<Placement> Placements = new List<Placement>();
        public int AugmentChoice;            // Offers 인덱스
        public int RecallInstanceId = -1;    // 후퇴 명령
        public static readonly PlayerCommand Empty = new PlayerCommand();
    }

    public sealed class TurnCommands
    {
        readonly PlayerCommand[,] _cmds;
        public TurnCommands(int playersPerTeam) { _cmds = new PlayerCommand[2, playersPerTeam]; }
        public PlayerCommand Get(int team, int player) => _cmds[team, player] ?? PlayerCommand.Empty;
        public void Set(int team, int player, PlayerCommand cmd) { _cmds[team, player] = cmd; }
    }
}
