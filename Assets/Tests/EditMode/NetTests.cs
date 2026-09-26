using System.Collections.Generic;
using System.Threading;
using LaneBattle.Core.Net;
using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    /// <summary>온라인 락스텝: 호스트·클라가 명령만 주고받아도 시뮬이 같게 간다.</summary>
    public class NetTests
    {
        [Test]
        public void WireRoundTripsTurnsAndLobby()
        {
            var t = new TurnData { Turn = 7, Tick = 28, Hash = "abc" };
            t.Commands.Add(MatchCommand.Build(1, 0, 4, 2, 1));
            t.Commands.Add(MatchCommand.Fuse(0, 1, 9, 12));
            var back = Wire.ReadTurn(Wire.Turn(t));
            Assert.AreEqual(7, back.Turn); Assert.AreEqual("abc", back.Hash); Assert.AreEqual(2, back.Commands.Count);
            Assert.AreEqual(CommandType.Fuse, back.Commands[1].Type); Assert.AreEqual(12, back.Commands[1].B);
            var l = new LobbyInfo { PlayersPerTeam = 2, SlotPlayer = new[] { 0, 3, -1, 5 } };
            l.Players.Add((0, "호스트")); l.Players.Add((3, "친구"));
            var lb = Wire.ReadLobby(Wire.Lobby(l));
            Assert.AreEqual("친구", lb.NameOf(3)); Assert.AreEqual(3, lb.SlotOf(5)); Assert.AreEqual((1, 1), lb.SlotPos(3));
        }

        static (NetSession host, NetSession client) Connect(long clock = 0)
        {
            var ht = new LoopbackTransport(); var ct = new LoopbackTransport();
            var host = new NetSession(ht, true, "호스트", () => clock);
            var client = new NetSession(ct, false, "친구", () => clock);
            LoopbackTransport.Connect(ht, ct);
            client.Poll(); host.Poll(); client.Poll();
            return (host, client);
        }

        [Test]
        public void LobbyAssignsSlotsAndTeamsCanChange()
        {
            var (host, client) = Connect();
            Assert.AreEqual(1, client.MyPlayerId);
            Assert.AreEqual(1, host.Lobby.SlotPlayer[1], "1v1: 두 번째 사람은 상대 팀");
            host.SetPlayersPerTeam(2);
            client.Poll();
            Assert.AreEqual(4, client.Lobby.SlotPlayer.Length);
            Assert.AreEqual((0, 1), client.MyPos, "2v2: 두 번째 사람은 팀원");
            host.MoveToNextSlot(1);
            client.Poll();
            Assert.AreEqual((1, 0), client.MyPos);
        }

        [TestCase(1)] [TestCase(2)]
        public void HostAndClientStayInSyncWithBotsOnHostOnly(int perTeam)
        {
            var (host, client) = Connect();
            host.SetPlayersPerTeam(perTeam); client.Poll();
            StartInfo clientStart = null;
            client.MatchStarted += s => clientStart = s;
            host.StartMatch(42);
            client.Poll();
            Assert.IsNotNull(clientStart);
            Assert.AreEqual(42UL, clientStart.Seed);
            var hs = host.CreateSim(); var cs = client.CreateSim();
            var hostHuman = new SimpleBot { Aggression = 70 }; var clientHuman = new SimpleBot { Aggression = 40 };
            var (ht, hp) = host.MyPos; var (ct, cp) = client.MyPos;
            int guard = 0;
            while (!hs.IsOver && guard++ < 200000)
            {
                if (hs.Tick % 20 == 0) hostHuman.Decide(hs, ht, hp, host.LocalCommands);
                host.Poll(); host.TryStep();
                // 클라는 조금 늦게 따라온다 (턴이 와 있을 때만 진행)
                if (!cs.IsOver && cs.Tick % 20 == 0) clientHuman.Decide(cs, ct, cp, client.LocalCommands);
                client.Poll();
                client.TryStep();
            }
            while (!cs.IsOver && guard++ < 200000) { client.Poll(); if (!client.TryStep()) break; }
            Assert.IsTrue(hs.IsOver && cs.IsOver);
            Assert.AreEqual(hs.Hash(), cs.Hash());
            Assert.AreEqual(hs.Winner, cs.Winner);
            Assert.IsNull(client.DesyncInfo);
            Assert.Greater(cs.Player(ct, cp).Sent + cs.OwnLane(ct).Towers.Count, 0, "클라의 명령이 양쪽에 반영됨");
        }

        [Test]
        public void ClientCommandsAreRetaggedToItsSlot()
        {
            var (host, client) = Connect();
            host.StartMatch(1); client.Poll();
            var hs = host.CreateSim(); client.CreateSim();
            client.LocalCommands.Add(MatchCommand.Build(0, 0, 1, 0, 0)); // 팀 0 이라고 속여도
            client.Poll(); host.Poll();
            host.TryStep();
            Assert.IsNotNull(hs.OwnLane(1).TowerAt(0, 0), "호스트가 클라 슬롯(팀 1)으로 고쳐 적용");
            Assert.IsNull(hs.OwnLane(0).TowerAt(0, 0));
        }

        [Test]
        public void DisconnectedHumanBecomesBotOnHost()
        {
            var ht = new LoopbackTransport(); var ct = new LoopbackTransport();
            var host = new NetSession(ht, true, "h", () => 0); var client = new NetSession(ct, false, "c", () => 0);
            LoopbackTransport.Connect(ht, ct);
            client.Poll(); host.Poll(); client.Poll();
            host.StartMatch(3); client.Poll();
            var hs = host.CreateSim();
            // 연결 끊김 흉내
            var m = typeof(NetSession).GetMethod("OnPeerDisconnected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            m.Invoke(host, new object[] { 1 });
            for (int i = 0; i < 20 * 60; i++) { host.Poll(); host.TryStep(); }
            Assert.Greater(hs.OwnLane(1).Towers.Count, 0, "나간 사람 자리를 봇이 이어받아 타워를 짓는다");
        }

        [Test]
        public void TcpLoopbackDeliversFramesBothWays()
        {
            int port = 27300 + (int)(System.DateTime.Now.Ticks % 200);
            using var ht = TcpTransport.Host(port);
            using var ct = TcpTransport.Connect("127.0.0.1", port);
            var got = new List<(int, MsgType, string)>();
            ht.Received += (p, t, d) => got.Add((p, t, Wire.ReadString(d)));
            ct.Received += (p, t, d) => got.Add((p, t, Wire.ReadString(d)));
            int connected = 0; ht.PeerConnected += _ => connected++; ct.PeerConnected += _ => connected++;
            for (int i = 0; i < 50 && connected < 2; i++) { ht.Poll(); ct.Poll(); Thread.Sleep(20); }
            Assert.AreEqual(2, connected);
            ct.Send(0, MsgType.Hello, Wire.String("안녕"));
            ht.Broadcast(MsgType.Chat, Wire.String("어서와"));
            for (int i = 0; i < 50 && got.Count < 2; i++) { ht.Poll(); ct.Poll(); Thread.Sleep(20); }
            Assert.AreEqual(2, got.Count);
            Assert.IsTrue(got.Exists(g => g.Item2 == MsgType.Hello && g.Item3 == "안녕" && g.Item1 == 1));
            Assert.IsTrue(got.Exists(g => g.Item2 == MsgType.Chat && g.Item3 == "어서와" && g.Item1 == 0));
        }
    }
}
