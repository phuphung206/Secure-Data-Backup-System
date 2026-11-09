using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Server.StorageManager;
using BackupSystem.Shared.Chunking;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Server.NetworkServer
{
    /// <summary>
    /// Quản lý một phiên làm việc độc lập với từng Client kết nối (Client Session Handler).
    /// Xử lý đa luồng bất đồng bộ (Multithreaded Asynchronous I/O), điều phối các gói tin qua luồng TCP Socket an toàn.
    /// </summary>
    public sealed class ClientSessionHandler : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _networkStream;
        private readonly ServerStorageManager _storageManager;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _isDisposed;

        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public string RemoteEndPoint { get; }
        public string ClientId { get; private set; } = "Unknown";
        public DateTime ConnectedUtc { get; } = DateTime.UtcNow;

        // Sự kiện báo cáo tiến trình lên Server
        public event Action<ClientSessionHandler, string>? OnLog;
        public event Action<ClientSessionHandler, BackupInitPayload>? OnTransferStarted;
        public event Action<ClientSessionHandler, string, int, int>? OnChunkReceived;
        public event Action<ClientSessionHandler, string, bool, string>? OnTransferCompleted;

        public ClientSessionHandler(TcpClient tcpClient, ServerStorageManager storageManager)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            _networkStream = _tcpClient.GetStream();
            RemoteEndPoint = _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Vòng lặp nhận và điều phối gói tin chính của phiên kết nối.
        /// Chạy ngầm trên một Task riêng biệt mà không chiếm giữ tài nguyên luồng chính.
        /// </summary>
        public async Task RunAsync()
        {
            string shortSession = !string.IsNullOrEmpty(SessionId) ? SessionId.Substring(0, Math.Min(8, SessionId.Length)) : "";
            Log($"[KẾT NỐI] Client từ {RemoteEndPoint} đã kết nối (Session: {shortSession}).");

            try
            {
                while (!_cts.IsCancellationRequested && _tcpClient.Connected)
                {
                    var packet = await NetworkPacket.ReadFromStreamAsync(_networkStream, _cts.Token).ConfigureAwait(false);
                    if (packet == null)
                    {
                        Log($"[NGẮT KẾT NỐI] Client {RemoteEndPoint} đã chủ động đóng kết nối.");
                        break;
                    }

                    await ProcessPacketAsync(packet).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Phiên bị hủy bình thường
            }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                Log($"[NGẮT KẾT NỐI ĐỘT NGỘT] Client {RemoteEndPoint} bị rớt mạng ({ex.Message}).");
            }
            catch (Exception ex)
            {
                Log($"[LỖI PHIÊN] Xảy ra ngoại lệ xử lý cho {RemoteEndPoint}: {ex.Message}");
            }
            finally
            {
                Dispose();
            }
        }

        private async Task ProcessPacketAsync(NetworkPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.HandshakeRequest:
                    await HandleHandshakeAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.HeartbeatPing:
                    await SendPacketAsync(NetworkPacket.CreateHeartbeatPong()).ConfigureAwait(false);
                    break;

                case PacketType.BackupInitRequest:
                    await HandleBackupInitAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.FileChunkData:
                    await HandleFileChunkAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.BackupCompleteRequest:
                    await HandleBackupCompleteAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.RestoreInitRequest:
                    await HandleRestoreInitAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.RestoreChunkRequest:
                    await HandleRestoreChunkAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.Disconnect:
                    Log($"[DISCONNECT] Nhận gói tin Disconnect từ Client {RemoteEndPoint}.");
                    _cts.Cancel();
                    break;

                default:
                    Log($"[CẢNH BÁO] Nhận gói tin chưa được hỗ trợ: {packet.Type} (0x{(byte)packet.Type:X2}).");
                    break;
            }
        }

        #region Packet Handlers

        private async Task HandleHandshakeAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<HandshakeRequestPayload>(packet.Payload);
            ClientId = req?.ClientId ?? "UnknownClient";

            Log($"[HANDSHAKE] Nhận bắt tay từ máy trạm '{ClientId}' (Client v{req?.ClientAppVersion}).");

            var resp = new HandshakeResponsePayload
            {
                IsCompatible = req?.ProtocolVersion == NetworkProtocolConstants.ProtocolVersion,
                ServerProtocolVersion = NetworkProtocolConstants.ProtocolVersion,
                ServerName = "SecureDataBackupServer v1.0",
                Message = "Kết nối bắt tay thành công.",
                SessionId = SessionId
            };

            byte[] payload = PacketSerializationHelper.SerializeJson(resp);
            await SendPacketAsync(new NetworkPacket(PacketType.HandshakeResponse, payload)).ConfigureAwait(false);
        }

        private async Task HandleBackupInitAsync(NetworkPacket packet)
        {
            var initPayload = PacketSerializationHelper.DeserializeJson<BackupInitPayload>(packet.Payload);
            if (initPayload == null)
            {
                await SendPacketAsync(NetworkPacket.CreateError("Dữ liệu BackupInitPayload không hợp lệ.")).ConfigureAwait(false);
                return;
            }

            string shortSha = !string.IsNullOrEmpty(initPayload.ExpectedSha256) ? initPayload.ExpectedSha256.Substring(0, Math.Min(8, initPayload.ExpectedSha256.Length)) : "";
            Log($"[BACKUP INIT] Bắt đầu nhận file: '{initPayload.RelativePath}' ({TransferProgressReport.FormatBytes(initPayload.TotalBytes)}, {initPayload.TotalChunks} chunks, SHA-256: {shortSha}...).");
            OnTransferStarted?.Invoke(this, initPayload);

            var resp = _storageManager.InitTransfer(initPayload);
            byte[] respBytes = PacketSerializationHelper.SerializeJson(resp);
            await SendPacketAsync(new NetworkPacket(PacketType.BackupInitResponse, respBytes)).ConfigureAwait(false);
        }

        private async Task HandleFileChunkAsync(NetworkPacket packet)
        {
            FileChunk chunk = PacketSerializationHelper.UnpackFileChunk(packet.Payload);
            var ack = _storageManager.WriteChunk(chunk);

            OnChunkReceived?.Invoke(this, chunk.TransferId, chunk.ChunkIndex, chunk.TotalChunks);

            byte[] ackBytes = PacketSerializationHelper.SerializeJson(ack);
            await SendPacketAsync(new NetworkPacket(PacketType.FileChunkAck, ackBytes)).ConfigureAwait(false);
        }

        private async Task HandleBackupCompleteAsync(NetworkPacket packet)
        {
            var compPayload = PacketSerializationHelper.DeserializeJson<BackupCompletePayload>(packet.Payload);
            if (compPayload == null)
            {
                await SendPacketAsync(NetworkPacket.CreateError("Dữ liệu BackupCompletePayload không hợp lệ.")).ConfigureAwait(false);
                return;
            }

            string shortTid = !string.IsNullOrEmpty(compPayload.TransferId) ? compPayload.TransferId.Substring(0, Math.Min(8, compPayload.TransferId.Length)) : "";
            Log($"[BACKUP COMPLETE] Đang tiến hành kiểm tra toàn vẹn SHA-256 cho TransferId: {shortTid}...");
            var resp = _storageManager.CompleteTransfer(compPayload);

            OnTransferCompleted?.Invoke(this, compPayload.TransferId, resp.IsSuccess, resp.Message);

            if (resp.IsSuccess)
            {
                Log($"[THÀNH CÔNG] File đã được lưu an toàn: {resp.StoredFilePath} ({TransferProgressReport.FormatBytes(resp.FinalSizeBytes)})");
            }
            else
            {
                Log($"[THẤT BẠI] Lỗi ráp file: {resp.Message}");
            }

            byte[] respBytes = PacketSerializationHelper.SerializeJson(resp);
            await SendPacketAsync(new NetworkPacket(PacketType.BackupCompleteResponse, respBytes)).ConfigureAwait(false);
        }

        private async Task HandleRestoreInitAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<RestoreInitRequestPayload>(packet.Payload);
            if (req == null)
            {
                await SendPacketAsync(NetworkPacket.CreateError("Dữ liệu RestoreInitRequest không hợp lệ.")).ConfigureAwait(false);
                return;
            }

            Log($"[RESTORE INIT] Yêu cầu khôi phục file '{req.RelativePath}' từ Backup '{req.BackupId}'.");
            var resp = _storageManager.GetFileForRestore(req.BackupId, req.RelativePath, NetworkProtocolConstants.DefaultChunkSize);

            byte[] respBytes = PacketSerializationHelper.SerializeJson(resp);
            await SendPacketAsync(new NetworkPacket(PacketType.RestoreInitResponse, respBytes)).ConfigureAwait(false);
        }

        private async Task HandleRestoreChunkAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<RestoreChunkRequestPayload>(packet.Payload);
            if (req == null) return;

            // Xử lý gửi chunk file về cho Client
            // Trong ứng dụng Restore, Client yêu cầu từng chunk theo chỉ số
        }

        #endregion

        /// <summary>
        /// Gửi gói tin an toàn với Semaphore chống xung đột luồng khi nhiều thread cùng ghi vào NetworkStream.
        /// </summary>
        public async Task SendPacketAsync(NetworkPacket packet)
        {
            if (_isDisposed || !_tcpClient.Connected) return;

            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                await packet.WriteToStreamAsync(_networkStream, _cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke(this, message);
        }

        public void Disconnect()
        {
            _cts.Cancel();
            Dispose();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try { _cts.Cancel(); } catch { }
            try { _networkStream.Dispose(); } catch { }
            try { _tcpClient.Close(); } catch { }
            try { _sendLock.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }
}
