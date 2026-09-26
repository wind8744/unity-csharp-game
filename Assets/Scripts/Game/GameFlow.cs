using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>씬 하나의 흐름: 타이틀 ↔ 경기. 명령줄 -mode N 이면 타이틀을 건너뛴다 (스크린샷·테스트).</summary>
    public sealed class GameFlow : MonoBehaviour
    {
        TitleScreen _title;
        MatchView _match;
        OnlineLobby _lobby;

        void Awake()
        {
            Application.targetFrameRate = 60;
            var args = System.Environment.GetCommandLineArgs();
            int mode = 0; bool titleShot = false, online = false, autoHost = false, tutorial = false; string shot = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-mode" && i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) mode = m;
                if (args[i] == "-title") titleShot = true;
                if (args[i] == "-tutorial") tutorial = true;
                if (args[i] == "-online") online = true;
                if (args[i] == "-host") autoHost = true;
                if (args[i] == "-nosave") GameSession.NoSave = true;
                if (args[i] == "-demo-profile") { var p = new LaneBattle.Core.Meta.Profile(); p.Apply(new LaneBattle.Core.Meta.MatchSummary { Win = true, MyLeaked = 0, MaxStar = 3, Sent = 70 }); ProfileStore.UseTransient(p); GameSession.NoSave = true; }
                if (args[i] == "-screenshot" && i + 1 < args.Length) shot = args[i + 1];
            }
            if (tutorial) StartTutorial();
            else if (mode > 0) StartMatch(mode);
            else if (online) { ShowOnline(); if (autoHost) _lobby.SendMessage("Host"); }
            else ShowTitle();
            if ((titleShot || online) && shot != null) StartCoroutine(ScreenshotAndQuit(shot));
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            yield return new WaitForSeconds(1.0f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1.5f);
            Application.Quit();
        }

        public void ShowTitle()
        {
            if (_match != null) { _match.Net?.Dispose(); Destroy(_match.gameObject); _match = null; }
            if (_lobby != null) { Destroy(_lobby.gameObject); _lobby = null; }
            var cam = Camera.main;
            if (cam != null) { cam.backgroundColor = new Color(0.55f, 0.78f, 0.95f); }
            var go = new GameObject("Title");
            go.transform.SetParent(transform, false);
            _title = go.AddComponent<TitleScreen>();
            _title.OnStart = StartMatch;
            _title.OnTutorial = StartTutorial;
            _title.OnOnline = ShowOnline;
        }

        public void ShowOnline()
        {
            if (_title != null) { Destroy(_title.gameObject); _title = null; }
            var go = new GameObject("OnlineLobby");
            go.transform.SetParent(transform, false);
            _lobby = go.AddComponent<OnlineLobby>();
            _lobby.OnStartMatch = StartOnlineMatch;
            _lobby.OnBack = ShowTitle;
        }

        public void StartOnlineMatch(LaneBattle.Core.Net.NetSession session)
        {
            if (_lobby != null) { _lobby.HandedOver = true; Destroy(_lobby.gameObject); _lobby = null; }
            if (_title != null) { Destroy(_title.gameObject); _title = null; }
            var go = new GameObject("Match");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            _match = go.AddComponent<MatchView>();
            _match.Net = session;
            _match.OnExit = ShowTitle;
            go.SetActive(true);
        }

        /// <summary>튜토리얼: 1v1, 조용한 봇, 7단계 안내 (MatchView 가 GameSession.Tutorial 을 본다).</summary>
        public void StartTutorial() { GameSession.Tutorial = true; StartMatchInner(1); }

        public void StartMatch(int playersPerTeam) { GameSession.Tutorial = false; StartMatchInner(playersPerTeam); }

        void StartMatchInner(int playersPerTeam)
        {
            if (_title != null) { Destroy(_title.gameObject); _title = null; }
            GameSession.PlayersPerTeam = Mathf.Clamp(playersPerTeam, 1, 3);
            GameSession.Seed = (ulong)(System.DateTime.Now.Ticks % 100000) + 1;
            var go = new GameObject("Match");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            _match = go.AddComponent<MatchView>();
            _match.PlayersPerTeam = GameSession.PlayersPerTeam;
            _match.Seed = GameSession.Seed;
            _match.OnExit = ShowTitle;
            go.SetActive(true);
        }
    }
}
