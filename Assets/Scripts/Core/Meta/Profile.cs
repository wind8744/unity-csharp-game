using System;
using System.Collections.Generic;
using System.Text;
using LaneBattle.Core.Wave;

namespace LaneBattle.Core.Meta
{
    /// <summary>한 판이 끝났을 때의 요약. 해금 조건은 이것만 본다.</summary>
    public sealed class MatchSummary
    {
        public bool Win, Online;
        public int PlayersPerTeam;
        public int MyBaseHp, MyBaseMax, MyLeaked;
        public int MaxStar;             // 내 타워 최고 별
        public int FusedKinds;          // 내가 만든 합성 타워 종류 수 (판 전체)
        public int Sent, Kills;
        public MissionId? MissionDone;  // 이번 판에 달성한 비밀 미션
        public int Seconds;
    }

    public enum UnlockKind { Title, Map, Augment, Bot }

    public sealed class UnlockDef
    {
        public string Id, Name, Reward, Hint;
        public UnlockKind Kind;
        public Func<Profile, MatchSummary, bool> Condition;
        public string Payload;          // 맵 이름 / 증강 이름 / 봇 단계
    }

    /// <summary>여러 판에 걸쳐 남는 것: 전적, 해금, 달성한 미션. 조건은 해금 전엔 힌트만 보인다 (문서 v0.3 13절).</summary>
    public sealed class Profile
    {
        public int Matches, Wins, TeamWins, OnlineWins, TotalSent;
        public readonly HashSet<string> Unlocks = new HashSet<string>();
        public readonly HashSet<MissionId> MissionsDone = new HashSet<MissionId>();
        public string Title = "";

        public bool Has(string id) => Unlocks.Contains(id);

        public static readonly UnlockDef[] Catalog =
        {
            new UnlockDef { Id = "first_win", Kind = UnlockKind.Title, Name = "새내기 지휘관", Reward = "칭호", Hint = "이기면 무언가 생긴다", Payload = "새내기 지휘관", Condition = (p, s) => s.Win },
            new UnlockDef { Id = "flawless", Kind = UnlockKind.Map, Name = "굽이 다섯", Reward = "새 맵 (긴 뱀 길)", Hint = "완벽하게 막아라", Payload = "굽이 다섯", Condition = (p, s) => s.Win && s.MyLeaked == 0 },
            new UnlockDef { Id = "star3", Kind = UnlockKind.Augment, Name = "별의 축복", Reward = "비밀 증강: ★2 이상 타워 공격 +10%", Hint = "같은 것을 아주 많이 모으면", Payload = "StarBlessing", Condition = (p, s) => s.MaxStar >= 3 },
            new UnlockDef { Id = "fuse4", Kind = UnlockKind.Augment, Name = "연금술", Reward = "비밀 증강: 합치기·합성 결과가 즉시 강화", Hint = "레시피를 골고루 써 보라", Payload = "Alchemy", Condition = (p, s) => s.FusedKinds >= 4 },
            new UnlockDef { Id = "horde60", Kind = UnlockKind.Bot, Name = "고수 봇", Reward = "봇 난이도 '고수' + 칭호 군단장", Hint = "쉬지 말고 보내라", Payload = "hard", Condition = (p, s) => s.Sent >= 60 },
            new UnlockDef { Id = "cliff", Kind = UnlockKind.Title, Name = "벼랑 끝", Reward = "칭호", Hint = "아슬아슬하게", Payload = "벼랑 끝", Condition = (p, s) => s.Win && s.MyBaseHp <= 5 },
            new UnlockDef { Id = "team_win", Kind = UnlockKind.Map, Name = "굽이 둘", Reward = "새 맵 (짧고 빠른 길)", Hint = "함께 이기면", Payload = "굽이 둘", Condition = (p, s) => s.Win && s.PlayersPerTeam >= 2 },
            new UnlockDef { Id = "online_win", Kind = UnlockKind.Title, Name = "온라인 정복자", Reward = "칭호", Hint = "사람을 이기면", Payload = "온라인 정복자", Condition = (p, s) => s.Win && s.Online },
            new UnlockDef { Id = "veteran10", Kind = UnlockKind.Augment, Name = "노련함", Reward = "비밀 증강: 뽑기 -1골드, 손패 +1", Hint = "꾸준히 하면", Payload = "Veteran", Condition = (p, s) => p.Matches >= 10 },
            new UnlockDef { Id = "all_missions", Kind = UnlockKind.Title, Name = "비밀 사냥꾼", Reward = "칭호", Hint = "모든 비밀을 밝히면", Payload = "비밀 사냥꾼", Condition = (p, s) => p.MissionsDone.Count >= FunCatalog.Missions.Length },
        };

        public static UnlockDef Find(string id) { foreach (var u in Catalog) if (u.Id == id) return u; return null; }

        /// <summary>판 결과를 반영하고 새로 열린 해금 목록을 돌려준다.</summary>
        public List<UnlockDef> Apply(MatchSummary s)
        {
            Matches++;
            if (s.Win) { Wins++; if (s.PlayersPerTeam >= 2) TeamWins++; if (s.Online) OnlineWins++; }
            TotalSent += s.Sent;
            if (s.MissionDone.HasValue) MissionsDone.Add(s.MissionDone.Value);
            var fresh = new List<UnlockDef>();
            foreach (var u in Catalog)
            {
                if (Unlocks.Contains(u.Id)) continue;
                bool ok;
                try { ok = u.Condition(this, s); } catch { ok = false; }
                if (!ok) continue;
                Unlocks.Add(u.Id);
                fresh.Add(u);
                if (u.Kind == UnlockKind.Title || u.Id == "horde60") Title = u.Kind == UnlockKind.Title ? u.Payload : "군단장";
            }
            return fresh;
        }

        public HashSet<AugmentId> AllowedSecretAugments()
        {
            var set = new HashSet<AugmentId>();
            foreach (var u in Catalog)
                if (u.Kind == UnlockKind.Augment && Unlocks.Contains(u.Id) && Enum.TryParse(u.Payload, out AugmentId a)) set.Add(a);
            return set;
        }

        public List<string> UnlockedMaps()
        {
            var list = new List<string>();
            foreach (var u in Catalog) if (u.Kind == UnlockKind.Map && Unlocks.Contains(u.Id)) list.Add(u.Payload);
            return list;
        }

        public bool HardBotUnlocked => Unlocks.Contains("horde60");

        // ─────────────────────────── 저장 ───────────────────────────

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("matches=").Append(Matches).Append('\n');
            sb.Append("wins=").Append(Wins).Append('\n');
            sb.Append("teamwins=").Append(TeamWins).Append('\n');
            sb.Append("onlinewins=").Append(OnlineWins).Append('\n');
            sb.Append("sent=").Append(TotalSent).Append('\n');
            sb.Append("title=").Append(Title ?? "").Append('\n');
            sb.Append("unlocks=").Append(string.Join(",", Unlocks)).Append('\n');
            var ms = new List<string>(); foreach (var m in MissionsDone) ms.Add(m.ToString());
            sb.Append("missions=").Append(string.Join(",", ms)).Append('\n');
            return sb.ToString();
        }

        public static Profile Parse(string text)
        {
            var p = new Profile();
            if (string.IsNullOrEmpty(text)) return p;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                switch (k)
                {
                    case "matches": int.TryParse(v, out p.Matches); break;
                    case "wins": int.TryParse(v, out p.Wins); break;
                    case "teamwins": int.TryParse(v, out p.TeamWins); break;
                    case "onlinewins": int.TryParse(v, out p.OnlineWins); break;
                    case "sent": int.TryParse(v, out p.TotalSent); break;
                    case "title": p.Title = v; break;
                    case "unlocks": foreach (var u in v.Split(',')) if (u.Length > 0) p.Unlocks.Add(u); break;
                    case "missions": foreach (var m in v.Split(',')) if (Enum.TryParse(m, out MissionId id)) p.MissionsDone.Add(id); break;
                }
            }
            return p;
        }
    }
}
