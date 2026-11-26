using System;
using System.IO;
using System.Windows.Forms;
using BackupSystem.Client.NetworkClient;
using BackupSystem.Shared.Models;
using BackupSystem.Shared.Network;

namespace SecureDataBackupSystem
{
    public partial class FormNetworkBackup : Form
    {
        private readonly NetworkBackupWorker _worker;
        private NetworkBackupWorkerArgs _lastArgs;
        private readonly string _historyPath = BackupPaths.GetHistoryFilePath();

        public FormNetworkBackup()
        {
            InitializeComponent();

            cboChunkSize.SelectedIndex = 1; // 64 KB (Khuyến nghị)

            _worker = new NetworkBackupWorker();
            _worker.TransferProgressUpdated += Worker_TransferProgressUpdated;
            _worker.TransferCompleted += Worker_TransferCompleted;

            // Kéo thả file/folder vào ô đường dẫn
            txtSourcePath.AllowDrop = true;
            txtSourcePath.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            txtSourcePath.DragDrop += (s, e) =>
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    txtSourcePath.Text = files[0];
                }
            };
        }

        private async void btnPingServer_Click(object sender, EventArgs e)
        {
            string host = txtServerIp.Text.Trim();
            int port = (int)numPort.Value;

            btnPingServer.Enabled = false;
            Log($"[PING] Đang kiểm tra kết nối tới {host}:{port}...");

            try
            {
                using (var testClient = new TcpBackupClient())
                {
                    bool ok = await testClient.ConnectAsync(host, port, 3000);
                    if (ok)
                    {
                        Log($"[THÀNH CÔNG] Máy chủ tại {host}:{port} phản hồi tốt!");
                        MessageBox.Show($"Kết nối thành công tới Server {host}:{port}!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        Log($"[THẤT BẠI] Không thể kết nối tới {host}:{port}.");
                        MessageBox.Show($"Không thể kết nối tới máy chủ {host}:{port}.\nVui lòng kiểm tra Server đã bật chưa.", "Lỗi kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[LỖI] {ex.Message}");
                MessageBox.Show($"Lỗi kết nối: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnPingServer.Enabled = true;
            }
        }

        private void btnBrowseFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Chọn file cần sao lưu qua mạng";
                ofd.Filter = "Tất cả các file (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtSourcePath.Text = ofd.FileName;
                }
            }
        }

        private void btnBrowseFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Chọn thư mục cần sao lưu qua mạng";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtSourcePath.Text = fbd.SelectedPath;
                }
            }
        }

        private void chkEncrypt_CheckedChanged(object sender, EventArgs e)
        {
            txtPassword.Enabled = chkEncrypt.Checked;
            if (chkEncrypt.Checked)
            {
                txtPassword.Focus();
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            string source = txtSourcePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(source))
            {
                MessageBox.Show("Vui lòng chọn file hoặc thư mục cần sao lưu!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!File.Exists(source) && !Directory.Exists(source))
            {
                MessageBox.Show("Đường dẫn nguồn không tồn tại trên máy tính!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Kiểm tra mật khẩu nếu chọn mã hóa AES-256
            if (chkEncrypt.Checked && string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                MessageBox.Show("Vui lòng nhập mật khẩu để bảo vệ và mã hóa dữ liệu AES-256!", "Yêu cầu mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPassword.Focus();
                return;
            }

            string host = txtServerIp.Text.Trim();
            int port = (int)numPort.Value;

            int chunkSize = NetworkProtocolConstants.DefaultChunkSize;
            switch (cboChunkSize.SelectedIndex)
            {
                case 0: chunkSize = 32 * 1024; break;
                case 1: chunkSize = 64 * 1024; break;
                case 2: chunkSize = 128 * 1024; break;
                case 3: chunkSize = 256 * 1024; break;
                case 4: chunkSize = 512 * 1024; break;
                case 5: chunkSize = 1024 * 1024; break;
            }

            var args = new NetworkBackupWorkerArgs
            {
                ServerHost = host,
                ServerPort = port,
                SourcePath = source,
                BackupId = "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                ChunkSize = chunkSize,
                IsSingleFile = File.Exists(source),
                IsIncremental = radIncremental.Checked,
                AllowedExtensions = string.IsNullOrWhiteSpace(txtFilter.Text) ? "*.*" : txtFilter.Text.Trim(),
                EncryptBeforeTransfer = chkEncrypt.Checked,
                EncryptionPassword = txtPassword.Text,
                EnableAiRansomwareProtection = chkAiProtection.Checked
            };

            // Thiết lập trạng thái giao diện UI
            btnStart.Enabled = false;
            btnCancel.Enabled = true;
            btnPingServer.Enabled = false;
            progressBarTransfer.Value = 0;
            lblPercent.Text = "0%";
            lblSpeed.Text = "Tốc độ: 0.0 MB/s";
            lblChunks.Text = "Gói tin: 0 / 0";
            lblEta.Text = "Còn lại: --:--";
            lblCurrentFile.Text = "Trạng thái: Đang chuẩn bị...";

            string modeText = args.IsIncremental ? "Incremental (Gia tăng)" : "Full (Toàn bộ)";
            string encText = args.EncryptBeforeTransfer ? ", Mã hóa AES-256: BẬT" : string.Empty;
            string aiText = args.EnableAiRansomwareProtection ? ", AI Ransomware: BẬT" : string.Empty;

            Log($"[BẮT ĐẦU] Khởi chạy sao lưu TCP: Chế độ {modeText}{encText}{aiText} (Chunk: {chunkSize / 1024} KB)...");

            _lastArgs = args;
            // Kích hoạt BackgroundWorker (chạy ngầm chống treo UI)
            _worker.StartBackup(args);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (_worker.IsBusy)
            {
                Log("[HỦY] Người dùng yêu cầu hủy tiến trình sao lưu...");
                btnCancel.Enabled = false;
                lblCurrentFile.Text = "Trạng thái: Đang hủy tác vụ...";
                _worker.CancelBackup();
            }
        }

        private void Worker_TransferProgressUpdated(TransferProgressReport report)
        {
            // BackgroundWorker tự động marshal sự kiện này về WinForms Main UI Thread
            progressBarTransfer.Value = Math.Min(100, Math.Max(0, report.ProgressPercentage));
            lblPercent.Text = $"{report.ProgressPercentage}%";
            lblCurrentFile.Text = report.StatusMessage;
            lblSpeed.Text = $"Tốc độ: {report.FormattedSpeed}";
            lblChunks.Text = report.FormattedChunkStatus;
            lblEta.Text = $"Còn lại: {report.FormattedTimeRemaining}";

            if (!string.IsNullOrWhiteSpace(report.FileName) && report.BytesTransferred == 0)
            {
                Log($"[TRUYỀN FILE] {report.FileName} ({TransferProgressReport.FormatBytes(report.TotalBytes)}) - {report.TotalChunks} chunks");
            }
            else if (report.StatusMessage.Contains("AI đang phân tích") || report.StatusMessage.Contains("Ransomware"))
            {
                Log($"[AI BẢO MẬT] {report.StatusMessage}");
            }
        }

        private void Worker_TransferCompleted(NetworkBackupWorkerResult result)
        {
            btnStart.Enabled = true;
            btnCancel.Enabled = false;
            btnPingServer.Enabled = true;

            string modeLabel = (_lastArgs != null && _lastArgs.IsIncremental) ? "Mạng TCP (Incremental)" : "Mạng TCP (Full)";
            string sourceLabel = _lastArgs != null ? _lastArgs.SourcePath : txtSourcePath.Text.Trim();

            if (result.IsCancelled)
            {
                Log("[ĐÃ HỦY] Tiến trình sao lưu mạng đã bị hủy bởi người dùng.");
                lblCurrentFile.Text = "Trạng thái: Đã hủy tác vụ.";
                AddHistory(modeLabel, sourceLabel, "Đã hủy");
                MessageBox.Show("Tác vụ sao lưu mạng đã được hủy an toàn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (result.RansomwareThreat != null || result.Error is BackupSystem.Client.Security.RansomwareThreatDetectedException)
            {
                lblCurrentFile.Text = "Trạng thái: ⛔ BỊ CHẶN BỞI AI CHỐNG RANSOMWARE!";
                progressBarTransfer.Value = 0;
                lblPercent.Text = "0%";
                Log($"[CẢNH BÁO NGUY CƠ CAO] {result.Message}");
                AddHistory(modeLabel, sourceLabel, "Chặn bởi AI Ransomware");

                MessageBox.Show(
                    $"⛔ PHÁT HIỆN DẤU HIỆU TẤN CÔNG RANSOMWARE!\n\n" +
                    $"{result.Message}\n\n" +
                    $"Hệ thống đã CHẶN NGAY LẬP TỨC quá trình sao lưu để bảo vệ dữ liệu sạch trước đó trên máy chủ Server!",
                    "AI Phát hiện Ransomware - Chặn Sao Lưu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Stop);
            }
            else if (result.Success)
            {
                progressBarTransfer.Value = 100;
                lblPercent.Text = "100%";
                lblCurrentFile.Text = "Trạng thái: Hoàn tất 100%!";
                Log($"[HOÀN TẤT] {result.Message}");
                AddHistory(modeLabel, sourceLabel, $"Thành công ({result.TransferredFilesCount} file)");
                MessageBox.Show(result.Message, "Sao lưu thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                lblCurrentFile.Text = "Trạng thái: Gặp sự cố!";
                Log($"[THẤT BẠI] {result.Message}");
                AddHistory(modeLabel, sourceLabel, "Thất bại");
                MessageBox.Show($"Quá trình sao lưu mạng thất bại:\n{result.Message}", "Lỗi sao lưu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddHistory(string mode, string sourcePath, string status)
        {
            try
            {
                string dir = Path.GetDirectoryName(_historyPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string time = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                string line = string.Join("\t", time, mode, sourcePath.TrimEnd('\\', '/'), status);
                File.AppendAllText(_historyPath, line + Environment.NewLine);
            }
            catch { }
        }

        private void btnOpenHistory_Click(object sender, EventArgs e)
        {
            using (var frm = new FormHistory())
            {
                frm.Text = "Lịch sử sao lưu";
                frm.FormBorderStyle = FormBorderStyle.Sizable;
                frm.ControlBox = true;
                frm.MinimizeBox = true;
                frm.MaximizeBox = true;
                frm.StartPosition = FormStartPosition.CenterParent;
                frm.ShowDialog(this);
            }
        }

        private void btnOpenRestore_Click(object sender, EventArgs e)
        {
            using (var frm = new FormRestore())
            {
                frm.Text = "Khôi phục dữ liệu";
                frm.ServerHost = txtServerIp.Text.Trim();
                frm.ServerPort = (int)numPort.Value;
                frm.FormBorderStyle = FormBorderStyle.Sizable;
                frm.ControlBox = true;
                frm.MinimizeBox = true;
                frm.MaximizeBox = true;
                frm.StartPosition = FormStartPosition.CenterParent;
                frm.ShowDialog(this);
            }
        }

        private void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            lstLogs.Items.Add($"[{time}] {message}");
            lstLogs.TopIndex = lstLogs.Items.Count - 1; // Tự động cuộn xuống cuối
        }

        private void btnClearLogs_Click(object sender, EventArgs e)
        {
            lstLogs.Items.Clear();
        }
    }
}
