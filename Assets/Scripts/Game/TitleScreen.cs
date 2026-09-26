using System.Collections.Generic;
using System.Linq;
using LaneBattle.Core.Meta;
using LaneBattle.Core.Wave;
using UnityEngine;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>타이틀: 배경, 로고, 모드 선택(1v1·2v2·3v3), 게임 방법, 합성표, 소리 설정. 아래쪽엔 유닛들이 행진한다.</summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        public System.Action<int> OnStart;
        public System.Action OnOnline;
        Transform _ui, _popup;
        RectTransform _logo;
        float _t;
        readonly List<(RectTransform rt, float speed, Sprite a, Sprite b, float phase)> _parade = new List<(RectTransform, float, Sprite, Sprite, float)>();

        void Awake()
        {
            var root = UiKit.Canvas;
            _ui = UiKit.Rect(root, "TitleUI", 0, 0, 1280, 720);
            var bg = UiKit.Rect(_ui, "Bg", 0, 0, 1280, 720).gameObject.AddComponent<Image>();
            bg.sprite = Art.Get("title_bg"); bg.preserveAspect = false; bg.color = Color.white;
            var bgRt = bg.rectTransform; bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = new Vector2(-40, -40); bgRt.offsetMax = new Vector2(40, 40);

            // 행진하는 유닛들 (장식)
            var rng = new System.Random(3);
            for (int i = 0; i < 9; i++)
            {
                int id = 1 + rng.Next(WaveCatalog.Attackers.Length);
                var d = WaveCatalog.Attacker(id);
                float size = d.Rarity >= Rarity.Hero ? 84 : 64;
                var rt = UiKit.Rect(_ui, "Parade" + i, -100 - i * 140, 560 - (d.Flying ? 60 : 0) + rng.Next(30), size, size);
                var img = rt.gameObject.AddComponent<Image>(); img.sprite = Art.Creep(id, 0); img.preserveAspect = true; img.raycastTarget = false;
                _parade.Add((rt, 40f + d.SpeedMilliPerSec / 40f, Art.Creep(id, 0), Art.Creep(id, 1), (float)rng.NextDouble() * 6f));
            }

            _logo = UiKit.Rect(_ui, "Logo", 320, 60, 640, 200);
            var logo = _logo.gameObject.AddComponent<Image>(); logo.sprite = Art.Get("logo"); logo.preserveAspect = true; logo.raycastTarget = false;
            UiKit.OutlinedLabel(_ui, "Tag", 0, 262, 1280, 24, "귀여운 타워로 막고, 뽑은 유닛을 보내 상대 성을 무너뜨리자!", 15, TextAnchor.MiddleCenter, Color.white);

            UiKit.SpritePanel(_ui, "Menu", 440, 300, 400, 340, "ui_panel");
            UiKit.SpriteButton(_ui, "M1", 470, 322, 340, 44, "혼자 하기 (1 vs 1 봇)", () => OnStart?.Invoke(1), "ui_button_green", 17);
            UiKit.SpriteButton(_ui, "M2", 470, 374, 340, 44, "2 vs 2 (봇 팀원과 함께)", () => OnStart?.Invoke(2), "ui_button", 17);
            UiKit.SpriteButton(_ui, "M3", 470, 426, 340, 44, "3 vs 3 (봇 팀원 둘과 함께)", () => OnStart?.Invoke(3), "ui_button", 17);
            UiKit.SpriteButton(_ui, "Online", 470, 478, 340, 44, "온라인 대전 (친구와 LAN/IP 연결)", () => OnOnline?.Invoke(), "ui_button_blue", 16);
            UiKit.SpriteButton(_ui, "How", 470, 532, 164, 40, "게임 방법", ShowHowTo, "ui_button_grey", 14);
            UiKit.SpriteButton(_ui, "Recipes", 646, 532, 164, 40, "합성표", ShowRecipes, "ui_button_grey", 14);
            UiKit.SpriteButton(_ui, "Sound", 470, 580, 164, 40, "소리 설정", ShowSound, "ui_button_grey", 14);
            UiKit.SpriteButton(_ui, "Quit", 646, 580, 164, 40, "종료", () => Application.Quit(), "ui_button_grey", 14);
            var profile = ProfileStore.Current;
            UiKit.SpriteButton(_ui, "Codex", 860, 322, 150, 40, $"해금 도감 {profile.Unlocks.Count}/{Profile.Catalog.Length}", ShowCodex, "ui_button", 13);
            BuildOptions(profile);
            UiKit.OutlinedLabel(_ui, "Ver", 0, 690, 1270, 20, $"쪼꼬미 공성전 v0.9 · 전적 {profile.Wins}승 {profile.Matches - profile.Wins}패" + (profile.Title.Length > 0 ? $" · 칭호 [{profile.Title}]" : ""), 11, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.85f));
            _popup = UiKit.SpritePanel(_ui, "Popup", 190, 60, 900, 600, "ui_panel").transform;
            _popup.gameObject.SetActive(false);
            Sfx.Music("bgm_title");
        }

        Transform _options;
        bool _argsDone;

        /// <summary>해금한 것이 있을 때만 보이는 선택지: 맵, 봇 난이도.</summary>
        void BuildOptions(Profile profile)
        {
            if (_options == null) _options = UiKit.Rect(_ui, "Options", 860, 370, 200, 200);
            UiKit.Clear(_options);
            var maps = profile.UnlockedMaps();
            if (maps.Count == 0 && !profile.HardBotUnlocked) return;
            UiKit.SpritePanel(_options, "Bg", 0, 0, 200, 60 + (maps.Count > 0 ? 34 * (maps.Count + 1) : 0) + (profile.HardBotUnlocked ? 40 : 0), "ui_panel");
            float y = 10;
            if (maps.Count > 0)
            {
                UiKit.Label(_options, "MapL", 10, y, 180, 20, "맵 (혼자 하기 1v1)", 12, TextAnchor.MiddleLeft, UiKit.Ink); y += 22;
                foreach (var name in new[] { "" }.Concat(maps))
                {
                    string n = name; bool sel = GameSession.MapName == n;
                    UiKit.SpriteButton(_options, "Map" + n, 10, y, 180, 30, n == "" ? "기본 (굽이 셋)" : n, () => { GameSession.MapName = n; BuildOptions(profile); }, sel ? "ui_button_green" : "ui_button_grey", 12); y += 34;
                }
            }
            if (profile.HardBotUnlocked)
            {
                UiKit.SpriteButton(_options, "Bot", 10, y + 4, 180, 30, GameSession.HardBot ? "봇: 고수 (해금)" : "봇: 보통", () => { GameSession.HardBot = !GameSession.HardBot; BuildOptions(profile); }, GameSession.HardBot ? "ui_button" : "ui_button_grey", 12);
            }
        }

        void ShowCodex()
        {
            OpenPopup("해금 도감 — 조건은 비밀. 여러 판에 걸쳐 저절로 열립니다");
            var profile = ProfileStore.Current;
            int i = 0;
            foreach (var u in Profile.Catalog)
            {
                bool open = profile.Has(u.Id);
                float x = 40 + (i % 2) * 420, y = 60 + (i / 2) * 92;
                UiKit.SpritePanel(_popup, "U" + i, x, y, 400, 84, open ? "ui_panel_dark" : "ui_slot");
                UiKit.Icon(_popup, "I" + i, x + 12, y + 26, 32, open ? "icon_check" : "icon_lock");
                string kind = u.Kind switch { UnlockKind.Title => "칭호", UnlockKind.Map => "맵", UnlockKind.Augment => "비밀 증강", _ => "봇" };
                UiKit.Label(_popup, "N" + i, x + 56, y + 8, 330, 22, open ? $"{u.Name}  [{kind}]" : $"???  [{kind}]", 15, TextAnchor.MiddleLeft, open ? UiKit.Gold : UiKit.Ink);
                UiKit.Label(_popup, "D" + i, x + 56, y + 32, 330, 46, open ? u.Reward : "힌트: " + u.Hint, 12, TextAnchor.UpperLeft, open ? Color.white : UiKit.InkSoft);
                i++;
            }
        }

        void OnDestroy() { if (_ui != null) Destroy(_ui.gameObject); }

        void Update()
        {
            _t += Time.deltaTime;
            if (_logo != null) _logo.anchoredPosition = new Vector2(320, -60 + Mathf.Sin(_t * 1.6f) * 6f);
            for (int i = 0; i < _parade.Count; i++)
            {
                var (rt, speed, a, b, phase) = _parade[i];
                var pos = rt.anchoredPosition;
                pos.x += speed * Time.deltaTime;
                if (pos.x > 1380) pos.x = -120;
                rt.anchoredPosition = pos;
                rt.GetComponent<Image>().sprite = Mathf.FloorToInt((_t + phase) / 0.2f) % 2 == 0 ? a : b;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && _popup.gameObject.activeSelf) _popup.gameObject.SetActive(false);
            if (!_argsDone) { _argsDone = true; foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-codex") ShowCodex(); }
        }

        void OpenPopup(string title)
        {
            UiKit.Clear(_popup);
            _popup.gameObject.SetActive(true);
            _popup.SetAsLastSibling();
            UiKit.Label(_popup, "Title", 0, 16, 900, 32, title, 20, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.SpriteButton(_popup, "Close", 390, 552, 120, 32, "닫기 (ESC)", () => _popup.gameObject.SetActive(false), "ui_button_grey", 12);
        }

        void ShowHowTo()
        {
            OpenPopup("게임 방법");
            string text =
                "■ 목표  8분 안에 상대 기지 체력을 0으로 만들거나, 끝났을 때 기지 체력이 더 많으면 승리.\n\n" +
                "■ 방어  왼쪽 [내 진영]의 경로 옆 빈 칸에 타워를 짓는다. 타워는 지나가는 상대 유닛을 쏘기만 하고 맞지 않는다.\n" +
                "   유닛은 구불구불한 경로를 따라 걷기만 하고, 살아서 끝까지 가면 기지 체력을 깎는다(누수). 굽이 안쪽 구석은 여러 구간을 동시에 본다.\n" +
                "   기본 웨이브는 24초마다 양쪽에 똑같이 온다.\n\n" +
                "■ 공격  [뽑기]로 유닛 카드를 뽑고(손패 8장, ×3 버튼으로 한 번에 셋), 카드를 눌러 상대 진영으로 보낸다. 보내면 팀 인컴이 오른다.\n" +
                "   뽑기 등급 확률은 내 레벨이 정한다(롤토체스처럼). 경험치는 수입마다 +2, 4골드로 +4 (E). 레벨 4부터 전설(맹수 왕·리치·고대 드래곤·타이탄).\n" +
                "   [돌격 강화]는 팀 공유: 레벨마다 우리 팀이 보내는 유닛 체력 +6%, 속도 +2% (25, 40, 55… 골드, 최대 6).\n" +
                "   상대가 보낸 유닛은 내 라인에 보이니, 오는 것을 보고 타워를 짓고 남는 골드로 보내자.\n\n" +
                "■ 경제  15초마다 팀 인컴이 팀원에게 똑같이 나뉘어 들어온다. 처치 1골드. 판매는 80% 환불.\n\n" +
                "■ 합성  같은 타워 3개 → ★2 (공격 ×2.2), ★2 3개 → ★3 (공격 ×5). 합성표의 두 타워 → 새 타워, 그 둘을 다시 합치면 3단계. 타워를 클릭해서 한다.\n" +
                "   시너지: 같은 계열(숲·불·기계) 3/5개, 같은 직업 타워를 옆에 붙이기. 5초 안에 같은 유닛 3마리를 보내면 무리 시너지.\n\n" +
                "■ 재미 요소  레벨 1·3·6·9에 닿으면 증강 3장 중 1장(20초 안에, 게임은 계속). 3:30·6:00에 이벤트 시간대(30초 전 예고). 비밀 미션은 시작 때 하나.\n\n" +
                "■ 온라인  한 명이 [방 만들기], 나머지는 그 주소로 [참가]. 호스트가 인원과 팀을 정하고 시작. 빈 자리는 봇.\n\n" +
                "■ 조작  타워 상점 클릭 또는 Q~O, 빈 칸 클릭 = 짓기, 타워 클릭 = 강화/판매/합치기/합성, 우클릭 = 판매,\n" +
                "   D = 뽑기, F = 3장 뽑기, 스페이스 = 정지, 1/2/3 = 배속, ESC = 메뉴.";
            UiKit.Label(_popup, "Body", 40, 56, 820, 490, text, 13, TextAnchor.UpperLeft, UiKit.Ink);
        }

        void ShowRecipes()
        {
            OpenPopup("합성표 — 재료 두 타워를 내 진영에 지은 뒤 한쪽을 클릭해 [합성]. 2단계 둘을 다시 합치면 3단계");
            int i = 0;
            foreach (var f in WaveCatalog.RecipeTowers)
            {
                float x = 40 + (i % 2) * 420, y = 56 + (i / 2) * 82;
                var a = WaveCatalog.Tower(f.RecipeA); var b = WaveCatalog.Tower(f.RecipeB);
                UiKit.SpritePanel(_popup, "Row" + i, x, y, 400, 76, f.Tier >= 3 ? "ui_panel_dark" : "ui_slot");
                var ink = f.Tier >= 3 ? Color.white : UiKit.Ink; var soft = f.Tier >= 3 ? new Color(0.85f, 0.85f, 0.9f) : UiKit.InkSoft;
                UiKit.IconSprite(_popup, "A" + i, x + 8, y + 4, 42, 42, Art.Tower(a.Id, 0));
                UiKit.Label(_popup, "An" + i, x - 4, y + 48, 66, 26, a.Name, 9, TextAnchor.UpperCenter, soft);
                UiKit.Label(_popup, "Plus" + i, x + 54, y + 10, 20, 30, "+", 20, TextAnchor.MiddleCenter, ink);
                UiKit.IconSprite(_popup, "B" + i, x + 78, y + 4, 42, 42, Art.Tower(b.Id, 0));
                UiKit.Label(_popup, "Bn" + i, x + 66, y + 48, 66, 26, b.Name, 9, TextAnchor.UpperCenter, soft);
                UiKit.Label(_popup, "Arrow" + i, x + 124, y + 10, 26, 30, "→", 20, TextAnchor.MiddleCenter, ink);
                UiKit.IconSprite(_popup, "R" + i, x + 154, y + 2, 48, 48, Art.Tower(f.Id, 0));
                UiKit.Label(_popup, "Rn" + i, x + 208, y + 4, 190, 20, (f.Tier >= 3 ? "★3단계  " : "") + f.Name, 13, TextAnchor.MiddleLeft, f.Tier >= 3 ? UiKit.Gold : ink);
                UiKit.Label(_popup, "Rd" + i, x + 208, y + 24, 188, 52, TowerInfo.Describe(f), 9, TextAnchor.UpperLeft, soft);
                i++;
            }
        }

        void ShowSound()
        {
            OpenPopup("소리 설정");
            Text sfxT = null, musT = null;
            UiKit.SpriteButton(_popup, "SfxDown", 300, 120, 50, 40, "−", () => { Sfx.SfxVolume -= 0.1f; sfxT.text = $"효과음 {Mathf.RoundToInt(Sfx.SfxVolume * 100)}%"; Sfx.Play("coin"); }, "ui_button_grey", 18);
            sfxT = UiKit.Label(_popup, "SfxT", 360, 120, 180, 40, $"효과음 {Mathf.RoundToInt(Sfx.SfxVolume * 100)}%", 16, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.SpriteButton(_popup, "SfxUp", 550, 120, 50, 40, "+", () => { Sfx.SfxVolume += 0.1f; sfxT.text = $"효과음 {Mathf.RoundToInt(Sfx.SfxVolume * 100)}%"; Sfx.Play("coin"); }, "ui_button_grey", 18);
            UiKit.SpriteButton(_popup, "MusDown", 300, 180, 50, 40, "−", () => { Sfx.MusicVolume -= 0.1f; musT.text = $"음악 {Mathf.RoundToInt(Sfx.MusicVolume * 100)}%"; }, "ui_button_grey", 18);
            musT = UiKit.Label(_popup, "MusT", 360, 180, 180, 40, $"음악 {Mathf.RoundToInt(Sfx.MusicVolume * 100)}%", 16, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.SpriteButton(_popup, "MusUp", 550, 180, 50, 40, "+", () => { Sfx.MusicVolume += 0.1f; musT.text = $"음악 {Mathf.RoundToInt(Sfx.MusicVolume * 100)}%"; }, "ui_button_grey", 18);
            UiKit.Label(_popup, "Hint", 0, 240, 900, 30, "설정은 자동 저장됩니다.", 12, TextAnchor.MiddleCenter, UiKit.InkSoft);
        }
    }
}
