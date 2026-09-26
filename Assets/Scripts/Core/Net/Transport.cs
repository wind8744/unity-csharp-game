using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LaneBattle.Core.Net
{
    /// <summary>
    /// 전송 계층. 상대(peer)마다 번호가 있고, 프레임 단위로 바이트를 주고받는다. Poll() 을 부르는 스레드에서 이벤트가 난다.
    /// 지금은 TCP 직결 (LAN·포트포워딩). 나중에 스팀 P2P 로 바꿀 때 이 인터페이스만 구현하면 된다.
    /// </summary>
    public interface INetTransport : IDisposable
    {
        event Action<int> PeerConnected;
        event Action<int> PeerDisconnected;
        event Action<int, MsgType, byte[]> Received;
        void Send(int peer, MsgType type, byte[] payload);
        void Broadcast(MsgType type, byte[] payload);
        void Poll();
        bool IsOpen { get; }
    }

    /// <summary>테스트용: 같은 프로세스 안에서 두 끝을 직접 잇는다. 호스트 쪽은 여러 클라를 가질 수 있다.</summary>
    public sealed class LoopbackTransport : INetTransport
    {
        public event Action<int> PeerConnected;
        public event Action<int> PeerDisconnected;
        public event Action<int, MsgType, byte[]> Received;
        readonly ConcurrentQueue<(int peer, MsgType type, byte[] data)> _inbox = new ConcurrentQueue<(int, MsgType, byte[])>();
        readonly Dictionary<int, LoopbackTransport> _peers = new Dictionary<int, LoopbackTransport>();
        readonly List<int> _pendingConnect = new List<int>();
        int _nextPeer = 1;
        public bool IsOpen { get; private set; } = true;

        /// <summary>호스트에 클라를 붙인다. 호스트는 새 peer 번호로, 클라는 peer 0 으로 서로를 본다.</summary>
        public static void Connect(LoopbackTransport host, LoopbackTransport client)
        {
            int id = host._nextPeer++;
            host._peers[id] = client; client._peers[0] = host;
            host._pendingConnect.Add(id); client._pendingConnect.Add(0);
            client._hostPeerIdOnHost = id;
        }
        int _hostPeerIdOnHost;

        public void Send(int peer, MsgType type, byte[] payload)
        {
            if (!_peers.TryGetValue(peer, out var other)) return;
            int from = other._peers.ContainsKey(0) && other._peers[0] == this ? 0 : _hostPeerIdOnHost;
            other._inbox.Enqueue((from, type, payload ?? new byte[0]));
        }

        public void Broadcast(MsgType type, byte[] payload) { foreach (var p in _peers.Keys) Send(p, type, payload); }

        public void Poll()
        {
            foreach (var p in _pendingConnect) PeerConnected?.Invoke(p);
            _pendingConnect.Clear();
            while (_inbox.TryDequeue(out var m)) Received?.Invoke(m.peer, m.type, m.data);
        }

        public void Dispose() { IsOpen = false; }
    }

    /// <summary>TCP: 호스트는 Listen, 클라는 Connect. 연결마다 읽기 스레드 하나, 받은 프레임은 큐에 쌓였다가 Poll() 에서 이벤트로 나간다.</summary>
    public sealed class TcpTransport : INetTransport
    {
        public event Action<int> PeerConnected;
        public event Action<int> PeerDisconnected;
        public event Action<int, MsgType, byte[]> Received;
        public bool IsOpen { get; private set; }
        public bool IsHost { get; private set; }
        public string LastError { get; private set; } = "";

        sealed class Peer { public int Id; public TcpClient Client; public NetworkStream Stream; public Thread Reader; public readonly object SendLock = new object(); public volatile bool Alive = true; }
        readonly ConcurrentQueue<(int peer, MsgType type, byte[] data)> _inbox = new ConcurrentQueue<(int, MsgType, byte[])>();
        readonly ConcurrentQueue<int> _connected = new ConcurrentQueue<int>();
        readonly ConcurrentQueue<int> _disconnected = new ConcurrentQueue<int>();
        readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();
        readonly object _peersLock = new object();
        TcpListener _listener;
        Thread _acceptThread;
        int _nextPeer = 1;
        volatile bool _closing;

        public const int DefaultPort = 27015;

        public static TcpTransport Host(int port = DefaultPort)
        {
            var t = new TcpTransport { IsHost = true };
            t._listener = new TcpListener(IPAddress.Any, port);
            t._listener.Start();
            t.IsOpen = true;
            t._acceptThread = new Thread(t.AcceptLoop) { IsBackground = true, Name = "net-accept" };
            t._acceptThread.Start();
            return t;
        }

        public static TcpTransport Connect(string host, int port = DefaultPort, int timeoutMs = 4000)
        {
            var t = new TcpTransport { IsHost = false };
            var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) { client.Close(); throw new SocketException((int)SocketError.TimedOut); }
            client.EndConnect(ar);
            client.NoDelay = true;
            t.IsOpen = true;
            t.AddPeer(0, client);
            t._connected.Enqueue(0);
            return t;
        }

        void AcceptLoop()
        {
            while (!_closing)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { break; }
                c.NoDelay = true;
                int id;
                lock (_peersLock) id = _nextPeer++;
                AddPeer(id, c);
                _connected.Enqueue(id);
            }
        }

        void AddPeer(int id, TcpClient client)
        {
            var p = new Peer { Id = id, Client = client, Stream = client.GetStream() };
            lock (_peersLock) _peers[id] = p;
            p.Reader = new Thread(() => ReadLoop(p)) { IsBackground = true, Name = "net-read-" + id };
            p.Reader.Start();
        }

        void ReadLoop(Peer p)
        {
            var header = new byte[4];
            try
            {
                while (!_closing && p.Alive)
                {
                    if (!ReadExact(p.Stream, header, 4)) break;
                    int len = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
                    if (len <= 0 || len > 4_000_000) break;
                    var body = new byte[len];
                    if (!ReadExact(p.Stream, body, len)) break;
                    var payload = new byte[len - 1];
                    Buffer.BlockCopy(body, 1, payload, 0, len - 1);
                    _inbox.Enqueue((p.Id, (MsgType)body[0], payload));
                }
            }
            catch (Exception e) { LastError = e.Message; }
            p.Alive = false;
            try { p.Client.Close(); } catch { }
            lock (_peersLock) _peers.Remove(p.Id);
            _disconnected.Enqueue(p.Id);
        }

        static bool ReadExact(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        public void Send(int peer, MsgType type, byte[] payload)
        {
            Peer p;
            lock (_peersLock) if (!_peers.TryGetValue(peer, out p)) return;
            var frame = Wire.Frame(type, payload);
            try { lock (p.SendLock) p.Stream.Write(frame, 0, frame.Length); }
            catch (Exception e) { LastError = e.Message; p.Alive = false; }
        }

        public void Broadcast(MsgType type, byte[] payload)
        {
            List<int> ids;
            lock (_peersLock) ids = new List<int>(_peers.Keys);
            foreach (var id in ids) Send(id, type, payload);
        }

        public void Poll()
        {
            while (_connected.TryDequeue(out int c)) PeerConnected?.Invoke(c);
            while (_inbox.TryDequeue(out var m)) Received?.Invoke(m.peer, m.type, m.data);
            while (_disconnected.TryDequeue(out int d)) PeerDisconnected?.Invoke(d);
        }

        public void Dispose()
        {
            _closing = true; IsOpen = false;
            try { _listener?.Stop(); } catch { }
            List<Peer> ps;
            lock (_peersLock) ps = new List<Peer>(_peers.Values);
            foreach (var p in ps) { p.Alive = false; try { p.Client.Close(); } catch { } }
        }

        /// <summary>이 기계의 LAN IPv4 주소들 (호스트가 화면에 보여줄 것).</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(a.Address.ToString());
                }
            }
            catch { }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }
    }
}
