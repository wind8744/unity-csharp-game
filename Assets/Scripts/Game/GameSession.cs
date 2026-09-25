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
    }
}
