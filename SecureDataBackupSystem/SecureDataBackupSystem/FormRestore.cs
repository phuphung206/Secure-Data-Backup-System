
using System;
using System.IO;
using System.Windows.Forms;

namespace SecureDataBackupSystem
{
    public partial class FormRestore : Form
    {
        private string backupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SecureDataBackup");

        public FormRestore()
        {
            InitializeComponent();
            cboBackup.SelectedIndexChanged += cboBackup_SelectedIndexChanged;
        }

        // Lưu đường dẫn thật của file Backup
        private class BackupItem
        {
            public string FilePath { get; set; }
            // Phân biệt file và thư mục
            public bool IsFolder { get; set; }
            public override string ToString()
            {
                return (IsFolder ? "[Thư mục] " : "[File] ") + FilePath;
            }
        }


        // Hiển thị các file đã Backup lên ComboBox
        private void LoadBackupFiles()
        {
            cboBackup.Items.Clear();

            if (!Directory.Exists(backupFolder))
            {
                lblStatus.Text = "Chưa có dữ liệu Backup";
                return;
            }

            try
            {
                string[] files = Directory.GetFiles(
                    backupFolder, "*", SearchOption.AllDirectories);
                // Lấy các thư mục Backup trực tiếp trong thư mục lưu trữ
                string[] folders = Directory.GetDirectories(
                    backupFolder, "*", SearchOption.TopDirectoryOnly);
                // Thêm thư mục vào danh sách chọn Backup
                foreach (string folder in folders)
                {
                    cboBackup.Items.Add(new BackupItem
                    {
                        FilePath = folder,
                        IsFolder = true
                    });
                }

                foreach (string file in files)
                {
                    // Không hiển thị file lịch sử
                    if (Path.GetFileName(file).Equals(
                        "backup_history.txt",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    cboBackup.Items.Add(new BackupItem
                    {
                        FilePath = file
                    });
                }

                if (cboBackup.Items.Count > 0)
                {
                    cboBackup.SelectedIndex = 0;
                    lblStatus.Text = "Sẵn sàng khôi phục";
                }
                else
                {
                    lblStatus.Text = "Chưa có file Backup";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi đọc Backup: " + ex.Message);
            }
        }

        private void cboBackup_SelectedIndexChanged(object sender, EventArgs e)
        {
            BackupItem selected = cboBackup.SelectedItem as BackupItem;
            if (selected == null || selected.IsFolder)
            {
                lblDecryptPassword.Text = "Khóa DPAPI / mật khẩu";
                txtPassword.Enabled = selected != null;
                chkShowPassword.Enabled = selected != null;
                return;
            }

            bool encrypted = selected.FilePath.EndsWith(
                ".enc", StringComparison.OrdinalIgnoreCase);
            bool usesProtectedKey = encrypted &&
                BackupFileEncryptionService.IsEncryptedFile(selected.FilePath);
            bool usesPasswordKey = encrypted &&
                BackupFileEncryptionService.IsPasswordEncryptedFile(selected.FilePath);

            if (usesProtectedKey)
            {
                lblDecryptPassword.Text = "Khóa Windows (DPAPI)";
                txtPassword.Clear();
            }
            else if (usesPasswordKey)
            {
                lblDecryptPassword.Text = "Mật khẩu giải mã";
            }
            else if (encrypted)
            {
                lblDecryptPassword.Text = "Mật khẩu backup cũ";
            }
            else
            {
                lblDecryptPassword.Text = "Không cần giải mã";
                txtPassword.Clear();
            }

            txtPassword.Enabled = encrypted && !usesProtectedKey;
            chkShowPassword.Enabled = txtPassword.Enabled;
        }

        // Giải mã file Backup
        private void DecryptFile(
            string source, string destination, string password)
        {
            if (BackupFileEncryptionService.IsEncryptedFile(source))
            {
                ValidateProtectedKey();
                try
                {
                    BackupFileEncryptionService.DecryptFile(source, destination);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    throw new InvalidDataException(
                        "Không xác thực được dữ liệu Backup; file có thể đã bị thay đổi hoặc khóa không khớp.",
                        ex);
                }
                return;
            }
            if (BackupFileEncryptionService.IsPasswordEncryptedFile(source))
            {
                try
                {
                    BackupFileEncryptionService.DecryptFileWithPassword(
                        source, destination, password);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    throw new InvalidDataException(
                        "Mật khẩu không đúng hoặc dữ liệu Backup đã bị thay đổi.",
                        ex);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "Backup cũ cần mật khẩu để giải mã.");

            byte[] data = File.ReadAllBytes(source);

            if (data.Length < 64)
                throw new Exception("File mã hóa không hợp lệ!");

            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            byte[] savedTag = new byte[32];

            Array.Copy(data, 0, salt, 0, 16);
            Array.Copy(data, 16, iv, 0, 16);
            Array.Copy(data, data.Length - 32, savedTag, 0, 32);

            using (var derive =
                new System.Security.Cryptography.Rfc2898DeriveBytes(
                    password, salt, 100000))
            {
                byte[] keyMaterial = derive.GetBytes(64);
                byte[] encryptionKey = new byte[32];
                byte[] macKey = new byte[32];

                Array.Copy(keyMaterial, 0, encryptionKey, 0, 32);
                Array.Copy(keyMaterial, 32, macKey, 0, 32);

                int encryptedLength = data.Length - 32;

                byte[] actualTag;

                using (var hmac =
                    new System.Security.Cryptography.HMACSHA256(macKey))
                {
                    actualTag = hmac.ComputeHash(
                        data, 0, encryptedLength);
                }

                int difference = 0;

                for (int i = 0; i < 32; i++)
                    difference |= savedTag[i] ^ actualTag[i];

                if (difference != 0)
                    throw new Exception(
                        "Mật khẩu sai hoặc file mã hóa đã bị thay đổi!");

                using (var aes =
                    System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = encryptionKey;
                    aes.IV = iv;
                    aes.Mode =
                        System.Security.Cryptography.CipherMode.CBC;
                    aes.Padding =
                        System.Security.Cryptography.PaddingMode.PKCS7;

                    using (var input = new MemoryStream(
                        data, 32, data.Length - 64))
                    using (var crypto =
                        new System.Security.Cryptography.CryptoStream(
                            input,
                            aes.CreateDecryptor(),
                            System.Security.Cryptography.CryptoStreamMode.Read))
                    using (var output = File.Create(destination))
                    {
                        crypto.CopyTo(output);
                    }
                }
            }
        }

        // Chọn thư mục để khôi phục
        private void btnChooseFolder_Click_1(
            object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog =
                new FolderBrowserDialog())
            {
                dialog.Description = "Chọn thư mục khôi phục";

                if (dialog.ShowDialog() == DialogResult.OK)
                    txtRestorePath.Text = dialog.SelectedPath;
            }
        }

        // Bắt đầu khôi phục
        private void btnRestore_Click(
            object sender, EventArgs e)
        {
            BackupItem selected =
                cboBackup.SelectedItem as BackupItem;

            if (selected == null)
            {
                MessageBox.Show("Vui lòng chọn file Backup!");
                return;
            }

            string folder = txtRestorePath.Text.Trim();

            if (!Directory.Exists(folder))
            {
                MessageBox.Show("Vui lòng chọn thư mục khôi phục!");
                return;
            }

            string source = selected.FilePath;

            // Nếu chọn thư mục thì khôi phục toàn bộ thư mục
            if (selected.IsFolder)
            {
                string sourceFolder = selected.FilePath;

                if (!Directory.Exists(sourceFolder))
                {
                    MessageBox.Show("Thư mục Backup không tồn tại!");
                    LoadBackupFiles();
                    return;
                }

                string destinationFolder = Path.Combine(
                    folder,
                    Path.GetFileName(sourceFolder.TrimEnd('\\', '/')));

                // Không cho khôi phục vào bên trong thư mục Backup
                string sourceFull = Path.GetFullPath(sourceFolder)
                    .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

                string destinationFull = Path.GetFullPath(destinationFolder)
                    .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string backupRootFull = Path.GetFullPath(backupFolder)
                    .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

                if (destinationFull.StartsWith(sourceFull,
                        StringComparison.OrdinalIgnoreCase) ||
                    destinationFull.StartsWith(backupRootFull,
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Hãy chọn nơi khôi phục bên ngoài thư mục Backup!");
                    return;
                }

                // Không cho ghi đè thư mục đã tồn tại
                if (Directory.Exists(destinationFolder))
                {
                    MessageBox.Show("Thư mục đích đã tồn tại!");
                    return;
                }

                try
                {
                    progressRestore.Value = 0;
                    lblStatus.Text = "Đang khôi phục thư mục...";

                    // Gọi hàm RestoreFolder đã có trong code
                    RestoreFolder(sourceFolder, destinationFolder, txtPassword.Text);

                    progressRestore.Value = 100;
                    lblStatus.Text = "Khôi phục thư mục thành công!";

                    MessageBox.Show("Khôi phục thành công!\n" + destinationFolder);
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Khôi phục thất bại!";
                    MessageBox.Show("Lỗi: " + ex.Message);
                }

                return; // Không chạy tiếp phần khôi phục file
            }


            if (!File.Exists(source))
            {
                MessageBox.Show("File Backup không tồn tại!");
                LoadBackupFiles();
                return;
            }

            bool encrypted = source.EndsWith(
                ".enc", StringComparison.OrdinalIgnoreCase);
            bool usesProtectedKey = encrypted &&
                BackupFileEncryptionService.IsEncryptedFile(source);

            if (encrypted && !usesProtectedKey &&
                string.IsNullOrWhiteSpace(txtPassword.Text))
            {
                MessageBox.Show("Backup cũ cần mật khẩu để giải mã!");
                return;
            }

            string fileName = Path.GetFileName(source);

            if (encrypted)
                fileName = fileName.Substring(0, fileName.Length - 4);

            string destination = Path.GetFullPath(
                Path.Combine(folder, fileName));

            string backupRoot = Path.GetFullPath(backupFolder)
                .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

            if (destination.StartsWith(
                backupRoot, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Hãy chọn thư mục khôi phục ngoài thư mục Backup!");
                return;
            }

            if (string.Equals(
                Path.GetFullPath(source),
                destination,
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Không thể ghi đè file Backup!");
                return;
            }

            if (File.Exists(destination))
            {
                MessageBox.Show(
                    "File đã tồn tại trong thư mục khôi phục!");
                return;
            }

            string tempFile = Path.Combine(
                folder,
                ".restore_" + Guid.NewGuid().ToString("N") + ".tmp");

            progressRestore.Value = 0;
            lblStatus.Text = "Đang khôi phục...";

            try
            {
                if (encrypted)
                {
                    DecryptFile(source, tempFile, txtPassword.Text);
                }
                else
                {
                    File.Copy(source, tempFile);
                }

                File.Move(tempFile, destination);

                progressRestore.Value = 100;
                lblStatus.Text = "Khôi phục thành công!";

                MessageBox.Show(
                    "Khôi phục thành công!\n" + destination);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Khôi phục thất bại!";
                MessageBox.Show("Lỗi: " + ex.Message);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        // Khi mở FormRestore
        private void FormRestore_Load(object sender, EventArgs e)
        {
            txtPassword.Clear(); // Xóa nội dung mật khẩu có sẵn
            chkShowPassword.Checked = false;// Mặc định không tích hiện mật khẩu
            txtPassword.UseSystemPasswordChar = true;// Ẩn ký tự mật khẩu bằng dấu chấm
            progressRestore.Value = 0;// Đưa thanh tiến trình về 0
            LoadBackupFiles(); // Hiển thị danh sách Backup
        }

        private void lblStatus_Click( object sender, EventArgs e)
        {
        }

        // Hàm cho phép người dùng chọn thư mục Backup để khôi phục
        private void RestoreBackupFolder()
        {
            // Mở hộp thoại chọn thư mục Backup
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Chọn thư mục Backup cần khôi phục";

                // Nếu người dùng bấm Cancel thì dừng
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                // Lấy đường dẫn thư mục Backup đã chọn
                string sourceFolder = dialog.SelectedPath;

                // Lấy đường dẫn thư mục đích từ TextBox
                string destinationRoot = txtRestorePath.Text.Trim();

                // Kiểm tra người dùng đã chọn nơi khôi phục chưa
                if (!Directory.Exists(destinationRoot))
                {
                    MessageBox.Show("Vui lòng chọn nơi khôi phục trước!");
                    return;
                }

                // Tạo đường dẫn thư mục khôi phục
                // Ví dụ: D:\KhoiPhuc\BaiTap
                string destinationFolder = Path.Combine(
                    destinationRoot,
                    Path.GetFileName(sourceFolder.TrimEnd('\\', '/')));

                // Chuẩn hóa đường dẫn thư mục nguồn
                string sourceFull = Path.GetFullPath(sourceFolder)
                    .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

                // Chuẩn hóa đường dẫn thư mục đích
                string destinationFull = Path.GetFullPath(destinationFolder)
                    .TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

                // Không cho khôi phục vào bên trong thư mục nguồn
                // Tránh sao chép lặp vô hạn
                if (destinationFull.StartsWith(
                    sourceFull, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Không được khôi phục vào trong thư mục Backup!");
                    return;
                }

                // Kiểm tra thư mục đích đã tồn tại hay chưa
                if (Directory.Exists(destinationFolder))
                {
                    MessageBox.Show(
                        "Thư mục đích đã tồn tại. Hãy chọn nơi khác!");
                    return;
                }

                try
                {
                    // Đặt thanh tiến trình về 0%
                    progressRestore.Value = 0;

                    // Hiển thị trạng thái đang khôi phục
                    lblStatus.Text = "Đang khôi phục thư mục...";

                    // Gọi hàm khôi phục toàn bộ thư mục
                    RestoreFolder(
                        sourceFolder,
                        destinationFolder,
                        txtPassword.Text);

                    // Cập nhật thanh tiến trình khi hoàn thành
                    progressRestore.Value = 100;

                    // Hiển thị trạng thái thành công
                    lblStatus.Text = "Khôi phục thư mục thành công!";

                    // Thông báo vị trí lưu dữ liệu đã khôi phục
                    MessageBox.Show(
                        "Đã khôi phục thư mục tại:\n" + destinationFolder);
                }
                catch (Exception ex)
                {
                    // Bắt lỗi phát sinh trong quá trình khôi phục
                    lblStatus.Text = "Khôi phục thất bại!";
                    MessageBox.Show("Lỗi: " + ex.Message);
                }
            }
        }

        // Hàm khôi phục toàn bộ file và thư mục con từ bản Backup
        private void RestoreFolder(string sourceFolder,
                                   string destinationFolder,
                                   string password)
        {
            if (Directory.Exists(destinationFolder) || File.Exists(destinationFolder))
                throw new IOException("Đường dẫn khôi phục đã tồn tại.");

            string[] files = Directory.GetFiles(
                sourceFolder, "*", SearchOption.AllDirectories);
            bool needsProtectedKey = false;
            bool needsLegacyPassword = false;

            foreach (string file in files)
            {
                if (file.EndsWith(".enc.old", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!file.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (BackupFileEncryptionService.IsEncryptedFile(file))
                    needsProtectedKey = true;
                else
                    needsLegacyPassword = true;
            }

            if (needsLegacyPassword && string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "Backup cũ cần mật khẩu để giải mã.");

            if (needsProtectedKey)
                ValidateProtectedKey();

            string destinationPrefix = Path.GetFullPath(destinationFolder)
                .TrimEnd(Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            Directory.CreateDirectory(destinationFolder);
            try
            {
                foreach (string folder in Directory.GetDirectories(
                    sourceFolder, "*", SearchOption.AllDirectories))
                {
                    string relativePath = folder.Substring(
                        sourceFolder.Length).TrimStart('\\', '/');
                    string destination = Path.GetFullPath(Path.Combine(
                        destinationFolder, relativePath));
                    if (!destination.StartsWith(
                        destinationPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "Đường dẫn thư mục trong Backup không hợp lệ.");
                    Directory.CreateDirectory(destination);
                }

                foreach (string file in files)
                {
                    if (file.EndsWith(".enc.old",
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    string relativePath = file.Substring(
                        sourceFolder.Length).TrimStart('\\', '/');
                    bool encrypted = file.EndsWith(
                        ".enc", StringComparison.OrdinalIgnoreCase);
                    if (encrypted)
                        relativePath = relativePath.Substring(
                            0, relativePath.Length - 4);

                    string destination = Path.GetFullPath(Path.Combine(
                        destinationFolder, relativePath));
                    if (!destination.StartsWith(
                        destinationPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "Đường dẫn file trong Backup không hợp lệ.");
                    if (File.Exists(destination))
                        throw new IOException(
                            "File đã tồn tại: " + destination);

                    string destinationDirectory = Path.GetDirectoryName(destination);
                    Directory.CreateDirectory(destinationDirectory);
                    string temporaryPath = Path.Combine(
                        destinationDirectory,
                        ".restore_" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        if (encrypted)
                            DecryptFile(file, temporaryPath, password);
                        else
                            File.Copy(file, temporaryPath);

                        File.Move(temporaryPath, destination);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                }
            }
            catch (Exception restoreException)
            {
                try
                {
                    if (Directory.Exists(destinationFolder))
                        Directory.Delete(destinationFolder, true);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Khôi phục thất bại và không thể dọn dữ liệu khôi phục tạm.",
                        restoreException,
                        cleanupException);
                }

                throw;
            }
        }

        private void ValidateProtectedKey()
        {
            try
            {
                KeyProtectionService.UseExistingKey(key => true);
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy khóa DPAPI của tài khoản Windows hiện tại.",
                    ex);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Không mở được khóa DPAPI. Hãy dùng đúng tài khoản Windows đã tạo khóa.",
                    ex);
            }
        }

        private void txtRestorePath_TextChanged(
            object sender, EventArgs e)
        {
        }

        private void chkShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            // Tích vào ô: hiện mật khẩu
            // Bỏ tích: ẩn mật khẩu
            txtPassword.UseSystemPasswordChar = !chkShowPassword.Checked;
        }
    }
}
