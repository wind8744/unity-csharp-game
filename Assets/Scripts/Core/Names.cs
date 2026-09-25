namespace LaneBattle.Core
{
    /// <summary>UI 와 로그용 한글 이름·설명. 룰 숫자를 바꾸면 여기 설명도 같이 바꾼다.</summary>
    public static class Names
    {
        public static string Lane(Lane l) => l switch { Core.Lane.Top => "탑", Core.Lane.Mid => "미드", _ => "봇" };
        public static string Tribe(Tribe t) => t switch { Core.Tribe.Forest => "숲", Core.Tribe.Fire => "불", _ => "기계" };
        public static string Job(Job j) => j switch { Core.Job.Warrior => "전사", Core.Job.Archer => "궁수", _ => "마법사" };

        public static string Augment(AugmentId a) => a switch
        {
            AugmentId.Abundance => "풍요", AugmentId.Overload => "과부하", AugmentId.TopKeeper => "탑 지킴이",
            AugmentId.Retreat => "후퇴 명령", AugmentId.Twins => "쌍둥이", AugmentId.Scout => "정찰",
            AugmentId.Gambler => "도박사", AugmentId.Bond => "결속", AugmentId.MindGames => "심리전", _ => "기습",
        };

        public static string AugmentDesc(AugmentId a) => a switch
        {
            AugmentId.Abundance => "매 턴 드로우 +1",
            AugmentId.Overload => "매 턴 마나 +1, 아군 타워 전체 -1 (즉시)",
            AugmentId.TopKeeper => "탑 타워 체력 +5",
            AugmentId.Retreat => "턴당 1회 내 유닛 하나를 손패로",
            AugmentId.Twins => "다음 1코스트 유닛 복사",
            AugmentId.Scout => "매 턴 상대 손패 1장 공개",
            AugmentId.Gambler => "매 턴 50% 확률로 마나 +2",
            AugmentId.Bond => "계열 시너지 임계값 -1",
            AugmentId.MindGames => "상대 팀의 핑이 보인다",
            _ => "이번 턴 배치한 유닛은 한 턴 동안 공격받지 않음",
        };

        public static string LaneRule(LaneRuleId r) => r switch
        {
            LaneRuleId.Swamp => "늪", LaneRuleId.Canyon => "협곡", LaneRuleId.Jungle => "정글", LaneRuleId.DragonNest => "용의 둥지",
            LaneRuleId.Fog => "안개", LaneRuleId.HighGround => "고지", _ => "전령",
        };

        public static string LaneRuleDesc(LaneRuleId r) => r switch
        {
            LaneRuleId.Swamp => "배치된 턴에는 공격하지 않음",
            LaneRuleId.Canyon => "슬롯 4 → 2",
            LaneRuleId.Jungle => "상대 유닛을 죽이면 1장 드로우",
            LaneRuleId.DragonNest => "턴 종료 시 양쪽 유닛 전원 1 피해",
            LaneRuleId.Fog => "배치가 다음 턴에 공개됨",
            LaneRuleId.HighGround => "타워는 턴당 최대 5 피해",
            _ => "이 타워를 파괴하면 즉시 승리",
        };

        public static string Mission(MissionId m) => m switch
        {
            MissionId.MidFocus => "미드 집중", MissionId.Disguise => "위장", MissionId.Balance => "균형",
            MissionId.Restraint => "절제", MissionId.Purebred => "순혈", _ => "소모전",
        };

        public static string MissionDesc(MissionId m, GameConfig cfg) => m switch
        {
            MissionId.MidFocus => "미드 타워를 파괴하라",
            MissionId.Disguise => "3턴 연속 한 라인에만 배치하라",
            MissionId.Balance => "한 턴에 세 라인 모두에 배치하라",
            MissionId.Restraint => "3턴 이후 어느 턴에 마나를 하나도 쓰지 마라",
            MissionId.Purebred => "한 라인에 같은 계열 아군 4명을 모아라",
            _ => $"경기 중 죽은 유닛이 {cfg.AttritionDeaths}명 이상 (양 팀 합산)",
        };

        public static string Ability(Ability a) => a switch
        {
            Core.Ability.DrawOnPlace => "배치 시 1장 드로우",
            Core.Ability.TowerGuard => "이 라인 아군 타워 피해 -1",
            Core.Ability.ImpDeathSting => "죽을 때 상대 맨 앞에 1 피해",
            Core.Ability.TowerSniper => "항상 타워를 공격",
            Core.Ability.SpiritBurst => "배치 시 이 라인 상대 전원 1 피해",
            Core.Ability.Ramp => "턴 종료마다 공격력 +1",
            Core.Ability.RepairDrone => "턴 종료마다 아군 맨 앞 체력 +1",
            Core.Ability.GiantDiscount => "이 라인에 아군 기계 2 이상이면 코스트 -2",
            _ => "",
        };
    }
}
