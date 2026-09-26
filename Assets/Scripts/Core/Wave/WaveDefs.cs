using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    public enum DefTribe { Forest, Fire, Machine }
    public enum DefJob { Warrior, Archer, Mage }
    public enum AtkTribe { Beast, Air, Giant, Dark }
    public enum Rarity { Common, Rare, Hero, Legend }

    /// <summary>타워 정의. 숫자는 core-rules-v0.3.md 6절. 타워는 체력이 없다.</summary>
    public sealed class TowerDef
    {
        public int Id; public string Name;
        public int Cost, Atk;
        public int AttacksPer10s;       // 공속 ×10 (1.0/초 = 10)
        public int Range10;             // 사거리 ×10 (1.5칸 = 15)
        public DefTribe Tribe; public DefJob Job;
        public bool AntiAir;
        public int SplashRadius10;      // 광역 반경 ×10
        public int BurnPerSec, BurnSeconds;
        public int SlowPercent, SlowSeconds;
        public int AuraAtkPercent;      // 드루이드: 사거리 안 아군 타워 공격 +%
        public int AuraSpeedPercentMachine; // 드론: 사거리 안 기계 타워 공속 +%
        public int AirMultiplier = 1, GiantMultiplier = 1;
        public int ChainCount, ChainRange10, ChainPercent = 70; // 연쇄: 맞은 유닛 주변으로 N번 튄다 (피해 %)
        public int Tier = 1;            // 2 = 합성 타워
        public int RecipeA, RecipeB;    // 합성 재료 타워 id (Tier 2 만)
        public bool IsSupport => Atk == 0;
        public bool IsFused => Tier >= 2;
    }

    /// <summary>공격 유닛 정의. 숫자는 core-rules-v0.3.md 7절. 공격력이 없다.</summary>
    public sealed class AttackerDef
    {
        public int Id; public string Name;
        public Rarity Rarity; public AtkTribe Tribe;
        public int SendCost, Hp;
        public int SpeedMilliPerSec;    // 칸/초 ×1000
        public int Leak, Income;
        public bool Flying;
        public int StealthSeconds;      // 암살자
        public int HealPerSec, HealRange10; // 어둠 사제
        public int SpawnSilenceRange10, SpawnSilenceTenths; // 드래곤: 등장 시 침묵 (초 ×10)
        public int PeriodicSilenceEverySec, PeriodicSilenceTenths; // 흑마법사
        public bool SlowImmune;         // 거대 골렘
    }

    public static class WaveCatalog
    {
        public static readonly TowerDef[] Towers =
        {
            // 기본 9종. 역할이 겹치지 않게: 단일·대공 / 근접 광역 감속 / 지원 / 화상 / 광역 / 대공 특화 / 원거리 저격 / 광역 강타(거인) / 지원. 문서 v0.4 10절.
            new TowerDef { Id = 1, Name = "나무 궁수",    Cost = 6,  Atk = 4,  AttacksPer10s = 10, Range10 = 30, Tribe = DefTribe.Forest,  Job = DefJob.Archer,  AntiAir = true },
            new TowerDef { Id = 2, Name = "가시 덤불",    Cost = 6,  Atk = 3,  AttacksPer10s = 12, Range10 = 15, Tribe = DefTribe.Forest,  Job = DefJob.Warrior, SplashRadius10 = 10, SlowPercent = 30, SlowSeconds = 2 },
            new TowerDef { Id = 3, Name = "숲의 드루이드", Cost = 10, Atk = 0,  AttacksPer10s = 0,  Range10 = 20, Tribe = DefTribe.Forest,  Job = DefJob.Mage,    AuraAtkPercent = 15 },
            new TowerDef { Id = 4, Name = "화염 창탑",    Cost = 7,  Atk = 4,  AttacksPer10s = 10, Range10 = 20, Tribe = DefTribe.Fire,    Job = DefJob.Warrior, BurnPerSec = 3, BurnSeconds = 3 },
            new TowerDef { Id = 5, Name = "불꽃 술사",    Cost = 9,  Atk = 7,  AttacksPer10s = 6,  Range10 = 20, Tribe = DefTribe.Fire,    Job = DefJob.Mage,    SplashRadius10 = 12 },
            new TowerDef { Id = 6, Name = "불사조",      Cost = 14, Atk = 8,  AttacksPer10s = 9,  Range10 = 35, Tribe = DefTribe.Fire,    Job = DefJob.Archer,  AntiAir = true, AirMultiplier = 2 },
            new TowerDef { Id = 7, Name = "태엽 포탑",    Cost = 8,  Atk = 14, AttacksPer10s = 4,  Range10 = 45, Tribe = DefTribe.Machine, Job = DefJob.Archer },
            new TowerDef { Id = 8, Name = "강철 대포",    Cost = 15, Atk = 24, AttacksPer10s = 3,  Range10 = 30, Tribe = DefTribe.Machine, Job = DefJob.Warrior, SplashRadius10 = 7, GiantMultiplier = 2 },
            new TowerDef { Id = 9, Name = "수리 드론",    Cost = 8,  Atk = 0,  AttacksPer10s = 0,  Range10 = 20, Tribe = DefTribe.Machine, Job = DefJob.Mage,    AuraSpeedPercentMachine = 25 },
            // ── 합성 타워 (Tier 2): 재료 두 개의 역할을 합친다. 비용은 재료 합 (판매 환불 기준).
            new TowerDef { Id = 10, Name = "불화살 사수", Cost = 13, Atk = 9,  AttacksPer10s = 12, Range10 = 35, Tribe = DefTribe.Fire,    Job = DefJob.Archer,  AntiAir = true, BurnPerSec = 3, BurnSeconds = 3, Tier = 2, RecipeA = 1, RecipeB = 4 },
            new TowerDef { Id = 11, Name = "가시 폭발꽃", Cost = 15, Atk = 10, AttacksPer10s = 6,  Range10 = 20, Tribe = DefTribe.Forest,  Job = DefJob.Mage,    SplashRadius10 = 15, SlowPercent = 30, SlowSeconds = 2, Tier = 2, RecipeA = 2, RecipeB = 5 },
            new TowerDef { Id = 12, Name = "화염 방사기", Cost = 17, Atk = 7,  AttacksPer10s = 16, Range10 = 25, Tribe = DefTribe.Machine, Job = DefJob.Mage,    SplashRadius10 = 8, BurnPerSec = 3, BurnSeconds = 3, Tier = 2, RecipeA = 7, RecipeB = 5 },
            new TowerDef { Id = 13, Name = "거목 투석기", Cost = 25, Atk = 30, AttacksPer10s = 3,  Range10 = 40, Tribe = DefTribe.Forest,  Job = DefJob.Warrior, SplashRadius10 = 12, GiantMultiplier = 2, Tier = 2, RecipeA = 8, RecipeB = 3 },
            new TowerDef { Id = 14, Name = "번개 비행선", Cost = 22, Atk = 12, AttacksPer10s = 8,  Range10 = 40, Tribe = DefTribe.Machine, Job = DefJob.Archer,  AntiAir = true, AirMultiplier = 2, ChainCount = 3, ChainRange10 = 15, ChainPercent = 70, Tier = 2, RecipeA = 6, RecipeB = 9 },
            new TowerDef { Id = 15, Name = "저격 드론",   Cost = 14, Atk = 22, AttacksPer10s = 5,  Range10 = 55, Tribe = DefTribe.Machine, Job = DefJob.Archer,  AntiAir = true, Tier = 2, RecipeA = 1, RecipeB = 9 },
            new TowerDef { Id = 16, Name = "철갑 가시",   Cost = 21, Atk = 18, AttacksPer10s = 5,  Range10 = 20, Tribe = DefTribe.Machine, Job = DefJob.Warrior, SplashRadius10 = 8, SlowPercent = 40, SlowSeconds = 2, GiantMultiplier = 2, Tier = 2, RecipeA = 2, RecipeB = 8 },
            new TowerDef { Id = 17, Name = "화염 정령",   Cost = 19, Atk = 6,  AttacksPer10s = 8,  Range10 = 25, Tribe = DefTribe.Fire,    Job = DefJob.Mage,    SplashRadius10 = 10, AuraAtkPercent = 20, Tier = 2, RecipeA = 3, RecipeB = 5 },
            // ── 3단계 (Tier 3): 합성 타워 둘을 다시 합친다. 비용은 재료 합. 문서 v0.4 16절.
            new TowerDef { Id = 18, Name = "유성 저격수", Cost = 27, Atk = 40, AttacksPer10s = 6,  Range10 = 60, Tribe = DefTribe.Fire,    Job = DefJob.Archer,  AntiAir = true, AirMultiplier = 2, BurnPerSec = 5, BurnSeconds = 3, Tier = 3, RecipeA = 10, RecipeB = 15 },
            new TowerDef { Id = 19, Name = "가시 거목",   Cost = 36, Atk = 34, AttacksPer10s = 5,  Range10 = 25, Tribe = DefTribe.Forest,  Job = DefJob.Warrior, SplashRadius10 = 20, SlowPercent = 50, SlowSeconds = 2, GiantMultiplier = 2, Tier = 3, RecipeA = 11, RecipeB = 16 },
            new TowerDef { Id = 20, Name = "화산 심장",   Cost = 36, Atk = 14, AttacksPer10s = 16, Range10 = 30, Tribe = DefTribe.Fire,    Job = DefJob.Mage,    SplashRadius10 = 15, BurnPerSec = 4, BurnSeconds = 4, AuraAtkPercent = 25, Tier = 3, RecipeA = 12, RecipeB = 17 },
            new TowerDef { Id = 21, Name = "천둥 요새",   Cost = 47, Atk = 45, AttacksPer10s = 4,  Range10 = 50, Tribe = DefTribe.Machine, Job = DefJob.Archer,  AntiAir = true, AirMultiplier = 2, GiantMultiplier = 2, SplashRadius10 = 15, ChainCount = 4, ChainRange10 = 20, ChainPercent = 80, Tier = 3, RecipeA = 13, RecipeB = 14 },
        };

        /// <summary>기본 타워만 (상점에 나오는 것).</summary>
        public static readonly TowerDef[] BasicTowers = System.Array.FindAll(Towers, t => t.Tier == 1);
        public static readonly TowerDef[] FusedTowers = System.Array.FindAll(Towers, t => t.Tier == 2);
        public static readonly TowerDef[] Tier3Towers = System.Array.FindAll(Towers, t => t.Tier == 3);
        /// <summary>상점에 없는 타워 전부 (2·3단계), 합성표 순서.</summary>
        public static readonly TowerDef[] RecipeTowers = System.Array.FindAll(Towers, t => t.Tier >= 2);

        /// <summary>뽑기 등급 확률 (%) — 롤토체스처럼 플레이어 레벨이 정한다. (일반, 희귀, 영웅, 전설). 문서 v0.4 14절.</summary>
        public static (int common, int rare, int hero, int legend) DrawOdds(int level) => level switch
        {
            <= 1 => (80, 20, 0, 0),
            2 => (70, 28, 2, 0),
            3 => (60, 32, 8, 0),
            4 => (50, 34, 14, 2),
            5 => (42, 34, 19, 5),
            6 => (35, 33, 23, 9),
            7 => (28, 32, 27, 13),
            8 => (22, 30, 30, 18),
            _ => (15, 28, 32, 25),
        };
        public const int MaxLevel = 9;
        /// <summary>다음 레벨까지 필요한 경험치 (레벨 1 → 2 부터).</summary>
        public static int XpToNext(int level) => level switch { <= 1 => 2, 2 => 4, 3 => 8, 4 => 12, 5 => 20, 6 => 30, 7 => 44, 8 => 60, _ => int.MaxValue };
        public const int XpPerIncome = 2;       // 수입 때마다 자동 경험치
        public const int XpBuyCost = 4, XpBuyAmount = 4;  // 골드로 경험치 사기

        /// <summary>두 타워 정의로 만들 수 있는 합성 타워. 순서 무관. 없으면 null.</summary>
        public static TowerDef FindRecipe(int defA, int defB)
        {
            foreach (var t in Towers)
                if (t.Tier >= 2 && ((t.RecipeA == defA && t.RecipeB == defB) || (t.RecipeA == defB && t.RecipeB == defA))) return t;
            return null;
        }

        /// <summary>별 등급 공격 배율 (%). ★1 100, ★2 220, ★3 500. 3개를 합치므로 슬롯을 아끼는 대신 합보다 약간 낮거나 비슷하다.</summary>
        public static int StarPercent(int star) => star <= 1 ? 100 : star == 2 ? 220 : 500;
        public static int StarRangeBonus10(int star) => star <= 1 ? 0 : star == 2 ? 5 : 10;

        public static readonly AttackerDef[] Attackers =
        {
            new AttackerDef { Id = 1,  Name = "늑대",     Rarity = Rarity.Common, Tribe = AtkTribe.Beast, SendCost = 4,  Hp = 30,  SpeedMilliPerSec = 2000, Leak = 1, Income = 1 },
            new AttackerDef { Id = 2,  Name = "고블린",   Rarity = Rarity.Common, Tribe = AtkTribe.Beast, SendCost = 4,  Hp = 45,  SpeedMilliPerSec = 1200, Leak = 1, Income = 2 },
            new AttackerDef { Id = 3,  Name = "박쥐",     Rarity = Rarity.Common, Tribe = AtkTribe.Air,   SendCost = 4,  Hp = 25,  SpeedMilliPerSec = 2200, Leak = 1, Income = 1, Flying = true },
            new AttackerDef { Id = 4,  Name = "거북",     Rarity = Rarity.Common, Tribe = AtkTribe.Giant, SendCost = 5,  Hp = 100, SpeedMilliPerSec = 700,  Leak = 1, Income = 1 },
            new AttackerDef { Id = 5,  Name = "오우거",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Giant, SendCost = 8,  Hp = 180, SpeedMilliPerSec = 1000, Leak = 2, Income = 2 },
            new AttackerDef { Id = 6,  Name = "그리핀",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Air,   SendCost = 8,  Hp = 110, SpeedMilliPerSec = 1600, Leak = 2, Income = 2, Flying = true },
            new AttackerDef { Id = 7,  Name = "암살자",   Rarity = Rarity.Rare,   Tribe = AtkTribe.Dark,  SendCost = 8,  Hp = 60,  SpeedMilliPerSec = 1800, Leak = 2, Income = 1, StealthSeconds = 3 },
            new AttackerDef { Id = 8,  Name = "어둠 사제", Rarity = Rarity.Rare,  Tribe = AtkTribe.Dark,  SendCost = 9,  Hp = 70,  SpeedMilliPerSec = 1000, Leak = 1, Income = 2, HealPerSec = 3, HealRange10 = 20 },
            new AttackerDef { Id = 9,  Name = "드래곤",   Rarity = Rarity.Hero,   Tribe = AtkTribe.Air,   SendCost = 14, Hp = 320, SpeedMilliPerSec = 1300, Leak = 3, Income = 4, Flying = true, SpawnSilenceRange10 = 20, SpawnSilenceTenths = 15 },
            new AttackerDef { Id = 10, Name = "거대 골렘", Rarity = Rarity.Hero,  Tribe = AtkTribe.Giant, SendCost = 14, Hp = 450, SpeedMilliPerSec = 600,  Leak = 4, Income = 3, SlowImmune = true },
            new AttackerDef { Id = 11, Name = "흑마법사", Rarity = Rarity.Hero,   Tribe = AtkTribe.Dark,  SendCost = 15, Hp = 150, SpeedMilliPerSec = 1000, Leak = 2, Income = 3, PeriodicSilenceEverySec = 3, PeriodicSilenceTenths = 20 },
            // ── 전설 (4:00 부터 뽑기에 나온다, 문서 v0.4 13절): 비싸고 단단하고 누수·인컴이 크다. 계열마다 하나.
            new AttackerDef { Id = 12, Name = "맹수 왕",   Rarity = Rarity.Legend, Tribe = AtkTribe.Beast, SendCost = 20, Hp = 340, SpeedMilliPerSec = 1800, Leak = 4, Income = 4, StealthSeconds = 2 },
            new AttackerDef { Id = 13, Name = "리치",     Rarity = Rarity.Legend, Tribe = AtkTribe.Dark,  SendCost = 22, Hp = 300, SpeedMilliPerSec = 1000, Leak = 4, Income = 5, HealPerSec = 5, HealRange10 = 25, PeriodicSilenceEverySec = 4, PeriodicSilenceTenths = 20 },
            new AttackerDef { Id = 14, Name = "고대 드래곤", Rarity = Rarity.Legend, Tribe = AtkTribe.Air, SendCost = 26, Hp = 560, SpeedMilliPerSec = 1200, Leak = 5, Income = 6, Flying = true, SpawnSilenceRange10 = 30, SpawnSilenceTenths = 20 },
            new AttackerDef { Id = 15, Name = "타이탄",    Rarity = Rarity.Legend, Tribe = AtkTribe.Giant, SendCost = 24, Hp = 760, SpeedMilliPerSec = 700, Leak = 6, Income = 5, SlowImmune = true },
        };

        static readonly Dictionary<int, TowerDef> _t = Index(Towers, x => x.Id);
        static readonly Dictionary<int, AttackerDef> _a = Index(Attackers, x => x.Id);
        public static TowerDef Tower(int id) => _t[id];
        public static AttackerDef Attacker(int id) => _a[id];

        static Dictionary<int, T> Index<T>(T[] arr, System.Func<T, int> key)
        {
            var d = new Dictionary<int, T>();
            foreach (var x in arr) d[key(x)] = x;
            return d;
        }

        /// <summary>기본 웨이브 시간표 (1v1 기준). (공격 유닛 id, 마리 수, 체력 배수)</summary>
        public static readonly (int id, int count, int hpMult)[][] BaseWaves =
        {
            new[] { (1, 4, 1) },
            new[] { (2, 5, 1) },
            new[] { (1, 4, 1), (3, 2, 1) },
            new[] { (5, 1, 3) },                         // 보스: 오우거 ×3
            new[] { (4, 3, 1), (3, 3, 1) },
            new[] { (2, 6, 1), (1, 4, 1) },
            new[] { (5, 2, 1), (1, 6, 1) },
            new[] { (10, 1, 1) },                        // 보스: 거대 골렘
            new[] { (6, 3, 1), (2, 6, 1) },
            new[] { (7, 3, 1), (4, 4, 1) },
            new[] { (5, 3, 1), (3, 6, 1) },
            new[] { (9, 1, 1) },                         // 보스: 드래곤
            new[] { (6, 4, 1), (1, 8, 1) },
            new[] { (10, 1, 1), (2, 8, 1) },
            new[] { (7, 4, 1), (6, 3, 1) },
            new[] { (11, 1, 3) },                        // 보스: 흑마법사 ×3
            new[] { (5, 4, 1), (9, 1, 1) },
            new[] { (4, 6, 1), (6, 4, 1), (7, 3, 1) },
            new[] { (15, 1, 1), (1, 10, 1) },            // 마지막: 타이탄 + 늑대 떼
        };

        public static bool IsBossWave(int index) => index == 3 || index == 7 || index == 11 || index == 15;

        public static string Describe(int waveIndex)
        {
            if (waveIndex < 0 || waveIndex >= BaseWaves.Length) return "없음";
            var parts = new List<string>();
            foreach (var (id, n, mult) in BaseWaves[waveIndex]) parts.Add($"{Attacker(id).Name} {n}" + (mult > 1 ? $"(체력×{mult})" : ""));
            return (IsBossWave(waveIndex) ? "보스: " : "") + string.Join(", ", parts);
        }
    }

    /// <summary>타워 역할 분류 (수치에서 계산). 기본 9종은 서로 다른 역할을 갖도록 수치를 정했다.</summary>
    public static class TowerRoles
    {
        /// <summary>역할 이름과 아이콘 이름. 상점·합성표·행동 패널에 보인다.</summary>
        public static (string label, string icon) Of(TowerDef d)
        {
            if (d.IsSupport) return (d.AuraAtkPercent > 0 ? "지원·공격 오라" : "지원·공속 오라", "icon_shield");
            var parts = new System.Collections.Generic.List<string>();
            if (d.ChainCount > 0) parts.Add("연쇄");
            if (d.SplashRadius10 >= 10) parts.Add("광역"); else if (d.SplashRadius10 > 0) parts.Add("소광역");
            if (d.Range10 >= 40) parts.Add("원거리");
            if (d.Range10 <= 15) parts.Add("근접");
            if (d.SlowPercent > 0) parts.Add("감속");
            if (d.BurnPerSec > 0) parts.Add("화상");
            if (d.GiantMultiplier > 1) parts.Add("대거인");
            if (d.AirMultiplier > 1) parts.Add("대공 특화"); else if (d.AntiAir) parts.Add("대공");
            if (parts.Count == 0) parts.Add("단일");
            string icon = d.ChainCount > 0 ? "proj_bolt" : d.SplashRadius10 >= 10 ? "fx_boom_1" : d.Range10 >= 40 ? "icon_range" : d.AirMultiplier > 1 ? "icon_wing" : d.SlowPercent > 0 ? "fx_slow" : d.BurnPerSec > 0 ? "fx_burn" : d.GiantMultiplier > 1 ? "icon_sword" : "icon_sword";
            return (string.Join("·", parts), icon);
        }

    }
}
