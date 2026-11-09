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
                IsSingleFile = File.Exists(source)
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
            lblCurrentFile.Text = "Trạng thái: Bắt đầu kết nối...";

            Log($"[BẮT ĐẦU] Khởi chạy tiến trình sao lưu mạng dưới nền (Chunk: {chunkSize / 1024} KB)...");

            // Kích hoạt BackgroundWorker (chạy ngầm chống treo UI hoàn hảo)
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
        }

        private void Worker_TransferCompleted(NetworkBackupWorkerResult result)
        {
            btnStart.Enabled = true;
            btnCancel.Enabled = false;
            btnPingServer.Enabled = true;

            if (result.IsCancelled)
            {
                Log("[ĐÃ HỦY] Tiến trình sao lưu mạng đã bị hủy bởi người dùng.");
                lblCurrentFile.Text = "Trạng thái: Đã hủy tác vụ.";
                MessageBox.Show("Tác vụ sao lưu mạng đã được hủy an toàn.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (result.Success)
            {
                progressBarTransfer.Value = 100;
                lblPercent.Text = "100%";
                lblCurrentFile.Text = "Trạng thái: Hoàn tất 100%!";
                Log($"[HOÀN TẤT] {result.Message}");
                MessageBox.Show(result.Message, "Sao lưu thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                lblCurrentFile.Text = "Trạng thái: Gặp sự cố!";
                Log($"[THẤT BẠI] {result.Message}");
                MessageBox.Show($"Quá trình sao lưu mạng thất bại:\n{result.Message}", "Lỗi sao lưu", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

