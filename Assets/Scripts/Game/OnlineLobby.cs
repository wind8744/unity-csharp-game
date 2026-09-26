using System.Collections.Generic;
using LaneBattle.Core.Net;
using UnityEngine;
using UnityEngine.UI;

namespace LaneBattle.Game
{
    /// <summary>온라인 로비: 방 만들기(호스트) / 주소로 참가. 호스트가 인원수·팀 배치를 정하고 시작한다. 채팅 포함.</summary>
    public sealed class OnlineLobby : MonoBehaviour
    {
        public System.Action<NetSession> OnStartMatch;
        public System.Action OnBack;
        public NetSession Session { get; private set; }
        public bool HandedOver;     // 경기로 넘어가면 세션을 여기서 닫지 않는다

        Transform _ui, _panel, _lobbyArea;
        TextBox _name, _addr, _chat;
        Text _status;
        readonly List<string> _log = new List<string>();
        bool _dirty;

        void Awake()
        {
            _ui = UiKit.Rect(UiKit.Canvas, "OnlineUI", 0, 0, 1280, 720);
            var bg = UiKit.Rect(_ui, "Bg", 0, 0, 1280, 720).gameObject.AddComponent<Image>();
            bg.sprite = Art.Get("title_bg"); bg.color = new Color(0.7f, 0.7f, 0.75f);
            var bgRt = bg.rectTransform; bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = new Vector2(-40, -40); bgRt.offsetMax = new Vector2(40, 40);
            _panel = UiKit.SpritePanel(_ui, "Panel", 140, 40, 1000, 640, "ui_panel").transform;
            UiKit.Label(_panel, "Title", 0, 14, 1000, 30, "온라인 대전 — 같은 네트워크(LAN)나 포트포워딩한 주소로 직접 연결", 18, TextAnchor.MiddleCenter, UiKit.Ink);
            UiKit.Label(_panel, "NameL", 30, 60, 60, 30, "이름", 14, TextAnchor.MiddleLeft, UiKit.Ink);
            _name = TextBox.Create(_panel, "Name", 90, 60, 200, 30, GameSession.PlayerName == "나" ? "플레이어" + Random.Range(10, 99) : GameSession.PlayerName);
            UiKit.SpriteButton(_panel, "Host", 310, 58, 150, 34, "방 만들기", Host, "ui_button_green", 14);
            UiKit.Label(_panel, "AddrL", 490, 60, 60, 30, "주소", 14, TextAnchor.MiddleLeft, UiKit.Ink);
            _addr = TextBox.Create(_panel, "Addr", 550, 60, 240, 30, PlayerPrefs.GetString("lastAddr", "127.0.0.1"));
            UiKit.SpriteButton(_panel, "Join", 800, 58, 120, 34, "참가", Join, "ui_button_blue", 14);
            _status = UiKit.Label(_panel, "Status", 30, 96, 940, 22, "방을 만들면 주소가 보입니다. 친구는 그 주소를 넣고 [참가]. 포트 27015 (방화벽 허용 필요).", 12, TextAnchor.MiddleLeft, UiKit.InkSoft);
            _lobbyArea = UiKit.Rect(_panel, "Lobby", 30, 124, 940, 470);
            UiKit.SpriteButton(_panel, "Back", 30, 596, 120, 32, "돌아가기", () => { Leave(); OnBack?.Invoke(); }, "ui_button_grey", 13);
            Rebuild();
        }

        void OnDestroy()
        {
            if (!HandedOver) Session?.Dispose();
            if (_ui != null) Destroy(_ui.gameObject);
        }

        void Update()
        {
            Session?.Poll();
            if (_dirty) { _dirty = false; Rebuild(); }
            if (Session != null && !Session.Transport.IsOpen && !HandedOver) { Log("연결이 닫혔습니다"); Leave(); }
        }

        void Log(string line) { _log.Add(line); if (_log.Count > 8) _log.RemoveAt(0); _dirty = true; }

        void Host()
        {
            if (Session != null) return;
            try
            {
                var t = TcpTransport.Host(TcpTransport.DefaultPort);
                Attach(new NetSession(t, true, _name.Value));
                Log($"방을 만들었습니다. 내 주소: {string.Join(" / ", TcpTransport.LocalAddresses())}:{TcpTransport.DefaultPort}");
            }
            catch (System.Exception e) { _status.text = "방 만들기 실패: " + e.Message; }
        }

        void Join()
        {
            if (Session != null) return;
            string addr = _addr.Value.Trim(); int port = TcpTransport.DefaultPort;
            int colon = addr.LastIndexOf(':');
            if (colon > 0 && int.TryParse(addr.Substring(colon + 1), out int p)) { port = p; addr = addr.Substring(0, colon); }
            try
            {
                var t = TcpTransport.Connect(addr, port);
                PlayerPrefs.SetString("lastAddr", _addr.Value.Trim());
                Attach(new NetSession(t, false, _name.Value));
                Log($"{addr}:{port} 에 연결했습니다. 호스트가 시작하길 기다리는 중…");
            }
            catch (System.Exception e) { _status.text = "참가 실패: " + e.Message + " (주소·방화벽·호스트가 방을 만들었는지 확인)"; }
        }

        void Attach(NetSession s)
        {
            Session = s;
            GameSession.PlayerName = _name.Value;
            s.LobbyChanged += () => _dirty = true;
            s.Log += Log;
            s.Disconnected += reason => { Log(reason); Leave(); };
            s.MatchStarted += _ => { HandedOver = true; OnStartMatch?.Invoke(s); };
            _dirty = true;
        }

        void Leave()
        {
            if (Session == null) return;
            if (!HandedOver) Session.Dispose();
            Session = null;
            _dirty = true;
        }

        void Rebuild()
        {
            if (_lobbyArea == null) return;
            UiKit.Clear(_lobbyArea);
            var a = _lobbyArea;
            if (Session == null)
            {
                UiKit.Label(a, "Hint", 0, 10, 940, 120,
                    "■ 방식: 호스트가 턴마다 모두의 명령을 모아 보내고, 모두가 같은 시뮬레이션을 돌립니다 (락스텝). 상태를 보내지 않으니 데이터가 아주 작습니다.\n" +
                    "■ 같은 와이파이/공유기 안이면 호스트의 192.168.x.x 주소로 바로 됩니다. 인터넷 너머면 호스트 공유기에서 27015 포트를 열어야 합니다.\n" +
                    "■ 빈 자리는 호스트의 봇이 채웁니다. 중간에 나간 사람 자리도 봇이 이어받습니다.", 12, TextAnchor.UpperLeft, UiKit.Ink);
                ShowLog(a, 150);
                return;
            }
            var lobby = Session.Lobby;
            bool host = Session.IsHost;
            if (host)
            {
                UiKit.Label(a, "Addr", 0, 0, 940, 22, $"내 주소: {string.Join(" / ", TcpTransport.LocalAddresses())}  (포트 {TcpTransport.DefaultPort})", 14, TextAnchor.MiddleLeft, UiKit.Ink);
                UiKit.Label(a, "ModeL", 0, 30, 80, 30, "인원", 13, TextAnchor.MiddleLeft, UiKit.Ink);
                for (int n = 1; n <= 3; n++)
                {
                    int nn = n;
                    UiKit.SpriteButton(a, "Mode" + n, 60 + (n - 1) * 90, 28, 84, 32, $"{n} vs {n}", () => Session.SetPlayersPerTeam(nn), lobby.PlayersPerTeam == n ? "ui_button" : "ui_button_grey", 13);
                }
                UiKit.Label(a, "ModeHint", 340, 30, 600, 30, "사람 이름을 누르면 다음 빈 자리로 옮깁니다 (팀 바꾸기). 빈 자리는 봇.", 11, TextAnchor.MiddleLeft, UiKit.InkSoft);
            }
            else
            {
                UiKit.Label(a, "Wait", 0, 0, 940, 22, $"{lobby.PlayersPerTeam} vs {lobby.PlayersPerTeam} — 호스트가 시작하길 기다리는 중… (핑 {Mathf.Max(0, Session.PingMs)}ms)", 14, TextAnchor.MiddleLeft, UiKit.Ink);
            }
            for (int team = 0; team < 2; team++)
            {
                float x = team * 470;
                UiKit.SpritePanel(a, "Team" + team, x, 70, 460, 40 + lobby.PlayersPerTeam * 44, "ui_panel_dark");
                UiKit.Label(a, "TeamL" + team, x + 12, 76, 300, 24, team == 0 ? "팀 A (파랑)" : "팀 B (빨강)", 14, TextAnchor.MiddleLeft, team == 0 ? UiKit.Blue : UiKit.Red);
                for (int p = 0; p < lobby.PlayersPerTeam; p++)
                {
                    int slot = team * lobby.PlayersPerTeam + p;
                    int pid = slot < lobby.SlotPlayer.Length ? lobby.SlotPlayer[slot] : -1;
                    string label = pid < 0 ? "봇" : lobby.NameOf(pid) + (pid == Session.MyPlayerId ? " (나)" : "") + (pid == 0 ? " [호스트]" : "");
                    int capturedPid = pid;
                    var b = UiKit.SpriteButton(a, $"Slot{slot}", x + 12, 106 + p * 44, 436, 36, label, () => { if (host && capturedPid >= 0) Session.MoveToNextSlot(capturedPid); }, pid < 0 ? "ui_button_grey" : team == 0 ? "ui_button_blue" : "ui_button", 14);
                    b.interactable = host && pid >= 0;
                }
            }
            float y = 70 + 40 + lobby.PlayersPerTeam * 44 + 16;
            ShowLog(a, y);
            _chat = TextBox.Create(a, "Chat", 0, 430, 760, 30, "");
            _chat.OnSubmit = SendChat;
            UiKit.SpriteButton(a, "Send", 770, 428, 70, 34, "전송", SendChat, "ui_button_grey", 13);
            if (host) UiKit.SpriteButton(a, "Start", 850, 424, 90, 40, "시작!", () => Session.StartMatch((ulong)(System.DateTime.Now.Ticks % 1000000) + 1), "ui_button_green", 15);
            else UiKit.SpriteButton(a, "Leave", 850, 424, 90, 40, "나가기", Leave, "ui_button_grey", 13);
        }

        void ShowLog(Transform a, float y)
        {
            UiKit.SpritePanel(a, "LogBox", 0, y, 940, 420 - y, "ui_slot");
            UiKit.Label(a, "Log", 10, y + 6, 920, 420 - y - 12, string.Join("\n", _log), 12, TextAnchor.LowerLeft, UiKit.Ink);
        }

        void SendChat()
        {
            if (Session == null || _chat == null || string.IsNullOrWhiteSpace(_chat.Value)) return;
            Session.SendChat(_chat.Value.Trim());
            _chat.Value = ""; _chat.Refresh();
        }
    }
}
