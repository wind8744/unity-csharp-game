using LaneBattle.Core.Wave;

namespace LaneBattle.Game
{
    public static class TowerInfo
    {
        /// <summary>역할 이름과 아이콘 (규칙은 Core 의 TowerRoles).</summary>
        public static (string label, string icon) Role(TowerDef d) => TowerRoles.Of(d);

        /// <summary>타워 한 줄 설명: [역할] 공격·공속·사거리·특성.</summary>
        public static string Describe(TowerDef d)
        {
            string s = $"[{Role(d).label}] " + (d.IsSupport ? "공격 없음" : $"공격 {d.Atk} · 공속 {d.AttacksPer10s / 10f:0.#}/초 · 사거리 {d.Range10 / 10f:0.#}");
            s += d.AntiAir ? " · 대공" : "";
            if (d.SplashRadius10 > 0) s += $" · 광역 {d.SplashRadius10 / 10f:0.#}";
            if (d.BurnPerSec > 0) s += $" · 화상 {d.BurnPerSec}/초";
            if (d.SlowPercent > 0) s += $" · 감속 {d.SlowPercent}%";
            if (d.AuraAtkPercent > 0) s += $" · 주변 타워 공격 +{d.AuraAtkPercent}%";
            if (d.AuraSpeedPercentMachine > 0) s += $" · 주변 기계 공속 +{d.AuraSpeedPercentMachine}%";
            if (d.AirMultiplier > 1) s += " · 공중 2배";
            if (d.GiantMultiplier > 1) s += " · 거인 2배";
            if (d.ChainCount > 0) s += $" · 연쇄 {d.ChainCount}회 ({d.ChainPercent}%)";
            return s + $"  [{Tribe(d.Tribe)}·{Job(d.Job)}]";
        }

        public static string Tribe(DefTribe t) => t switch { DefTribe.Forest => "숲", DefTribe.Fire => "불", _ => "기계" };
        public static string Job(DefJob j) => j switch { DefJob.Warrior => "전사", DefJob.Archer => "궁수", _ => "마법사" };
    }
}
