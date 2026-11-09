using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BackupSystem.Shared.Chunking;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace BackupSystem.Client.NetworkClient
{
    /// <summary>
    /// Client giao tiếp mạng TCP Socket với Server (TCP Backup Client).
    /// Hỗ trợ truyền file lớn đa luồng, cơ chế Chunking, kiểm tra tính toàn vẹn SHA-256 và báo cáo tiến trình theo thời gian thực.
    /// </summary>
    public sealed class TcpBackupClient : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _receiveLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public string Host { get; private set; } = "127.0.0.1";
        public int Port { get; private set; } = NetworkProtocolConstants.DefaultPort;
        public string SessionId { get; private set; } = string.Empty;

        public event Action<string>? OnLog;

        public TcpBackupClient()
        {
        }

        /// <summary>
        /// Thiết lập kết nối TCP Socket tới Server với thời gian chờ (Timeout).
        /// </summary>
        public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = NetworkProtocolConstants.DefaultPort, int timeoutMs = NetworkProtocolConstants.DefaultTimeoutMs, CancellationToken cancellationToken = default)
        {
            Disconnect();

            Host = host;
            Port = port;

            Log($"[KẾT NỐI] Đang thiết lập kết nối tới {Host}:{Port}...");

            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true;
            _tcpClient.ReceiveBufferSize = 64 * 1024;
            _tcpClient.SendBufferSize = 64 * 1024;

            using (var timeoutCts = new CancellationTokenSource(timeoutMs))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
#if NET5_0_OR_GREATER
                    await _tcpClient.ConnectAsync(Host, Port, linkedCts.Token).ConfigureAwait(false);
#else
                    var connectTask = _tcpClient.ConnectAsync(Host, Port);
                    var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, linkedCts.Token)).ConfigureAwait(false);
                    if (completedTask != connectTask)
                    {
                        throw new TimeoutException($"Kết nối tới {Host}:{Port} quá thời gian chờ {timeoutMs}ms.");
                    }
                    await connectTask.ConfigureAwait(false);
#endif
                    _networkStream = _tcpClient.GetStream();
                    Log($"[KẾT NỐI] Kết nối Socket TCP thành công tới {Host}:{Port}.");

                    // Thực hiện bắt tay (Handshake) ngay sau khi nối mạng
                    bool handshakeOk = await HandshakeAsync(linkedCts.Token).ConfigureAwait(false);
                    return handshakeOk;
                }
                catch (Exception ex)
                {
                    Log($"[LỖI KẾT NỐI] Không thể kết nối tới {Host}:{Port}: {ex.Message}");
                    Disconnect();
                    return false;
                }
            }
        }

        private async Task<bool> HandshakeAsync(CancellationToken cancellationToken)
        {
            var req = new HandshakeRequestPayload
            {
                ProtocolVersion = NetworkProtocolConstants.ProtocolVersion,
                ClientId = Environment.MachineName,
                ClientAppVersion = "1.0.0"
            };

            byte[] reqBytes = PacketSerializationHelper.SerializeJson(req);
            await SendPacketAsync(new NetworkPacket(PacketType.HandshakeRequest, reqBytes), cancellationToken).ConfigureAwait(false);

            var respPacket = await ReceivePacketAsync(cancellationToken).ConfigureAwait(false);
            if (respPacket == null || respPacket.Type != PacketType.HandshakeResponse)
            {
                Log("[LỖI HANDSHAKE] Server không phản hồi gói tin Handshake hợp lệ.");
                return false;
            }

            var resp = PacketSerializationHelper.DeserializeJson<HandshakeResponsePayload>(respPacket.Payload);
            if (resp == null || !resp.IsCompatible)
            {
                Log($"[LỖI PHIÊN BẢN] Server không tương thích: {resp?.Message}");
                return false;
            }

            SessionId = resp.SessionId;
            string shortSession = !string.IsNullOrEmpty(SessionId) ? SessionId.Substring(0, Math.Min(8, SessionId.Length)) : "";
            Log($"[HANDSHAKE THÀNH CÔNG] Máy chủ: {resp.ServerName} (Phiên: {shortSession}).");
            return true;
        }

        /// <summary>
        /// Truyền một file lớn lên Server qua TCP Socket sử dụng cơ chế Chunking và báo cáo tiến trình theo thời gian thực.
        /// </summary>
        public async Task<bool> UploadFileAsync(
            string filePath,
            string relativePath,
            string backupId,
            string sourceKey = "",
            int chunkSize = NetworkProtocolConstants.DefaultChunkSize,
            IProgress<TransferProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _networkStream == null)
                throw new InvalidOperationException("Chưa kết nối tới Backup Server.");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File nguồn không tồn tại.", filePath);

            var fileInfo = new FileInfo(filePath);
            long totalBytes = fileInfo.Length;
            int totalChunks = FileChunker.CalculateTotalChunks(totalBytes, chunkSize);
            string fileName = Path.GetFileName(filePath);

            Log($"[BẮT ĐẦU TRUYỀN] File: '{fileName}' ({TransferProgressReport.FormatBytes(totalBytes)}), Chia thành {totalChunks} chunks.");

            // Báo cáo trạng thái chuẩn bị và tính SHA-256
            progress?.Report(new TransferProgressReport
            {
                FileName = fileName,
                RelativePath = relativePath,
                TotalBytes = totalBytes,
                TotalChunks = totalChunks,
                ProgressPercentage = 0,
                StatusMessage = "Đang tính toán mã băm SHA-256 trước khi gửi..."
            });

            string sha256 = await Task.Run(() => FileChunker.ComputeFileSha256(filePath), cancellationToken).ConfigureAwait(false);

            string transferId = Guid.NewGuid().ToString("N");
            var initPayload = new BackupInitPayload
            {
                TransferId = transferId,
                SourceKey = sourceKey,
                BackupId = backupId,
                RelativePath = relativePath,
                TotalBytes = totalBytes,
                ChunkSize = chunkSize,
                TotalChunks = totalChunks,
                ExpectedSha256 = sha256
            };

            // 1. Gửi gói tin khởi tạo BackupInitRequest
            byte[] initBytes = PacketSerializationHelper.SerializeJson(initPayload);
            await SendPacketAsync(new NetworkPacket(PacketType.BackupInitRequest, initBytes), cancellationToken).ConfigureAwait(false);

            // 2. Chờ Server phản hồi BackupInitResponse
            var initRespPacket = await ReceivePacketAsync(cancellationToken).ConfigureAwait(false);
            if (initRespPacket == null || initRespPacket.Type != PacketType.BackupInitResponse)
            {
                throw new InvalidOperationException("Server không chấp nhận khởi tạo phiên sao lưu.");
            }

            var initResp = PacketSerializationHelper.DeserializeJson<BackupInitResponsePayload>(initRespPacket.Payload);
            if (initResp == null || !initResp.IsAccepted)
            {
                throw new InvalidOperationException($"Server từ chối phiên sao lưu: {initResp?.Message}");
            }

            int startChunkIndex = initResp.ResumeFromChunkIndex;
            if (startChunkIndex > 0)
            {
                Log($"[TIẾP TỤC TRUYỀN] Server đã có {startChunkIndex} chunks. Tiếp tục từ chunk {startChunkIndex}.");
            }

            // 3. Cắt và truyền từng Chunk qua mạng
            long bytesSent = 0;
            var stopwatch = Stopwatch.StartNew();
            long lastReportBytes = 0;
            var speedStopwatch = Stopwatch.StartNew();

            foreach (var chunk in FileChunker.SliceFile(filePath, transferId, chunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (chunk.ChunkIndex < startChunkIndex)
                {
                    bytesSent += chunk.Length;
                    continue; // Bỏ qua chunk đã được Server ghi nhận
                }

                // Đóng gói nhị phân tối ưu không Base64
                byte[] packedChunk = PacketSerializationHelper.PackFileChunk(chunk);
                await SendPacketAsync(new NetworkPacket(PacketType.FileChunkData, packedChunk), cancellationToken).ConfigureAwait(false);

                // Chờ Server gửi xác nhận FileChunkAck
                var ackPacket = await ReceivePacketAsync(cancellationToken).ConfigureAwait(false);
                if (ackPacket == null || ackPacket.Type != PacketType.FileChunkAck)
                {
                    throw new IOException($"Không nhận được ACK từ Server cho chunk {chunk.ChunkIndex}.");
                }

                var ack = PacketSerializationHelper.DeserializeJson<FileChunkAckPayload>(ackPacket.Payload);
                if (ack == null || !ack.IsSuccess)
                {
                    throw new IOException($"Server báo lỗi khi nhận chunk {chunk.ChunkIndex}: {ack?.Message}");
                }

                bytesSent += chunk.Length;

                // Tính toán vận tốc truyền tải thực tế (Bytes/s)
                double speedElapsedSec = speedStopwatch.Elapsed.TotalSeconds;
                double speed = 0;
                if (speedElapsedSec >= 0.2 || chunk.IsLastChunk) // Cập nhật tốc độ mỗi 200ms
                {
                    long deltaBytes = bytesSent - lastReportBytes;
                    speed = deltaBytes / Math.Max(0.001, speedElapsedSec);
                    lastReportBytes = bytesSent;
                    speedStopwatch.Restart();
                }

                int percent = totalBytes > 0 ? (int)((bytesSent * 100) / totalBytes) : 100;
                double remainingBytes = totalBytes - bytesSent;
                double etaSeconds = speed > 0 ? remainingBytes / speed : 0;

                // Báo cáo tiến trình ra ngoài cho BackgroundWorker / UI
                progress?.Report(new TransferProgressReport
                {
                    TransferId = transferId,
                    FileName = fileName,
                    RelativePath = relativePath,
                    CurrentChunk = chunk.ChunkIndex + 1,
                    TotalChunks = totalChunks,
                    BytesTransferred = bytesSent,
                    TotalBytes = totalBytes,
                    ProgressPercentage = Math.Min(99, percent), // Giữ 99% cho đến khi Server xác thực SHA-256 xong
                    TransferSpeedBytesPerSec = speed,
                    EstimatedRemainingSeconds = etaSeconds,
                    StatusMessage = $"Đang gửi gói tin {chunk.ChunkIndex + 1}/{totalChunks} ({percent}%)..."
                });
            }

            stopwatch.Stop();
            Log($"[HOÀN TẤT TRUYỀN CHUNKS] Đã gửi đủ {totalChunks} chunks ({TransferProgressReport.FormatBytes(bytesSent)}). Đang chờ Server ráp và xác thực SHA-256...");

            progress?.Report(new TransferProgressReport
            {
                TransferId = transferId,
                FileName = fileName,
                RelativePath = relativePath,
                CurrentChunk = totalChunks,
                TotalChunks = totalChunks,
                BytesTransferred = totalBytes,
                TotalBytes = totalBytes,
                ProgressPercentage = 99,
                StatusMessage = "Server đang kiểm tra tính toàn vẹn SHA-256..."
            });

            // 4. Gửi BackupCompleteRequest yêu cầu Server ráp và xác thực
            var completePayload = new BackupCompletePayload
            {
                TransferId = transferId,
                ExpectedFullSha256 = sha256,
                ExpectedTotalBytes = totalBytes
            };

            byte[] compBytes = PacketSerializationHelper.SerializeJson(completePayload);
            await SendPacketAsync(new NetworkPacket(PacketType.BackupCompleteRequest, compBytes), cancellationToken).ConfigureAwait(false);

            // 5. Chờ kết quả xác thực từ Server
            var compRespPacket = await ReceivePacketAsync(cancellationToken).ConfigureAwait(false);
            if (compRespPacket == null || compRespPacket.Type != PacketType.BackupCompleteResponse)
            {
                throw new InvalidOperationException("Không nhận được kết quả xác thực hoàn tất từ Server.");
            }

            var compResp = PacketSerializationHelper.DeserializeJson<BackupCompleteResponsePayload>(compRespPacket.Payload);
            if (compResp == null || !compResp.IsSuccess)
            {
                throw new InvalidDataException($"Server xác thực toàn vẹn thất bại: {compResp?.Message}");
            }

            string shortSha = !string.IsNullOrEmpty(compResp.ServerSha256) ? compResp.ServerSha256.Substring(0, Math.Min(8, compResp.ServerSha256.Length)) : "";
            Log($"[THÀNH CÔNG] File '{fileName}' đã lưu trữ an toàn trên Server! (Mã SHA-256 trùng khớp: {shortSha}...).");

            progress?.Report(new TransferProgressReport
            {
                TransferId = transferId,
                FileName = fileName,
                RelativePath = relativePath,
                CurrentChunk = totalChunks,
                TotalChunks = totalChunks,
                BytesTransferred = totalBytes,
                TotalBytes = totalBytes,
                ProgressPercentage = 100,
                IsCompleted = true,
                StatusMessage = "Sao lưu qua mạng thành công 100%!"
            });

            return true;
        }

        /// <summary>
        /// Gửi gói tin an toàn luồng qua NetworkStream.
        /// </summary>
        public async Task SendPacketAsync(NetworkPacket packet, CancellationToken cancellationToken = default)
        {
            if (_networkStream == null || !IsConnected)
                throw new InvalidOperationException("Chưa kết nối tới Server.");

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await packet.WriteToStreamAsync(_networkStream, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Nhận gói tin an toàn luồng từ NetworkStream.
        /// </summary>
        public async Task<NetworkPacket?> ReceivePacketAsync(CancellationToken cancellationToken = default)
        {
            if (_networkStream == null || !IsConnected)
                throw new InvalidOperationException("Chưa kết nối tới Server.");

            await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await NetworkPacket.ReadFromStreamAsync(_networkStream, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _receiveLock.Release();
            }
        }

        public void Disconnect()
        {
            try { _networkStream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _networkStream = null;
            _tcpClient = null;
            SessionId = string.Empty;
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

            Disconnect();
            _sendLock.Dispose();
            _receiveLock.Dispose();
        }
    }
}
