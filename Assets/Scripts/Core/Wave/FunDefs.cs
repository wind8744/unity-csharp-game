using System.Collections.Generic;

namespace LaneBattle.Core.Wave
{
    public enum AugmentId
    {
        Merchant, Interest, Mercenaries, Legacy,          // 자원
        Elite, Venom, AirNet, Fortress,                   // 강화
        Curse, Corrosion, SilenceShell,                   // 방해
        Scout, Wiretap, Accountant,                       // 정보
        StarBlessing, Alchemy, Veteran,                   // 비밀 (해금해야 나온다, 문서 v0.4 11절)
    }

    public enum EventId { Night, Storm, GoldenAge, Express, Fog, Bazaar, Earthquake }

    public enum MissionId { Horde, IronWall, Miser, Purebred, BossHunter, Blitz, Frugal, Massacre }

    public static class FunCatalog
    {
        public static readonly AugmentId[] Augments = (AugmentId[])System.Enum.GetValues(typeof(AugmentId));
        public static bool IsSecret(AugmentId a) => a >= AugmentId.StarBlessing;
        public static readonly EventId[] Events = (EventId[])System.Enum.GetValues(typeof(EventId));
        public static readonly MissionId[] Missions = (MissionId[])System.Enum.GetValues(typeof(MissionId));

        public static string AugmentName(AugmentId a) => a switch
        {
            AugmentId.Merchant => "상인", AugmentId.Interest => "이자", AugmentId.Mercenaries => "용병단", AugmentId.Legacy => "유산",
            AugmentId.Elite => "정예", AugmentId.Venom => "맹독", AugmentId.AirNet => "대공망", AugmentId.Fortress => "요새",
            AugmentId.Curse => "저주", AugmentId.Corrosion => "부식", AugmentId.SilenceShell => "침묵탄",
            AugmentId.Scout => "정찰", AugmentId.Wiretap => "감청", AugmentId.Accountant => "회계",
            AugmentId.StarBlessing => "별의 축복", AugmentId.Alchemy => "연금술", _ => "노련함",
        };

        public static string AugmentGroup(AugmentId a) => (int)a switch { < 4 => "자원", < 8 => "강화", < 11 => "방해", < 14 => "정보", _ => "비밀" };

        public static string AugmentDesc(AugmentId a) => a switch
        {
            AugmentId.Merchant => "뽑기 비용 5 → 3",
            AugmentId.Interest => "수입 때마다 보유 골드의 10% 추가 (최대 +5)",
            AugmentId.Mercenaries => "보낼 때 팀 인컴 +1 추가",
            AugmentId.Legacy => "즉시 골드 +25",
            AugmentId.Elite => "내 타워 강화 효과 +50% → +80%",
            AugmentId.Venom => "내가 보낸 유닛 체력 +25%",
            AugmentId.AirNet => "내 궁수 타워 전원 대공 가능",
            AugmentId.Fortress => "기지 체력 +8",
            AugmentId.Curse => "내가 보낸 유닛 등장 시 사거리 2 안 상대 타워 공속 -20% (3초)",
            AugmentId.Corrosion => "내가 보낸 유닛이 죽어도 상대가 처치 골드를 못 받음",
            AugmentId.SilenceShell => "5초 안에 연달아 보낸 무리의 첫 유닛이 등장 시 타워 1.5초 침묵",
            AugmentId.Scout => "상대 손패 2장이 보인다",
            AugmentId.Wiretap => "상대 팀의 핑이 보인다",
            AugmentId.Accountant => "상대 골드 잔액이 보인다",
            AugmentId.StarBlessing => "★2 이상 타워 공격 +10%",
            AugmentId.Alchemy => "합치기·합성 결과가 즉시 강화 상태",
            _ => "뽑기 비용 -1, 손패 최대 +1",
        };

        public static string EventName(EventId e) => e switch
        {
            EventId.Night => "야간", EventId.Storm => "폭풍", EventId.GoldenAge => "황금기", EventId.Express => "급행",
            EventId.Fog => "안개", EventId.Bazaar => "대목", _ => "지진",
        };

        public static string EventDesc(EventId e) => e switch
        {
            EventId.Night => "타워 사거리 -1",
            EventId.Storm => "공중 유닛 보내기 불가",
            EventId.GoldenAge => "처치 골드 2배",
            EventId.Express => "모든 유닛 속도 +30%",
            EventId.Fog => "상대가 보낸 유닛이 라인 절반까지 안 보인다",
            EventId.Bazaar => "보내기 비용 절반",
            _ => "시작 시 모든 타워 3초 침묵",
        };

        public static string MissionName(MissionId m) => m switch
        {
            MissionId.Horde => "대군", MissionId.IronWall => "철벽", MissionId.Miser => "구두쇠", MissionId.Purebred => "순혈",
            MissionId.BossHunter => "보스 사냥", MissionId.Blitz => "급습", MissionId.Frugal => "검약", _ => "학살",
        };

        public static string MissionDesc(MissionId m) => m switch
        {
            MissionId.Horde => "5초 안에 유닛 5개 이상 보내기 → 골드 +15",
            MissionId.IronWall => "120초 동안 누수 0 → 기지 체력 +4",
            MissionId.Miser => "골드 50 이상 보유한 채 수입 받기 → 팀 인컴 +4",
            MissionId.Purebred => "같은 계열 타워 5개 → 그 계열 타워 전원 즉시 강화",
            MissionId.BossHunter => "보스를 라인 1/3 지점 전에 처치 → 상대 기지 -4",
            MissionId.Blitz => "영웅 등급 2마리를 5초 안에 보내기 → 다음 보내기 3회 무료",
            MissionId.Frugal => "타워 3개 이하로 4:00 넘기기 → 증강 1개 추가 선택",
            _ => "30초 안에 내 라인에서 20마리 처치 → 골드 +10, 기지 +1",
        };
    }

    /// <summary>이벤트 시간대가 라인에 거는 임시 배율. MatchSim 이 매 틱 설정한다.</summary>
    public struct LaneModifiers
    {
        public int TowerRangeDelta10;     // 야간 -10
        public int CreepSpeedBonusPercent; // 급행 +30
        public bool FogHalfLane;          // 안개 (표시용)
        public int KillGoldMultiplier;    // 황금기 2 (MatchSim 이 씀)
    }
}
