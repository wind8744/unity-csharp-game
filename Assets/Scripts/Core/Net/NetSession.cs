using System;
using System.Collections.Generic;
using LaneBattle.Core.Wave;

namespace LaneBattle.Core.Net
{
    /// <summary>
    /// 온라인 한 판: 로비(호스트가 슬롯을 정함) → 시작 → 락스텝.
    /// 락스텝: 호스트가 TurnTicks 틱마다 "턴"을 만든다 = 그 사이 들어온 모든 명령(자기 것, 클라 것, 봇 것). 턴을 모두에게 보내고 자기도 적용한다.
    /// 클라는 받은 턴이 있어야 그 틱을 진행한다 (없으면 기다림). 같은 시드·같은 턴 목록이면 시뮬이 같으므로 상태를 보낼 필요가 없다.
    /// 봇은 호스트만 돌리고 그 명령도 턴에 실린다. 사람이 나가면 그 자리는 호스트의 봇이 이어받는다.
    /// </summary>
    public sealed class NetSession : IDisposable
    {
        public INetTransport Transport { get; }
        public bool IsHost { get; }
        public int MyPlayerId { get; private set; } = -1;
        public string MyName { get; }
        public LobbyInfo Lobby { get; private set; } = new LobbyInfo();
        public StartInfo Start { get; private set; }
        public int TurnTicks = 4;
        public int HashEveryTurns = 25;
        public int PingMs { get; private set; } = -1;
        public string DesyncInfo { get; private set; }
        public bool InMatch => Start != null;
        public int MySlot => Start != null ? Start.MySlot : Lobby.SlotOf(MyPlayerId);
        public (int team, int player) MyPos => Lobby.SlotPos(MySlot);
        public readonly List<MatchCommand> LocalCommands = new List<MatchCommand>();
        public readonly List<string> Chat = new List<string>();

        public event Action LobbyChanged;
        public event Action<StartInfo> MatchStarted;
        public event Action<string> Disconnected;
        public event Action<string> Log;

        MatchSim _sim;
        IMatchAgent[] _bots;                                  // 슬롯별 봇 (호스트만)
        readonly List<MatchCommand> _fromClients = new List<MatchCommand>();
        readonly Dictionary<int, TurnData> _turns = new Dictionary<int, TurnData>();
        readonly Dictionary<int, int> _peerPlayer = new Dictionary<int, int>();  // peer → playerId (호스트: 같음)
        long _lastPingSent;
        bool _closed;
        readonly Func<long> _clock;

        public NetSession(INetTransport transport, bool isHost, string name, Func<long> clockMs = null)
        {
            Transport = transport; IsHost = isHost; MyName = name;
            _clock = clockMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            transport.PeerConnected += OnPeerConnected;
            transport.PeerDisconnected += OnPeerDisconnected;
            transport.Received += OnReceived;
            if (isHost)
            {
                MyPlayerId = 0;
                Lobby.Players.Add((0, name));
                Lobby.SlotPlayer = new[] { 0, -1 };
            }
        }

        public int QueuedTurns
        {
            get
            {
                if (_sim == null) return 0;
                int next = _sim.Tick / TurnTicks; int n = 0;
                foreach (var k in _turns.Keys) if (k >= next) n++;
                return n;
            }
        }

        // ─────────────────────────── 연결 ───────────────────────────

        void OnPeerConnected(int peer)
        {
            if (!IsHost) Transport.Send(0, MsgType.Hello, Wire.String(MyName));
        }

        void OnPeerDisconnected(int peer)
        {
            if (!IsHost) { Disconnected?.Invoke("호스트와 연결이 끊겼습니다"); return; }
            if (!_peerPlayer.TryGetValue(peer, out int pid)) return;
            _peerPlayer.Remove(peer);
            string name = Lobby.NameOf(pid);
            Lobby.Players.RemoveAll(p => p.id == pid);
            int slot = Lobby.SlotOf(pid);
            if (slot >= 0)
            {
                Lobby.SlotPlayer[slot] = -1;
                if (InMatch && _bots != null && _bots[slot] == null) _bots[slot] = new SimpleBot { Aggression = 60 };
            }
            Transport.Broadcast(MsgType.Leave, Wire.String(name));
            Transport.Broadcast(MsgType.Lobby, Wire.Lobby(Lobby));
            Log?.Invoke($"{name} 나감" + (InMatch ? " (봇이 이어받음)" : ""));
            LobbyChanged?.Invoke();
        }

        void OnReceived(int peer, MsgType type, byte[] p)
        {
            switch (type)
            {
                case MsgType.Hello when IsHost:
                {
                    string name = Wire.ReadString(p);
                    if (InMatch) { Transport.Send(peer, MsgType.Leave, Wire.String("경기 중")); return; }
                    int pid = peer;
                    _peerPlayer[peer] = pid;
                    Lobby.Players.Add((pid, name));
                    int slot = FirstEmptySlot();
                    if (slot >= 0) Lobby.SlotPlayer[slot] = pid;
                    Transport.Send(peer, MsgType.Welcome, Wire.Int(pid));
                    Transport.Broadcast(MsgType.Lobby, Wire.Lobby(Lobby));
                    Log?.Invoke($"{name} 들어옴");
                    LobbyChanged?.Invoke();
                    break;
                }
                case MsgType.Welcome when !IsHost:
                    MyPlayerId = Wire.ReadInt(p);
                    break;
                case MsgType.Lobby when !IsHost:
                    Lobby = Wire.ReadLobby(p);
                    LobbyChanged?.Invoke();
                    break;
                case MsgType.Start when !IsHost:
                {
                    var s = Wire.ReadStart(p);
                    s.MySlot = Array.IndexOf(s.SlotPlayer, MyPlayerId);
                    Lobby.PlayersPerTeam = s.PlayersPerTeam; Lobby.SlotPlayer = s.SlotPlayer;
                    TurnTicks = s.TurnTicks;
                    Start = s;
                    MatchStarted?.Invoke(s);
                    break;
                }
                case MsgType.Commands when IsHost:
                {
                    if (!_peerPlayer.TryGetValue(peer, out int pid)) return;
                    int slot = Lobby.SlotOf(pid);
                    if (slot < 0) return;
                    var (team, player) = Lobby.SlotPos(slot);
                    foreach (var c in Wire.ReadCommands(p)) { var cc = c; cc.Team = team; cc.Player = player; _fromClients.Add(cc); }
                    break;
                }
                case MsgType.Turn when !IsHost:
                {
                    var t = Wire.ReadTurn(p);
                    _turns[t.Turn] = t;
                    break;
                }
                case MsgType.Ping:
                    Transport.Send(peer, MsgType.Pong, p);
                    break;
                case MsgType.Pong:
                    PingMs = (int)(_clock() - Wire.ReadLong(p));
                    break;
                case MsgType.Leave:
                    Log?.Invoke(Wire.ReadString(p) + " 나감");
                    break;
                case MsgType.Chat:
                {
                    string line = Wire.ReadString(p);
                    Chat.Add(line);
                    if (IsHost) Transport.Broadcast(MsgType.Chat, p);
                    Log?.Invoke(line);
                    break;
                }
            }
        }

        int FirstEmptySlot() { for (int i = 0; i < Lobby.SlotPlayer.Length; i++) if (Lobby.SlotPlayer[i] < 0) return i; return -1; }

        // ─────────────────────────── 로비 (호스트) ───────────────────────────

        public void SetPlayersPerTeam(int n)
        {
            if (!IsHost || InMatch) return;
            n = Math.Max(1, Math.Min(3, n));
            var old = Lobby.SlotPlayer;
            Lobby.PlayersPerTeam = n;
            Lobby.SlotPlayer = new int[n * 2];
            for (int i = 0; i < Lobby.SlotPlayer.Length; i++) Lobby.SlotPlayer[i] = -1;
            var ids = new List<int>();
            foreach (var v in old) if (v >= 0) ids.Add(v);
            foreach (var (id, _) in Lobby.Players) if (!ids.Contains(id)) ids.Add(id);
            for (int i = 0; i < ids.Count && i < Lobby.SlotPlayer.Length; i++) Lobby.SlotPlayer[i] = ids[i];
            Transport.Broadcast(MsgType.Lobby, Wire.Lobby(Lobby));
            LobbyChanged?.Invoke();
        }

        /// <summary>플레이어를 다음 빈 슬롯으로 옮긴다 (팀 바꾸기).</summary>
        public void MoveToNextSlot(int playerId)
        {
            if (!IsHost || InMatch) return;
            int cur = Lobby.SlotOf(playerId);
            if (cur < 0) return;
            int n = Lobby.SlotPlayer.Length;
            for (int k = 1; k < n; k++)
            {
                int s = (cur + k) % n;
                if (Lobby.SlotPlayer[s] < 0) { Lobby.SlotPlayer[s] = playerId; Lobby.SlotPlayer[cur] = -1; break; }
            }
            Transport.Broadcast(MsgType.Lobby, Wire.Lobby(Lobby));
            LobbyChanged?.Invoke();
        }

        public void SendChat(string text)
        {
            string line = $"{MyName}: {text}";
            if (IsHost) { Chat.Add(line); Transport.Broadcast(MsgType.Chat, Wire.String(line)); Log?.Invoke(line); }
            else Transport.Send(0, MsgType.Chat, Wire.String(line));
        }

        public void StartMatch(ulong seed)
        {
            if (!IsHost || InMatch) return;
            var s = new StartInfo { Seed = seed, PlayersPerTeam = Lobby.PlayersPerTeam, SlotPlayer = (int[])Lobby.SlotPlayer.Clone(), TurnTicks = TurnTicks, MySlot = Lobby.SlotOf(0) };
            Start = s;
            Transport.Broadcast(MsgType.Start, Wire.Start(s));
            MatchStarted?.Invoke(s);
        }

        // ─────────────────────────── 경기 (락스텝) ───────────────────────────

        /// <summary>시작 정보로 시뮬을 만들고 붙인다. 호스트는 봇 자리에 봇을 둔다.</summary>
        public MatchSim CreateSim(int botAggression = 65)
        {
            var cfg = new MatchConfig { PlayersPerTeam = Start.PlayersPerTeam };
            _sim = new MatchSim(cfg, Start.Seed);
            if (IsHost)
            {
                _bots = new IMatchAgent[Start.SlotPlayer.Length];
                for (int i = 0; i < _bots.Length; i++)
                    if (Start.SlotPlayer[i] < 0) _bots[i] = new SimpleBot { Aggression = botAggression + (i % Start.PlayersPerTeam) * 5 };
            }
            return _sim;
        }

        public MatchSim Sim => _sim;

        /// <summary>매 프레임: 전송 계층 폴링, 클라는 쌓인 명령 전송, 핑.</summary>
        public void Poll()
        {
            if (_closed) return;
            Transport.Poll();
            if (!IsHost && LocalCommands.Count > 0 && InMatch)
            {
                Transport.Send(0, MsgType.Commands, Wire.Commands(LocalCommands));
                LocalCommands.Clear();
            }
            long now = _clock();
            if (now - _lastPingSent > 2000)
            {
                _lastPingSent = now;
                if (IsHost) Transport.Broadcast(MsgType.Ping, Wire.Long(now)); else Transport.Send(0, MsgType.Ping, Wire.Long(now));
            }
        }

        /// <summary>시뮬을 한 틱 진행한다. 턴 경계에서 호스트는 턴을 만들어 보내고, 클라는 턴이 와 있어야 한다. 진행했으면 true.</summary>
        public bool TryStep()
        {
            if (_sim == null || _sim.IsOver) return false;
            int tick = _sim.Tick;
            List<MatchCommand> cmds = null;
            if (tick % TurnTicks == 0)
            {
                int turn = tick / TurnTicks;
                if (IsHost)
                {
                    var t = new TurnData { Turn = turn, Tick = tick };
                    var (myTeam, myPlayer) = Lobby.SlotPos(Start.MySlot);
                    foreach (var c in LocalCommands) { var cc = c; cc.Team = myTeam; cc.Player = myPlayer; t.Commands.Add(cc); }
                    LocalCommands.Clear();
                    t.Commands.AddRange(_fromClients); _fromClients.Clear();
                    if (tick % _sim.Cfg.TicksPerSecond == 0)
                        for (int s = 0; s < _bots.Length; s++)
                            if (_bots[s] != null) { var (bt, bp) = Lobby.SlotPos(s); _bots[s].Decide(_sim, bt, bp, t.Commands); }
                    if (turn % HashEveryTurns == 0) t.Hash = _sim.Hash();
                    Transport.Broadcast(MsgType.Turn, Wire.Turn(t));
                    cmds = t.Commands;
                }
                else
                {
                    if (!_turns.TryGetValue(turn, out var t)) return false;
                    _turns.Remove(turn);
                    if (t.Hash != null && DesyncInfo == null)
                    {
                        string mine = _sim.Hash();
                        if (mine != t.Hash) { DesyncInfo = $"턴 {turn} 에서 상태가 어긋남"; Log?.Invoke(DesyncInfo); }
                    }
                    cmds = t.Commands;
                }
            }
            _sim.Step(cmds);
            return true;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            Transport.PeerConnected -= OnPeerConnected;
            Transport.PeerDisconnected -= OnPeerDisconnected;
            Transport.Received -= OnReceived;
            Transport.Dispose();
        }
    }
}
