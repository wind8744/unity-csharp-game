using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>씬 하나의 흐름: 타이틀 ↔ 경기. 명령줄 -mode N 이면 타이틀을 건너뛴다 (스크린샷·테스트).</summary>
    public sealed class GameFlow : MonoBehaviour
    {
        TitleScreen _title;
        MatchView _match;

        void Awake()
        {
            Application.targetFrameRate = 60;
            var args = System.Environment.GetCommandLineArgs();
            int mode = 0; bool titleShot = false; string shot = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-mode" && i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) mode = m;
                if (args[i] == "-title") titleShot = true;
                if (args[i] == "-screenshot" && i + 1 < args.Length) shot = args[i + 1];
            }
            if (mode > 0) StartMatch(mode);
            else ShowTitle();
            if (titleShot && shot != null) StartCoroutine(ScreenshotAndQuit(shot));
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
            if (_match != null) { Destroy(_match.gameObject); _match = null; }
            var cam = Camera.main;
            if (cam != null) { cam.backgroundColor = new Color(0.55f, 0.78f, 0.95f); }
            var go = new GameObject("Title");
            go.transform.SetParent(transform, false);
            _title = go.AddComponent<TitleScreen>();
            _title.OnStart = StartMatch;
        }

        public void StartMatch(int playersPerTeam)
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
