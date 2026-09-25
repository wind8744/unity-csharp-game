namespace LaneBattle.Core
{
    public enum Lane { Top = 0, Mid = 1, Bot = 2 }

    public enum Tribe { Forest, Fire, Machine }

    public enum Job { Warrior, Archer, Mage }

    /// <summary>유닛 고유 효과. 데이터 파일로 옮기기 전까지는 enum + 엔진 switch 로 처리한다.</summary>
    public enum Ability
    {
        None,
        DrawOnPlace,     // 다람쥐 정찰병: 배치 시 1장 드로우
        TowerGuard,      // 나무 수호자: 이 라인의 아군 타워가 받는 피해 -1
        ImpDeathSting,   // 불꽃 임프: 죽을 때 상대 맨 앞 유닛에 1 피해
        TowerSniper,     // 화염 궁수: 항상 타워를 공격
        SpiritBurst,     // 불의 정령: 배치 시 이 라인의 모든 상대 유닛에 1 피해
        Ramp,            // 톱니 궁수: 턴 종료마다 공격력 +1
        RepairDrone,     // 수리 드론: 턴 종료마다 이 라인 아군 맨 앞 유닛 체력 +1
        GiantDiscount,   // 강철 거인: 이 라인에 아군 기계 2 이상이면 코스트 -2
    }

    public enum AugmentId
    {
        Abundance,  // 풍요: 매 턴 드로우 +1
        Overload,   // 과부하: 매 턴 마나 +1, 아군 타워 전체 -3
        TopKeeper,  // 탑 지킴이: 탑 타워 +5
        Retreat,    // 후퇴 명령: 턴당 1회 내 유닛 하나를 손패로
        Twins,      // 쌍둥이: 다음 1코스트 유닛 복사
        Scout,      // 정찰: 상대 손패 1장 공개 (시뮬레이션에서는 효과 없음)
        Gambler,    // 도박사: 매 턴 50% 마나 +2
        Bond,       // 결속: 계열 시너지 임계값 -1
        MindGames,  // 심리전: 상대 핑 보기 (시뮬레이션에서는 효과 없음)
        Ambush,     // 기습: 이번 턴 배치한 유닛은 한 턴 상대 유닛의 공격을 받지 않음
    }

    public enum LaneRuleId
    {
        Swamp,       // 늪: 배치된 턴에 공격 안 함
        Canyon,      // 협곡: 슬롯 4 → 2
        Jungle,      // 정글: 상대 유닛을 죽이면 1장 드로우
        DragonNest,  // 용의 둥지: 턴 종료 시 양쪽 유닛 전원 1 피해
        Fog,         // 안개: 배치가 다음 턴에 공개 (그때까지 공격도 피격도 없음)
        HighGround,  // 고지: 타워는 한 턴에 최대 5 피해
        Herald,      // 전령: 이 타워를 파괴하면 즉시 승리
    }

    public enum MissionId
    {
        MidFocus,   // 미드 타워 파괴
        Disguise,   // 3턴 연속 한 라인에만 배치
        Balance,    // 한 턴에 세 라인 모두 배치
        Restraint,  // 3턴 이후 어느 턴에 마나 0 사용
        Purebred,   // 한 라인에 같은 계열 아군 4명
        Attrition,  // 경기 중 죽은 유닛 8명 이상 (양 팀 합산)
    }
}
