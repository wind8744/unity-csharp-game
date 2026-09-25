using System.Text;

namespace LaneBattle.Core
{
    /// <summary>표준 매치업 3종을 돌려 마크다운 보고서를 만든다. Unity 와 콘솔 양쪽에서 쓴다.</summary>
    public static class SimulationReport
    {
        public static string Build(int games, ulong seedBase)
        {
            var cfg = new GameConfig();
            var sb = new StringBuilder();
            sb.AppendLine("# 시뮬레이션 결과 (자동 생성)");
            sb.AppendLine();
            sb.AppendLine($"룰 v0.1, 2v2, 매치업당 {games}판, 시드 {seedBase}부터.");
            sb.AppendLine();
            sb.AppendLine(BatchStats.Run("Random vs Random (사이드 편향 검사)", cfg, new IAgent[] { new RandomAgent(), new RandomAgent() }, games, seedBase).ToMarkdown(false));
            sb.AppendLine(BatchStats.Run("Greedy vs Random (휴리스틱이 무작위보다 나은가)", cfg, new IAgent[] { new GreedyAgent(), new RandomAgent() }, games, seedBase + 100000).ToMarkdown(false));
            sb.AppendLine(BatchStats.Run("Greedy vs Greedy (밸런스 통계)", cfg, new IAgent[] { new GreedyAgent(), new GreedyAgent() }, games, seedBase + 200000).ToMarkdown(true));
            var solo = new GameConfig { PlayersPerTeam = 1 };
            sb.AppendLine(BatchStats.Run("1v1 Greedy vs Greedy (개인전)", solo, new IAgent[] { new GreedyAgent(), new GreedyAgent() }, games, seedBase + 300000).ToMarkdown(false));
            return sb.ToString();
        }
    }
}
