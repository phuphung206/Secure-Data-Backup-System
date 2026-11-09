using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Server.StorageManager;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Server.NetworkServer
{
    /// <summary>
    /// Máy chủ sao lưu đa luồng TCP (Multithreaded High-Performance TCP Backup Server).
    /// Hỗ trợ kết nối song song từ nhiều Client, xử lý cắt/ghép file bằng cơ chế Chunking và xác thực toàn vẹn bằng SHA-256.
    /// </summary>
    public sealed class TcpBackupServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, ClientSessionHandler> _sessions;
        private readonly ServerStorageManager _storageManager;
        private bool _isDisposed;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }
        public int ActiveClientsCount => _sessions.Count;
        public ServerStorageManager Storage => _storageManager;

        // Các sự kiện giám sát và hiển thị giao diện UI/Console
        public event Action<int>? OnServerStarted;
        public event Action? OnServerStopped;
        public event Action<string, string>? OnClientConnected;
        public event Action<string, string>? OnClientDisconnected;
        public event Action<BackupInitPayload>? OnTransferStarted;
        public event Action<string, int, int>? OnChunkReceived;
        public event Action<string, bool, string>? OnTransferCompleted;
        public event Action<string>? OnLog;

        public TcpBackupServer(string? storagePath = null)
        {
            _sessions = new ConcurrentDictionary<string, ClientSessionHandler>(StringComparer.OrdinalIgnoreCase);
            _storageManager = new ServerStorageManager(storagePath);
        }

        /// <summary>
        /// Khởi động máy chủ TCP trên cổng chỉ định (Mặc định: 5050).
        /// </summary>
        public void Start(int port = NetworkProtocolConstants.DefaultPort)
        {
            if (IsRunning)
                throw new InvalidOperationException("Máy chủ đã đang chạy.");

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(100); // Backlog 100 kết nối chờ

            IsRunning = true;
            Log($"[SERVER KHỞI ĐỘNG] Máy chủ TCP lắng nghe trên cổng {Port} (Lưu trữ: {_storageManager.StorageRoot}).");
            OnServerStarted?.Invoke(Port);

            // Bắt đầu vòng lặp tiếp nhận Client bất đồng bộ
            _ = Task.Run(() => AcceptClientsLoopAsync(_cts.Token));
        }

        private async Task AcceptClientsLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);

                    // Thiết lập tối ưu Socket cho truyền file lớn
                    client.NoDelay = true; // Vô hiệu hóa Nagle's algorithm để giảm độ trễ
                    client.ReceiveBufferSize = 64 * 1024;
                    client.SendBufferSize = 64 * 1024;

                    var session = new ClientSessionHandler(client, _storageManager);
                    _sessions[session.SessionId] = session;

                    // Đăng ký nhận sự kiện từ phiên
                    session.OnLog += (s, msg) => Log($"[{s.RemoteEndPoint}] {msg}");
                    session.OnTransferStarted += (s, info) => OnTransferStarted?.Invoke(info);
                    session.OnChunkReceived += (s, tid, cIdx, total) => OnChunkReceived?.Invoke(tid, cIdx, total);
                    session.OnTransferCompleted += (s, tid, ok, msg) => OnTransferCompleted?.Invoke(tid, ok, msg);

                    OnClientConnected?.Invoke(session.RemoteEndPoint, session.ClientId);

                    // Chạy phiên xử lý client trên Thread Pool độc lập
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await session.RunAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            _sessions.TryRemove(session.SessionId, out _);
                            OnClientDisconnected?.Invoke(session.RemoteEndPoint, session.ClientId);
                            session.Dispose();
                        }
                    }, cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    Log($"[LỖI TIẾP NHẬN KẾT NỐI] {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Dừng máy chủ TCP và giải phóng toàn bộ kết nối hiện hành.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener = null;

                foreach (var session in _sessions.Values)
                {
                    session.Disconnect();
                }
                _sessions.Clear();

                Log("[SERVER DỪNG] Máy chủ TCP đã dừng hoạt động thành công.");
                OnServerStopped?.Invoke();
            }
            catch (Exception ex)
            {
                Log($"[LỖI DỪNG SERVER] {ex.Message}");
            }
        }

        private void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            OnLog?.Invoke($"[{time}] {message}");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            _storageManager.Dispose();
            _cts?.Dispose();
        }
    }
}

