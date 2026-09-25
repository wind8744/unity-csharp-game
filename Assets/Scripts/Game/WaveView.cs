using LaneBattle.Core.Wave;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>1단계 프로토타입: 라인 하나, 골드 없이 타워를 짓고 "상대가 보냄"으로 싸우는 모습을 본다.</summary>
    public sealed class WaveView : MonoBehaviour
    {
        public LaneSim Sim { get; private set; }
        public int PresetIndex = 1;
        public ulong Seed = 1;
        public float Speed = 1f;
        public int SelectedTowerId = 1;

        Sprite _square;
        LaneRenderer _lane;
        float _accumulator, _bannerLeft;
        Text _hud, _banner, _selectedInfo;
        readonly System.Collections.Generic.List<Button> _towerButtons = new System.Collections.Generic.List<Button>();
        bool _paused;

        void Awake()
        {
            _square = LaneRenderer.MakeSquare();
            BuildHud();
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-preset" && int.TryParse(args[i + 1], out int idx)) PresetIndex = Mathf.Clamp(idx, 0, WaveScenarios.Presets.Length - 1);
            Restart();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-send" && int.TryParse(args[i + 1], out int sendIdx)) EnemySend(Mathf.Clamp(sendIdx, 0, WaveScenarios.EnemySends.Length - 1));
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-wavetime" && float.TryParse(args[i + 1], out float sec)) FastForward(sec);
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshot") StartCoroutine(ScreenshotAndQuit(args[i + 1]));
        }

        System.Collections.IEnumerator ScreenshotAndQuit(string path)
        {
            _paused = true;
            yield return new WaitForSeconds(1.0f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(1.5f);
            Application.Quit();
        }

        public void Restart()
        {
            _lane?.Destroy();
            Sim = WaveScenarios.Build(WaveScenarios.Presets[PresetIndex], Seed);
            BuildCamera(Sim.Cfg);
            _lane = new LaneRenderer(transform, _square, Sim, Vector2.zero, "← 상대 유닛 출발", "내 기지 →", "타워 슬롯: 빈 칸 클릭 = 짓기, 다시 클릭 = 강화, 우클릭 = 판매");
            _lane.Banner += ShowBanner;
            _accumulator = 0; _paused = false; _bannerLeft = 0;
            RefreshHud();
        }

        public void FastForward(float seconds)
        {
            int ticks = Mathf.RoundToInt(seconds * Sim.Cfg.TicksPerSecond);
            for (int i = 0; i < ticks && !Sim.IsOver; i++) StepOnce();
            _lane.SnapAll();
        }

        void Update()
        {
            if (Sim == null) return;
            float dt = Time.deltaTime;
            HandleClick();
            if (!_paused && !Sim.IsOver)
            {
                _accumulator += dt * Speed;
                float tickLen = 1f / Sim.Cfg.TicksPerSecond;
                int steps = 0;
                while (_accumulator >= tickLen && steps++ < 8) { _accumulator -= tickLen; StepOnce(); }
                _lane.Interpolate(Mathf.Clamp01(_accumulator / tickLen));
            }
            _lane.UpdateFx(dt);
            if (_bannerLeft > 0) { _bannerLeft -= dt; if (_bannerLeft <= 0) _banner.text = ""; }
            RefreshHud();
        }

        void StepOnce() { _lane.BeforeStep(); Sim.Step(); _lane.AfterStep(); if (Sim.IsOver) { ShowBanner(Sim.BaseHp <= 0 ? "기지 파괴! 패배" : $"10분 종료. 남은 기지 체력 {Sim.BaseHp}"); _bannerLeft = 999f; } }

        void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || Sim.IsOver) return;
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
            if (!left && !right) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            var cam = Camera.main; if (cam == null) return;
            var w = cam.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 10));
            if (!_lane.WorldToSlot(w, out int col, out int row)) return;
            var existing = Sim.TowerAt(col, row);
            if (left) { if (existing == null) Sim.Build(WaveCatalog.Tower(SelectedTowerId), col, row); else Sim.Upgrade(existing.Id); }
            else if (existing != null) Sim.Sell(existing.Id);
            _lane.AfterStep();
        }

        public void EnemySend(int index)
        {
            var (_, id, count) = WaveScenarios.EnemySends[index];
            for (int i = 0; i < count; i++) Sim.Send(WaveCatalog.Attacker(id), true, index + 1);
            ShowBanner($"상대가 {WaveCatalog.Attacker(id).Name} {count}마리를 보냈습니다!");
        }

        void ShowBanner(string text) { _banner.text = text; _bannerLeft = 2.5f; }

        void BuildCamera(LaneConfig lane)
        {
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera", typeof(Camera)); go.tag = "MainCamera"; cam = go.GetComponent<Camera>(); }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.transform.position = new Vector3(lane.Length / 2f + 0.3f, lane.Width / 2f - 1.2f, -10);
            cam.orthographicSize = Mathf.Max((lane.Length + 3f) / cam.aspect / 2f, lane.Width / 2f + 3f);
        }

        void BuildHud()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);
            var root = canvasGo.transform;

            _hud = UiKit.Label(root, "Hud", 20, 10, 1240, 50, "", 17, TextAnchor.UpperLeft, UiKit.Accent);
            _banner = UiKit.Label(root, "Banner", 0, 62, 1280, 34, "", 22, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.4f));
            UiKit.Label(root, "TowerTitle", 20, 500, 400, 22, "지을 타워 (클릭해서 선택)", 13);
            float x = 20;
            foreach (var d in WaveCatalog.Towers)
            {
                int id = d.Id;
                var b = UiKit.ButtonBox(root, "T" + id, x, 524, 128, 44, $"{d.Name}\n{d.Cost}골드 · 공{d.Atk} 사{d.Range10 / 10f:0.#}", () => { SelectedTowerId = id; RefreshTowerButtons(); }, LaneRenderer.TowerColor(d), 11);
                foreach (var txt in b.GetComponentsInChildren<Text>()) txt.color = Color.black;
                _towerButtons.Add(b);
                x += 134;
            }
            _selectedInfo = UiKit.Label(root, "Sel", 20, 572, 1240, 20, "", 12);
            UiKit.Label(root, "SendTitle", 20, 598, 400, 22, "상대가 보냄 (내 라인에 들어옴)", 13);
            x = 20;
            for (int i = 0; i < WaveScenarios.EnemySends.Length; i++)
            {
                int idx = i;
                UiKit.ButtonBox(root, "S" + i, x, 622, 112, 36, WaveScenarios.EnemySends[i].label, () => EnemySend(idx), null, 12);
                x += 118;
            }
            x = 20;
            for (int i = 0; i < WaveScenarios.Presets.Length; i++)
            {
                int idx = i;
                var name = WaveScenarios.Presets[i].Name;
                float w = 30 + name.Length * 11;
                UiKit.ButtonBox(root, "P" + i, x, 668, w, 36, name, () => { PresetIndex = idx; Restart(); }, null, 12);
                x += w + 8;
            }
            UiKit.ButtonBox(root, "Again", 760, 668, 110, 36, "다시 (새 시드)", () => { Seed++; Restart(); }, null, 12);
            UiKit.ButtonBox(root, "Pause", 880, 668, 90, 36, "일시정지", () => _paused = !_paused, null, 12);
            UiKit.ButtonBox(root, "S1", 980, 668, 60, 36, "1배", () => Speed = 1f, null, 12);
            UiKit.ButtonBox(root, "S2", 1050, 668, 60, 36, "2배", () => Speed = 2f, null, 12);
            UiKit.ButtonBox(root, "S4", 1120, 668, 60, 36, "4배", () => Speed = 4f, null, 12);
            UiKit.ButtonBox(root, "S0", 1190, 668, 70, 36, "0.25배", () => Speed = 0.25f, null, 12);
            RefreshTowerButtons();
        }

        void RefreshTowerButtons()
        {
            for (int i = 0; i < _towerButtons.Count; i++)
            {
                var d = WaveCatalog.Towers[i];
                var c = LaneRenderer.TowerColor(d);
                _towerButtons[i].GetComponent<Image>().color = d.Id == SelectedTowerId ? c : new Color(c.r, c.g, c.b, 0.45f);
            }
            if (_selectedInfo != null) _selectedInfo.text = $"선택: {WaveCatalog.Tower(SelectedTowerId).Name} — {TowerInfo.Describe(WaveCatalog.Tower(SelectedTowerId))}   (초록=숲, 주황=불, 파랑=기계)";
        }

        void RefreshHud()
        {
            if (_hud == null || Sim == null) return;
            int sec = Sim.Seconds;
            int next = Sim.TicksToNextWave / Sim.Cfg.TicksPerSecond;
            string nextWave = Sim.NextWaveIndex < WaveCatalog.BaseWaves.Length ? $"다음 웨이브 {Sim.NextWaveIndex + 1} ({next}초 후): {WaveCatalog.Describe(Sim.NextWaveIndex)}" : "기본 웨이브 끝";
            _hud.text = $"{sec / 60}:{sec % 60:00} / {Sim.Cfg.MatchSeconds / 60}:00   |   기지 체력 {Sim.BaseHp} / {Sim.Cfg.BaseHp}   |   처치 {Sim.Kills}   누수 {Sim.Leaked}   |   {Speed}배속{(_paused ? " (일시정지)" : "")}\n{nextWave}";
        }
    }

    public static class TowerInfo
    {
        public static string Describe(TowerDef sel)
        {
            string extra = sel.AntiAir ? "대공 가능" : "대공 불가";
            if (sel.SplashRadius10 > 0) extra += " · 광역";
            if (sel.BurnPerSec > 0) extra += " · 화상";
            if (sel.SlowPercent > 0) extra += " · 감속";
            if (sel.AuraAtkPercent > 0) extra += " · 주변 타워 공격 +15%";
            if (sel.AuraSpeedPercentMachine > 0) extra += " · 주변 기계 타워 공속 +25%";
            if (sel.AirMultiplier > 1) extra += " · 공중에 2배";
            if (sel.GiantMultiplier > 1) extra += " · 거인에 2배";
            return extra;
        }
    }
}
