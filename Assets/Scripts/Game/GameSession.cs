namespace LaneBattle.Game
{
    /// <summary>타이틀 → 경기로 넘길 설정. 씬 하나라 정적으로 둔다.</summary>
    public static class GameSession
    {
        public static int PlayersPerTeam = 1;
        public static ulong Seed = 1;
        public static int BotAggression = 65;
        public static bool HumanIsBot;      // -bots: 사람 자리도 봇이 둔다 (스크린샷·테스트)
        public static string PlayerName = "나";
        public static int MatchesPlayed;
        public static int Wins;
        public static string MapName = "";  // 빈 값이면 인원수 기본 맵. 해금한 맵 이름이면 그 맵 (오프라인만)
        public static bool HardBot;         // 해금한 고수 봇
        public static bool NoSave;          // 스크린샷·테스트: 프로필 저장 안 함
    }
}
