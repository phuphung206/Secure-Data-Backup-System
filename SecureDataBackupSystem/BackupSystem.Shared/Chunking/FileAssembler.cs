using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BackupSystem.Shared.Models;

namespace BackupSystem.Shared.Chunking
{
    /// <summary>
    /// Module thực hiện ghép các chunk nhận được từ mạng thành file hoàn chỉnh (File Assembler).
    /// Đảm bảo tính nguyên tử (Atomicity), hỗ trợ kiểm tra tính toàn vẹn SHA-256 trước khi lưu chính thức.
    /// </summary>
    public sealed class FileAssembler : IDisposable
    {
        private readonly object _lock = new object();
        private readonly string _finalDestinationPath;
        private readonly string _tempAssemblingPath;
        private readonly string _expectedSha256;
        private readonly long _expectedTotalBytes;
        private readonly int _totalChunks;
        private readonly HashSet<int> _receivedChunks;
        private FileStream? _fileStream;
        private bool _isDisposed;

        public string TransferId { get; }
        public int TotalChunks => _totalChunks;
        public int ReceivedChunksCount
        {
            get
            {
                lock (_lock)
                {
                    return _receivedChunks.Count;
                }
            }
        }

        public bool IsComplete
        {
            get
            {
                lock (_lock)
                {
                    return _receivedChunks.Count >= _totalChunks;
                }
            }
        }

        public string TempAssemblingPath => _tempAssemblingPath;
        public string FinalDestinationPath => _finalDestinationPath;

        public FileAssembler(string transferId, string finalDestinationPath, long expectedTotalBytes, int totalChunks, string expectedSha256 = "")
        {
            if (string.IsNullOrWhiteSpace(transferId))
                throw new ArgumentNullException(nameof(transferId));
            if (string.IsNullOrWhiteSpace(finalDestinationPath))
                throw new ArgumentNullException(nameof(finalDestinationPath));
            if (totalChunks <= 0)
                totalChunks = 1;

            TransferId = transferId;
            _finalDestinationPath = Path.GetFullPath(finalDestinationPath);
            _expectedTotalBytes = expectedTotalBytes;
            _totalChunks = totalChunks;
            _expectedSha256 = expectedSha256?.Trim() ?? string.Empty;
            _receivedChunks = new HashSet<int>();

            string? dir = Path.GetDirectoryName(_finalDestinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _tempAssemblingPath = _finalDestinationPath + $".assembling.{transferId}.tmp";

            // Khởi tạo FileStream cho phép ghi ngẫu nhiên (Seek) theo vị trí chunk
            _fileStream = new FileStream(
                _tempAssemblingPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough);

            if (_expectedTotalBytes > 0)
            {
                _fileStream.SetLength(_expectedTotalBytes);
            }
        }

        /// <summary>
        /// Ghi một chunk vào file lắp ráp tại vị trí offset chính xác.
        /// </summary>
        public bool WriteChunk(FileChunk chunk)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            if (_isDisposed) throw new ObjectDisposedException(nameof(FileAssembler));

            lock (_lock)
            {
                if (_fileStream == null)
                    throw new InvalidOperationException("FileStream chưa được mở.");

                if (_receivedChunks.Contains(chunk.ChunkIndex))
                {
                    // Đã nhận chunk này trước đó (trùng lặp do truyền lại), bỏ qua ghi đè
                    return true;
                }

                if (chunk.Data != null && chunk.Data.Length > 0)
                {
                    _fileStream.Seek(chunk.Offset, SeekOrigin.Begin);
                    _fileStream.Write(chunk.Data, 0, chunk.Data.Length);
                }

                _receivedChunks.Add(chunk.ChunkIndex);
                return true;
            }
        }

        /// <summary>
        /// Hoàn tất lắp ráp: Kiểm tra đủ các chunk, xác thực tính toàn vẹn SHA-256 và hoán đổi nguyên tử sang file đích.
        /// </summary>
        public string FinalizeAndVerify(string expectedSha256Override = "")
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(FileAssembler));

            string effectiveExpectedHash = !string.IsNullOrWhiteSpace(expectedSha256Override)
                ? expectedSha256Override.Trim()
                : _expectedSha256;

            lock (_lock)
            {
                if (!IsComplete)
                {
                    throw new InvalidOperationException($"Chưa nhận đủ số chunk: Đã nhận {_receivedChunks.Count}/{_totalChunks} chunks.");
                }

                _fileStream?.Flush(true);
                _fileStream?.Dispose();
                _fileStream = null;

                // Tính toán mã SHA-256 của file vừa lắp ráp
                string actualSha256 = FileChunker.ComputeFileSha256(_tempAssemblingPath);

                if (!string.IsNullOrWhiteSpace(effectiveExpectedHash))
                {
                    if (!string.Equals(actualSha256, effectiveExpectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        // Toàn vẹn dữ liệu bị xâm phạm, hủy file tạm ngay lập tức
                        if (File.Exists(_tempAssemblingPath))
                        {
                            File.Delete(_tempAssemblingPath);
                        }

                        throw new InvalidDataException(
                            $"Xác thực toàn vẹn thất bại cho file '{Path.GetFileName(_finalDestinationPath)}'!\n" +
                            $"Mã kỳ vọng: {effectiveExpectedHash}\n" +
                            $"Mã thực tế: {actualSha256}");
                    }
                }

                // Di chuyển nguyên tử (Atomic Replace / Move) sang vị trí lưu trữ chính thức
                string? destDir = Path.GetDirectoryName(_finalDestinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(_finalDestinationPath))
                {
                    File.Delete(_finalDestinationPath);
                }

                File.Move(_tempAssemblingPath, _finalDestinationPath);

                return actualSha256;
            }
        }

        /// <summary>
        /// Hủy bỏ phiên lắp ráp và xóa dọn file tạm.
        /// </summary>
        public void Abort()
        {
            lock (_lock)
            {
                try
                {
                    _fileStream?.Dispose();
                    _fileStream = null;
                }
                catch { }

                try
                {
                    if (File.Exists(_tempAssemblingPath))
                    {
                        File.Delete(_tempAssemblingPath);
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            lock (_lock)
            {
                _fileStream?.Dispose();
                _fileStream = null;

                // Nếu chưa hoàn thành chuyển đổi mà dispose thì dọn dẹp file tạm
                if (File.Exists(_tempAssemblingPath))
                {
                    try { File.Delete(_tempAssemblingPath); } catch { }
                }
            }
        }
    }
}

