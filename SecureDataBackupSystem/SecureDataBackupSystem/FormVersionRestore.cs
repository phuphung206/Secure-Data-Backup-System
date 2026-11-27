using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BackupSystem.Client.NetworkClient;
using BackupSystem.Shared.Models;

namespace SecureDataBackupSystem
{
    /// <summary>
    /// Quản lý phiên bản (Version Control) &amp; Khôi phục về đúng thời điểm từ Server.
    /// - Xem lịch sử các lần backup (Full/Incremental, thủ công/lịch/real-time) của tài khoản.
    /// - Xem snapshot file của từng phiên bản (kể cả file kế thừa từ phiên bản cũ hơn).
    /// - Khôi phục toàn bộ thư mục hoặc một/vài file về đúng trạng thái tại phiên bản đã chọn.
    /// Form được dựng hoàn toàn bằng code (không cần file Designer).
    /// </summary>
    public class FormVersionRestore : Form
    {
        private readonly TextBox txtHost = new TextBox { Width = 130 };
        private readonly NumericUpDown numPort = new NumericUpDown { Width = 70, Minimum = 1, Maximum = 65535 };
        private readonly TextBox txtUser = new TextBox { Width = 100 };
        private readonly TextBox txtServerPassword = new TextBox { Width = 100, UseSystemPasswordChar = true };
        private readonly CheckBox chkShowFailed = new CheckBox { Text = "Hiện bản lỗi/hủy", AutoSize = true };
        private readonly Button btnLoad = new Button { Text = "Tải lịch sử phiên bản", AutoSize = true };

        private readonly ListView lvVersions = new ListView();
        private readonly ListView lvFiles = new ListView();

        private readonly TextBox txtAesPassword = new TextBox { Width = 170, UseSystemPasswordChar = true };
        private readonly TextBox txtDestination = new TextBox { Width = 330 };
        private readonly Button btnBrowse = new Button { Text = "Chọn...", AutoSize = true };
        private readonly CheckBox chkOverwrite = new CheckBox { Text = "Cho phép ghi đè", AutoSize = true };
        private readonly Button btnRestoreSelected = new Button { Text = "Khôi phục file đã chọn", AutoSize = true, Enabled = false };
        private readonly Button btnRestoreAll = new Button { Text = "Khôi phục toàn bộ thư mục", AutoSize = true, Enabled = false };
        private readonly Button btnCancel = new Button { Text = "Hủy", AutoSize = true, Enabled = false };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Height = 20 };
        private readonly Label lblStatus = new Label { AutoSize = true, Text = "Nhập tài khoản Server rồi bấm \"Tải lịch sử phiên bản\"." };

        private CancellationTokenSource _cts;
        private List<BackupVersion> _versions = new List<BackupVersion>();

        public string ServerHost { get => txtHost.Text.Trim(); set => txtHost.Text = value; }
        public int ServerPort { get => (int)numPort.Value; set => numPort.Value = Math.Max(1, Math.Min(65535, value)); }

        public FormVersionRestore()
        {
            Text = "Phiên bản & Khôi phục theo thời điểm";
            Font = new Font("Segoe UI", 9.75F);
            ClientSize = new Size(980, 640);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;

            var settings = ClientSettingsStore.Load();
            txtHost.Text = settings.ServerHost;
            numPort.Value = Math.Max(1, Math.Min(65535, settings.ServerPort));
            txtUser.Text = settings.Username;
            txtServerPassword.Text = settings.GetPassword();
            if (settings.EncryptBeforeTransfer)
                txtAesPassword.Text = settings.GetEncryptionPassword();

            BuildLayout();

            btnLoad.Click += async (s, e) => await LoadVersionsAsync();
            lvVersions.SelectedIndexChanged += async (s, e) => await LoadFilesOfSelectedVersionAsync();
            btnBrowse.Click += (s, e) => BrowseDestination();
            btnRestoreSelected.Click += async (s, e) => await RestoreAsync(onlySelected: true);
            btnRestoreAll.Click += async (s, e) => await RestoreAsync(onlySelected: false);
            btnCancel.Click += (s, e) => _cts?.Cancel();
        }

        private void BuildLayout()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
            top.Controls.AddRange(new Control[]
            {
                MakeLabel("Server:"), txtHost, MakeLabel("Port:"), numPort,
                MakeLabel("Tài khoản:"), txtUser, MakeLabel("Mật khẩu:"), txtServerPassword,
                btnLoad, chkShowFailed
            });

            ConfigureListView(lvVersions, false);
            lvVersions.Columns.Add("#", 55);
            lvVersions.Columns.Add("Thời gian", 140);
            lvVersions.Columns.Add("Loại", 95);
            lvVersions.Columns.Add("Kích hoạt", 85);
            lvVersions.Columns.Add("Thư mục gốc", 300);
            lvVersions.Columns.Add("Số file", 65);
            lvVersions.Columns.Add("Dung lượng gốc", 105);
            lvVersions.Columns.Add("Trạng thái", 95);

            ConfigureListView(lvFiles, true);
            lvFiles.Columns.Add("Đường dẫn trong snapshot", 380);
            lvFiles.Columns.Add("Kích thước", 90);
            lvFiles.Columns.Add("Thay đổi", 90);
            lvFiles.Columns.Add("Nén", 50);
            lvFiles.Columns.Add("Mã hóa", 60);
            lvFiles.Columns.Add("Lưu ở phiên bản", 110);
            lvFiles.Columns.Add("Sửa lần cuối", 140);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220 };
            split.Panel1.Controls.Add(WrapInGroup("Lịch sử phiên bản (mới nhất ở trên)", lvVersions));
            split.Panel2.Controls.Add(WrapInGroup("File trong phiên bản đã chọn (tích chọn để khôi phục từng file)", lvFiles));

            var restoreRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 0), WrapContents = true };
            restoreRow.Controls.AddRange(new Control[]
            {
                MakeLabel("Mật khẩu AES-256:"), txtAesPassword,
                MakeLabel("Khôi phục vào:"), txtDestination, btnBrowse, chkOverwrite
            });

            var actionRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 0) };
            actionRow.Controls.AddRange(new Control[] { btnRestoreSelected, btnRestoreAll, btnCancel, lblStatus });

            var progressRow = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(10, 4, 10, 4) };
            progressRow.Controls.Add(progress);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 110 };
            bottom.Controls.Add(progressRow);
            bottom.Controls.Add(actionRow);
            bottom.Controls.Add(restoreRow);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(top);
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(6, 7, 2, 0) };
        }

        private static void ConfigureListView(ListView lv, bool checkBoxes)
        {
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = true;
            lv.HideSelection = false;
            lv.MultiSelect = false;
            lv.CheckBoxes = checkBoxes;
        }

        private static GroupBox WrapInGroup(string title, Control content)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6) };
            group.Controls.Add(content);
            return group;
        }

        private RemoteRestoreOptions BuildOptions()
        {
            return new RemoteRestoreOptions
            {
                ServerHost = ServerHost,
                ServerPort = ServerPort,
                Username = txtUser.Text.Trim(),
                Password = txtServerPassword.Text,
                EncryptionPassword = txtAesPassword.Text,
                OverwriteExisting = chkOverwrite.Checked
            };
        }

        private async Task LoadVersionsAsync()
        {
            SetBusy(true, "Đang tải lịch sử phiên bản từ Server...");
            try
            {
                _versions = await RemoteRestoreService.ListVersionsAsync(BuildOptions(), string.Empty, chkShowFailed.Checked);

                lvVersions.BeginUpdate();
                lvVersions.Items.Clear();
                lvFiles.Items.Clear();
                foreach (var v in _versions)
                {
                    var item = new ListViewItem("#" + v.VersionId) { Tag = v };
                    item.SubItems.Add(v.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss"));
                    item.SubItems.Add(v.BackupType == "FULL" ? "Full" : "Incremental");
                    item.SubItems.Add(TriggerLabel(v.TriggerType));
                    item.SubItems.Add(DisplayRoot(v.RootClientPath));
                    item.SubItems.Add(v.TotalFiles.ToString());
                    item.SubItems.Add(TransferProgressReport.FormatBytes(v.OriginalSizeBytes));
                    item.SubItems.Add(v.Status);
                    if (!string.Equals(v.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                        item.ForeColor = Color.Firebrick;
                    lvVersions.Items.Add(item);
                }
                lvVersions.EndUpdate();

                lblStatus.Text = _versions.Count == 0
                    ? "Chưa có phiên bản nào trên Server cho tài khoản này."
                    : $"Có {_versions.Count} phiên bản. Chọn một phiên bản để xem file.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Lỗi: " + ex.Message;
                MessageBox.Show(this, ex.Message, "Không tải được lịch sử phiên bản", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task LoadFilesOfSelectedVersionAsync()
        {
            var version = SelectedVersion();
            lvFiles.Items.Clear();
            btnRestoreAll.Enabled = btnRestoreSelected.Enabled = false;
            if (version == null)
                return;

            SetBusy(true, $"Đang tải danh sách file của phiên bản #{version.VersionId}...");
            try
            {
                var files = await RemoteRestoreService.GetVersionFilesAsync(BuildOptions(), version.VersionId);

                lvFiles.BeginUpdate();
                foreach (var f in files)
                {
                    var item = new ListViewItem(f.RelativePath) { Tag = f };
                    item.SubItems.Add(TransferProgressReport.FormatBytes(f.OriginalSizeBytes));
                    item.SubItems.Add(ChangeLabel(f.ChangeType));
                    item.SubItems.Add(f.IsCompressed ? "GZip" : "-");
                    item.SubItems.Add(f.IsEncrypted ? "AES" : "-");
                    item.SubItems.Add("#" + f.SourceVersionId);
                    item.SubItems.Add(f.LastModifiedTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
                    lvFiles.Items.Add(item);
                }
                lvFiles.EndUpdate();

                bool anyEncrypted = files.Any(f => f.IsEncrypted);
                lblStatus.Text = $"Phiên bản #{version.VersionId} ({version.CreatedAt:dd/MM/yyyy HH:mm}) có {files.Count} file" +
                                 (anyEncrypted ? " - cần mật khẩu AES-256 để giải mã." : ".");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Lỗi: " + ex.Message;
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void BrowseDestination()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Chọn thư mục để khôi phục dữ liệu" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    txtDestination.Text = dialog.SelectedPath;
            }
        }

        private async Task RestoreAsync(bool onlySelected)
        {
            var version = SelectedVersion();
            if (version == null)
            {
                MessageBox.Show(this, "Vui lòng chọn một phiên bản.", "Thông báo");
                return;
            }

            string destinationBase = txtDestination.Text.Trim();
            if (string.IsNullOrWhiteSpace(destinationBase) || !Directory.Exists(destinationBase))
            {
                MessageBox.Show(this, "Vui lòng chọn thư mục khôi phục hợp lệ.", "Thông báo");
                return;
            }

            List<string> selectedPaths = null;
            if (onlySelected)
            {
                selectedPaths = lvFiles.CheckedItems.Cast<ListViewItem>()
                    .Select(i => ((VersionFileEntry)i.Tag).RelativePath)
                    .ToList();
                if (selectedPaths.Count == 0 && lvFiles.SelectedItems.Count == 1)
                    selectedPaths.Add(((VersionFileEntry)lvFiles.SelectedItems[0].Tag).RelativePath);
                if (selectedPaths.Count == 0)
                {
                    MessageBox.Show(this, "Hãy tích chọn ít nhất một file trong danh sách.", "Thông báo");
                    return;
                }
            }

            // Khôi phục cả thư mục: tạo thư mục con riêng theo phiên bản để không trộn với dữ liệu hiện có
            string destination = onlySelected
                ? destinationBase
                : Path.Combine(destinationBase, SafeFolderName(version));

            SetBusy(true, onlySelected
                ? $"Đang khôi phục {selectedPaths.Count} file từ phiên bản #{version.VersionId}..."
                : $"Đang khôi phục toàn bộ thư mục tại phiên bản #{version.VersionId}...");
            _cts = new CancellationTokenSource();
            btnCancel.Enabled = true;
            progress.Value = 0;

            try
            {
                var progressHandler = new Progress<TransferProgressReport>(r =>
                {
                    progress.Value = Math.Max(0, Math.Min(100, r.ProgressPercentage));
                    lblStatus.Text = r.StatusMessage;
                });

                var summary = await RemoteRestoreService.RestoreVersionAsync(
                    BuildOptions(), version.VersionId, destination, selectedPaths, progressHandler, _cts.Token);

                progress.Value = 100;
                lblStatus.Text = $"Đã khôi phục {summary.RestoredFiles} file ({TransferProgressReport.FormatBytes(summary.RestoredBytes)}).";
                AppendHistory(onlySelected ? "Khôi phục file (Server)" : "Khôi phục thư mục (Server)",
                    $"Phiên bản #{version.VersionId} → {destination}", "Thành công");
                MessageBox.Show(this,
                    $"Khôi phục thành công {summary.RestoredFiles} file về thời điểm {version.CreatedAt:dd/MM/yyyy HH:mm:ss}.\nVị trí: {destination}",
                    "Khôi phục thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Đã hủy khôi phục.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Khôi phục thất bại: " + ex.Message;
                AppendHistory("Khôi phục (Server)", $"Phiên bản #{version.VersionId}", "Thất bại");
                MessageBox.Show(this, ex.Message, "Khôi phục thất bại", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnCancel.Enabled = false;
                _cts.Dispose();
                _cts = null;
                SetBusy(false, null);
            }
        }

        private BackupVersion SelectedVersion()
        {
            return lvVersions.SelectedItems.Count == 1 ? lvVersions.SelectedItems[0].Tag as BackupVersion : null;
        }

        private void SetBusy(bool busy, string message)
        {
            btnLoad.Enabled = !busy;
            lvVersions.Enabled = !busy;
            bool hasVersion = SelectedVersion() != null && lvFiles.Items.Count > 0;
            btnRestoreAll.Enabled = !busy && hasVersion;
            btnRestoreSelected.Enabled = !busy && hasVersion;
            UseWaitCursor = busy;
            if (message != null)
                lblStatus.Text = message;
        }

        private static string SafeFolderName(BackupVersion version)
        {
            string root = DisplayRoot(version.RootClientPath);
            string name = Path.GetFileName(root.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(name))
                name = "KhoiPhuc";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return $"{name}_phienban{version.VersionId}_{version.CreatedAt:yyyyMMdd_HHmmss}";
        }

        private static string DisplayRoot(string rootKey)
        {
            int separator = rootKey?.IndexOf('|') ?? -1;
            return separator >= 0 ? rootKey.Substring(separator + 1) : rootKey ?? string.Empty;
        }

        private static string TriggerLabel(string trigger)
        {
            switch ((trigger ?? string.Empty).ToUpperInvariant())
            {
                case "SCHEDULER": return "Lịch";
                case "WATCHER": return "Real-time";
                default: return "Thủ công";
            }
        }

        private static string ChangeLabel(string changeType)
        {
            switch ((changeType ?? string.Empty).ToUpperInvariant())
            {
                case "ADDED": return "Thêm mới";
                case "MODIFIED": return "Sửa đổi";
                case "UNCHANGED": return "Không đổi";
                default: return changeType;
            }
        }

        private static void AppendHistory(string mode, string target, string status)
        {
            try
            {
                string historyPath = BackupPaths.GetHistoryFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(historyPath));
                string line = string.Join("\t", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), mode, target, status);
                File.AppendAllText(historyPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
