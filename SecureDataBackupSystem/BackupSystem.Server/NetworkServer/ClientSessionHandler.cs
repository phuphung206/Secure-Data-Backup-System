using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Server.DataAccess;
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
        private readonly BackupMetadataService? _metadata;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BackupInitPayload> _pendingTransfers =
            new System.Collections.Concurrent.ConcurrentDictionary<string, BackupInitPayload>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _openVersions =
            new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        private int? _userId;
        private string _username = string.Empty;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string FilePath, int ChunkSize)> _activeRestoreFiles =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (string FilePath, int ChunkSize)>(StringComparer.OrdinalIgnoreCase);
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

        public bool IsAuthenticated => _userId.HasValue;
        public string Username => _username;

        public ClientSessionHandler(TcpClient tcpClient, ServerStorageManager storageManager, BackupMetadataService? metadata = null)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            _metadata = metadata;
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

                case PacketType.AuthRequest:
                    await HandleAuthAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.BackupSessionStartRequest:
                    await HandleSessionStartAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.BackupSessionEndRequest:
                    await HandleSessionEndAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.VersionListRequest:
                    await HandleVersionListAsync(packet).ConfigureAwait(false);
                    break;

                case PacketType.VersionFilesRequest:
                    await HandleVersionFilesAsync(packet).ConfigureAwait(false);
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

                case PacketType.RestoreListRequest:
                    await HandleRestoreListAsync().ConfigureAwait(false);
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

            BackupInitResponsePayload resp;
            if (initPayload.VersionId > 0 &&
                (!_userId.HasValue ||
                 !_openVersions.TryGetValue(initPayload.VersionId, out string? openBackupId) ||
                 !string.Equals(openBackupId, initPayload.BackupId, StringComparison.Ordinal)))
            {
                resp = new BackupInitResponsePayload
                {
                    TransferId = initPayload.TransferId,
                    IsAccepted = false,
                    Message = $"Phiên bản #{initPayload.VersionId} chưa được mở trong phiên đăng nhập này."
                };
            }
            else
            {
                resp = _storageManager.InitTransfer(initPayload);
                if (resp.IsAccepted)
                    _pendingTransfers[initPayload.TransferId] = initPayload;
                else
                    Log($"[TỪ CHỐI] {resp.Message}");
            }

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

            if (_pendingTransfers.TryRemove(compPayload.TransferId, out var initInfo) &&
                resp.IsSuccess && initInfo.VersionId > 0 && _metadata != null && _userId.HasValue)
            {
                if (!_metadata.RecordUploadedFile(_userId.Value, initInfo, resp.StoredFilePath, resp.FinalSizeBytes, out string metaError))
                {
                    resp.IsSuccess = false;
                    resp.Message = "Đã lưu file nhưng không ghi được metadata phiên bản: " + metaError;
                }
            }

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

            RestoreInitResponsePayload resp;
            string? physicalPath = null;

            if (req.VersionId > 0)
            {
                // Khôi phục theo phiên bản: tra cứu metadata, chỉ cho phép phiên bản thuộc tài khoản đã đăng nhập
                Log($"[RESTORE INIT] Yêu cầu khôi phục '{req.RelativePath}' tại phiên bản #{req.VersionId}.");
                var meta = (_metadata != null && _userId.HasValue)
                    ? _metadata.ResolveFileForRestore(_userId.Value, req.VersionId, req.RelativePath)
                    : null;

                if (meta == null)
                {
                    resp = new RestoreInitResponsePayload
                    {
                        FileExists = false,
                        RelativePath = req.RelativePath,
                        Message = _userId.HasValue
                            ? $"Không tìm thấy '{req.RelativePath}' trong phiên bản #{req.VersionId}."
                            : "Cần đăng nhập để khôi phục theo phiên bản."
                    };
                }
                else
                {
                    resp = _storageManager.GetFileForRestoreByPhysicalPath(meta.ServerPhysicalPath, req.RelativePath, NetworkProtocolConstants.DefaultChunkSize);
                    resp.IsEncrypted = meta.IsEncrypted;
                    resp.IsCompressed = meta.IsCompressed;
                    resp.OriginalSha256 = meta.Sha256Hash;
                    if (resp.FileExists)
                        physicalPath = meta.ServerPhysicalPath;
                    _metadata!.LogRestore(_userId!.Value, req.VersionId, "SINGLE_FILE", req.RelativePath, resp.FileExists);
                }
            }
            else if (!CanAccessBackupId(req.BackupId))
            {
                resp = new RestoreInitResponsePayload
                {
                    FileExists = false,
                    RelativePath = req.RelativePath,
                    Message = _userId.HasValue
                        ? $"Bản backup '{req.BackupId}' không thuộc tài khoản hiện tại."
                        : "Cần đăng nhập để khôi phục dữ liệu từ Server."
                };
            }
            else
            {
                Log($"[RESTORE INIT] Yêu cầu khôi phục file '{req.RelativePath}' từ Backup '{req.BackupId}'.");
                resp = _storageManager.GetFileForRestore(req.BackupId, req.RelativePath, NetworkProtocolConstants.DefaultChunkSize);
                if (resp.FileExists)
                {
                    physicalPath = _storageManager.GetPhysicalFilePath(req.BackupId, req.RelativePath);
                    resp.IsEncrypted = req.RelativePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (resp.FileExists && physicalPath != null)
            {
                _activeRestoreFiles[resp.TransferId] = (physicalPath, resp.ChunkSize);
            }
            else
            {
                Log($"[RESTORE THẤT BẠI] {resp.Message}");
            }

            byte[] respBytes = PacketSerializationHelper.SerializeJson(resp);
            await SendPacketAsync(new NetworkPacket(PacketType.RestoreInitResponse, respBytes)).ConfigureAwait(false);
        }

        private async Task HandleRestoreChunkAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<RestoreChunkRequestPayload>(packet.Payload);
            if (req == null) return;

            if (!_activeRestoreFiles.TryGetValue(req.TransferId, out var fileInfo) || !File.Exists(fileInfo.FilePath))
            {
                await SendPacketAsync(NetworkPacket.CreateError($"Không tìm thấy phiên restore {req.TransferId}.")).ConfigureAwait(false);
                return;
            }

            int chunkSize = fileInfo.ChunkSize > 0 ? fileInfo.ChunkSize : NetworkProtocolConstants.DefaultChunkSize;
            long offset = (long)req.ChunkIndex * chunkSize;
            var file = new FileInfo(fileInfo.FilePath);
            int totalChunks = FileChunker.CalculateTotalChunks(file.Length, chunkSize);

            if (req.ChunkIndex < 0 || (req.ChunkIndex >= totalChunks && !(totalChunks == 0 && req.ChunkIndex == 0)))
            {
                await SendPacketAsync(NetworkPacket.CreateError($"Chỉ số chunk {req.ChunkIndex} không hợp lệ.")).ConfigureAwait(false);
                return;
            }

            byte[] buffer = new byte[chunkSize];
            int bytesRead;

            using (var fs = new FileStream(fileInfo.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                bytesRead = fs.Read(buffer, 0, chunkSize);
            }

            byte[] chunkData = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, chunkData, 0, bytesRead);

            var chunk = new FileChunk(req.TransferId, req.ChunkIndex, totalChunks, offset, chunkData);
            byte[] packed = PacketSerializationHelper.PackFileChunk(chunk);
            await SendPacketAsync(new NetworkPacket(PacketType.RestoreChunkData, packed)).ConfigureAwait(false);

            if (req.ChunkIndex + 1 >= totalChunks)
            {
                _activeRestoreFiles.TryRemove(req.TransferId, out _);
                Log($"[KHÔI PHỤC THÀNH CÔNG] Đã truyền trả file '{Path.GetFileName(fileInfo.FilePath)}' ({totalChunks} chunks, {TransferProgressReport.FormatBytes(file.Length)}) về Client {RemoteEndPoint}!");
            }
        }

        private async Task HandleRestoreListAsync()
        {
            // Chỉ liệt kê các bản backup thuộc tài khoản đã đăng nhập (không lộ dữ liệu của người dùng khác)
            var files = new System.Collections.Generic.List<RemoteBackupFileEntry>();
            if (_metadata != null && _userId.HasValue)
            {
                var owned = _metadata.GetOwnedBackupIds(_userId.Value);
                foreach (var entry in _storageManager.ListBackupFiles())
                {
                    if (owned.Contains(entry.BackupId))
                        files.Add(entry);
                }
            }
            var resp = new RestoreListResponsePayload { Files = files };
            byte[] respBytes = PacketSerializationHelper.SerializeJson(resp);
            Log($"[RESTORE LIST] Gửi danh sách {files.Count} file backup tới Client {RemoteEndPoint}.");
            await SendPacketAsync(new NetworkPacket(PacketType.RestoreListResponse, respBytes)).ConfigureAwait(false);
        }

        private async Task HandleAuthAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<AuthRequestPayload>(packet.Payload);
            var resp = new AuthResponsePayload();

            if (_metadata == null)
            {
                resp.Message = "Server chưa kết nối được CSDL người dùng.";
            }
            else if (req == null || string.IsNullOrWhiteSpace(req.Username))
            {
                resp.Message = "Thiếu tên đăng nhập.";
            }
            else
            {
                var user = _metadata.Authenticate(req.Username, req.Password ?? string.Empty, req.MachineName);
                if (user == null)
                {
                    resp.Message = "Sai tên đăng nhập hoặc mật khẩu.";
                    Log($"[ĐĂNG NHẬP THẤT BẠI] Tài khoản '{req.Username}'.");
                }
                else
                {
                    _userId = user.UserId;
                    _username = user.Username;
                    resp.IsSuccess = true;
                    resp.UserId = user.UserId;
                    resp.Username = user.Username;
                    resp.Message = $"Xin chào {user.Username}.";
                    Log($"[ĐĂNG NHẬP] Tài khoản '{user.Username}' (UserId={user.UserId}).");
                }
            }

            await SendJsonAsync(PacketType.AuthResponse, resp).ConfigureAwait(false);
        }

        private async Task HandleSessionStartAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<BackupSessionStartPayload>(packet.Payload);
            BackupSessionStartResponsePayload resp;

            if (_metadata == null || !_userId.HasValue)
            {
                resp = new BackupSessionStartResponsePayload { IsAccepted = false, Message = "Cần đăng nhập trước khi mở phiên bản backup." };
            }
            else if (req == null)
            {
                resp = new BackupSessionStartResponsePayload { IsAccepted = false, Message = "Dữ liệu BackupSessionStart không hợp lệ." };
            }
            else
            {
                resp = _metadata.StartSession(_userId.Value, req);
                if (resp.IsAccepted)
                {
                    _openVersions[resp.VersionId] = req.BackupId;
                    Log($"[PHIÊN BẢN] {resp.Message} (Thư mục: {req.RootClientPath}, Kích hoạt: {req.TriggerType}).");
                }
            }

            await SendJsonAsync(PacketType.BackupSessionStartResponse, resp).ConfigureAwait(false);
        }

        private async Task HandleSessionEndAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<BackupSessionEndPayload>(packet.Payload);
            BackupSessionEndResponsePayload resp;

            if (_metadata == null || !_userId.HasValue || req == null)
            {
                resp = new BackupSessionEndResponsePayload { IsSuccess = false, Message = "Cần đăng nhập và gửi dữ liệu hợp lệ." };
            }
            else if (!_openVersions.ContainsKey(req.VersionId))
            {
                resp = new BackupSessionEndResponsePayload { IsSuccess = false, VersionId = req.VersionId, Message = $"Phiên bản #{req.VersionId} không được mở trong phiên này." };
            }
            else
            {
                resp = _metadata.EndSession(_userId.Value, req);
                _openVersions.TryRemove(req.VersionId, out _);
                Log($"[PHIÊN BẢN] {resp.Message}");
            }

            await SendJsonAsync(PacketType.BackupSessionEndResponse, resp).ConfigureAwait(false);
        }

        private async Task HandleVersionListAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<VersionListRequestPayload>(packet.Payload) ?? new VersionListRequestPayload();
            var resp = new VersionListResponsePayload();

            if (_metadata == null || !_userId.HasValue)
            {
                resp.Message = "Cần đăng nhập để xem lịch sử phiên bản.";
            }
            else
            {
                resp.Versions = _metadata.ListVersions(_userId.Value, req.RootClientPath, req.IncludeFailed);
                resp.IsSuccess = true;
                resp.Message = $"Có {resp.Versions.Count} phiên bản.";
                Log($"[LỊCH SỬ PHIÊN BẢN] Gửi {resp.Versions.Count} phiên bản cho '{_username}'.");
            }

            await SendJsonAsync(PacketType.VersionListResponse, resp).ConfigureAwait(false);
        }

        private async Task HandleVersionFilesAsync(NetworkPacket packet)
        {
            var req = PacketSerializationHelper.DeserializeJson<VersionFilesRequestPayload>(packet.Payload);
            VersionFilesResponsePayload resp;

            if (_metadata == null || !_userId.HasValue || req == null)
                resp = new VersionFilesResponsePayload { IsSuccess = false, Message = "Cần đăng nhập để xem nội dung phiên bản." };
            else
                resp = _metadata.GetVersionFiles(_userId.Value, req.VersionId);

            await SendJsonAsync(PacketType.VersionFilesResponse, resp).ConfigureAwait(false);
        }

        private bool CanAccessBackupId(string backupId)
        {
            if (_metadata == null || !_userId.HasValue || string.IsNullOrWhiteSpace(backupId))
                return false;
            return _metadata.GetOwnedBackupIds(_userId.Value).Contains(backupId);
        }

        private Task SendJsonAsync<T>(PacketType type, T payload)
        {
            byte[] bytes = PacketSerializationHelper.SerializeJson(payload);
            return SendPacketAsync(new NetworkPacket(type, bytes));
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

            // Phiên bản còn mở khi Client ngắt kết nối = backup dở dang → đánh dấu FAILED để không bị dùng làm bản cha
            if (_metadata != null)
            {
                foreach (int versionId in _openVersions.Keys)
                {
                    try { _metadata.FailVersion(versionId); } catch { }
                }
                _openVersions.Clear();
            }

            foreach (string transferId in _pendingTransfers.Keys)
            {
                try { _storageManager.AbortTransfer(transferId); } catch { }
            }
            _pendingTransfers.Clear();

            try { _cts.Cancel(); } catch { }
            try { _networkStream.Dispose(); } catch { }
            try { _tcpClient.Close(); } catch { }
            try { _sendLock.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }
}
