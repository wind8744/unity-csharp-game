using System.Collections.Generic;
using System.IO;
using LaneBattle.Core.Wave;

namespace LaneBattle.Core.Net
{
    /// <summary>주고받는 메시지 종류. 락스텝은 명령만 교환한다 (문서 18절).</summary>
    public enum MsgType : byte
    {
        Hello = 1,      // 클라 → 호스트: 이름
        Welcome = 2,    // 호스트 → 클라: 네 플레이어 id
        Lobby = 3,      // 호스트 → 모두: 로비 상태 (인원수, 플레이어, 슬롯)
        Start = 4,      // 호스트 → 모두: 시드, 인원수, 슬롯, 턴 길이
        Commands = 5,   // 클라 → 호스트: 이번에 넣을 명령들
        Turn = 6,       // 호스트 → 모두: 턴 번호 + 그 턴의 모든 명령 (+ 가끔 해시)
        Ping = 7, Pong = 8,
        Leave = 9,      // 누가 나갔다 (호스트 → 모두: playerId)
        Chat = 10,
    }

    public sealed class LobbyInfo
    {
        public int PlayersPerTeam = 1;
        public List<(int id, string name)> Players = new List<(int, string)>();
        /// <summary>슬롯 = 팀 t 의 p 번 자리 → 인덱스 t * PlayersPerTeam + p. 값은 플레이어 id, 봇이면 -1.</summary>
        public int[] SlotPlayer = { 0, -1 };
        public int SlotCount => PlayersPerTeam * 2;
        public int SlotOf(int playerId) { for (int i = 0; i < SlotPlayer.Length; i++) if (SlotPlayer[i] == playerId) return i; return -1; }
        public (int team, int player) SlotPos(int slot) => (slot / PlayersPerTeam, slot % PlayersPerTeam);
        public string NameOf(int playerId) { foreach (var (id, n) in Players) if (id == playerId) return n; return "?"; }
    }

    public sealed class StartInfo
    {
        public ulong Seed;
        public int PlayersPerTeam;
        public int[] SlotPlayer;
        public int TurnTicks = 4;
        public int MySlot;      // 받는 쪽에서 채운다
    }

    public sealed class TurnData
    {
        public int Turn;
        public List<MatchCommand> Commands = new List<MatchCommand>();
        public string Hash;     // 이 턴을 적용하기 전 상태의 해시 (가끔만, 아니면 null)
        public int Tick;        // 호스트가 이 턴을 만들 때의 시뮬 틱 (검증용)
    }

    /// <summary>바이너리 직렬화. 프레임 = [길이 4바이트][종류 1바이트][내용].</summary>
    public static class Wire
    {
        public static byte[] Frame(MsgType type, byte[] payload)
        {
            payload ??= new byte[0];
            var buf = new byte[5 + payload.Length];
            int len = payload.Length + 1;
            buf[0] = (byte)len; buf[1] = (byte)(len >> 8); buf[2] = (byte)(len >> 16); buf[3] = (byte)(len >> 24);
            buf[4] = (byte)type;
            System.Buffer.BlockCopy(payload, 0, buf, 5, payload.Length);
            return buf;
        }

        public static byte[] String(string s) { using var ms = new MemoryStream(); using var w = new BinaryWriter(ms); w.Write(s ?? ""); return ms.ToArray(); }
        public static string ReadString(byte[] p) { using var r = new BinaryReader(new MemoryStream(p)); return r.ReadString(); }
        public static byte[] Int(int v) { using var ms = new MemoryStream(); using var w = new BinaryWriter(ms); w.Write(v); return ms.ToArray(); }
        public static int ReadInt(byte[] p) { using var r = new BinaryReader(new MemoryStream(p)); return r.ReadInt32(); }
        public static byte[] Long(long v) { using var ms = new MemoryStream(); using var w = new BinaryWriter(ms); w.Write(v); return ms.ToArray(); }
        public static long ReadLong(byte[] p) { using var r = new BinaryReader(new MemoryStream(p)); return r.ReadInt64(); }

        public static byte[] Lobby(LobbyInfo l)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(l.PlayersPerTeam);
            w.Write(l.Players.Count);
            foreach (var (id, name) in l.Players) { w.Write(id); w.Write(name ?? ""); }
            w.Write(l.SlotPlayer.Length);
            foreach (var s in l.SlotPlayer) w.Write(s);
            return ms.ToArray();
        }

        public static LobbyInfo ReadLobby(byte[] p)
        {
            using var r = new BinaryReader(new MemoryStream(p));
            var l = new LobbyInfo { PlayersPerTeam = r.ReadInt32() };
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { int id = r.ReadInt32(); string name = r.ReadString(); l.Players.Add((id, name)); }
            int m = r.ReadInt32();
            l.SlotPlayer = new int[m];
            for (int i = 0; i < m; i++) l.SlotPlayer[i] = r.ReadInt32();
            return l;
        }

        public static byte[] Start(StartInfo s)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(s.Seed); w.Write(s.PlayersPerTeam); w.Write(s.TurnTicks);
            w.Write(s.SlotPlayer.Length);
            foreach (var v in s.SlotPlayer) w.Write(v);
            return ms.ToArray();
        }

        public static StartInfo ReadStart(byte[] p)
        {
            using var r = new BinaryReader(new MemoryStream(p));
            var s = new StartInfo { Seed = r.ReadUInt64(), PlayersPerTeam = r.ReadInt32(), TurnTicks = r.ReadInt32() };
            int n = r.ReadInt32();
            s.SlotPlayer = new int[n];
            for (int i = 0; i < n; i++) s.SlotPlayer[i] = r.ReadInt32();
            return s;
        }

        public static void WriteCommand(BinaryWriter w, MatchCommand c)
        {
            w.Write((byte)c.Team); w.Write((byte)c.Player); w.Write((byte)c.Type); w.Write(c.A); w.Write(c.B); w.Write(c.C);
        }

        public static MatchCommand ReadCommand(BinaryReader r)
        {
            return new MatchCommand { Team = r.ReadByte(), Player = r.ReadByte(), Type = (CommandType)r.ReadByte(), A = r.ReadInt32(), B = r.ReadInt32(), C = r.ReadInt32() };
        }

        public static byte[] Commands(IList<MatchCommand> cmds)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(cmds.Count);
            foreach (var c in cmds) WriteCommand(w, c);
            return ms.ToArray();
        }

        public static List<MatchCommand> ReadCommands(byte[] p)
        {
            using var r = new BinaryReader(new MemoryStream(p));
            int n = r.ReadInt32();
            var list = new List<MatchCommand>(n);
            for (int i = 0; i < n; i++) list.Add(ReadCommand(r));
            return list;
        }

        public static byte[] Turn(TurnData t)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(t.Turn); w.Write(t.Tick);
            w.Write(t.Hash != null); if (t.Hash != null) w.Write(t.Hash);
            w.Write(t.Commands.Count);
            foreach (var c in t.Commands) WriteCommand(w, c);
            return ms.ToArray();
        }

        public static TurnData ReadTurn(byte[] p)
        {
            using var r = new BinaryReader(new MemoryStream(p));
            var t = new TurnData { Turn = r.ReadInt32(), Tick = r.ReadInt32() };
            if (r.ReadBoolean()) t.Hash = r.ReadString();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) t.Commands.Add(ReadCommand(r));
            return t;
        }
    }
}
