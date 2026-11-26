namespace SecureDataBackupSystem
{
    partial class FormNetworkBackup
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.grpServer = new System.Windows.Forms.GroupBox();
            this.btnPingServer = new System.Windows.Forms.Button();
            this.numPort = new System.Windows.Forms.NumericUpDown();
            this.lblPort = new System.Windows.Forms.Label();
            this.txtServerIp = new System.Windows.Forms.TextBox();
            this.lblServerIp = new System.Windows.Forms.Label();
            this.grpSource = new System.Windows.Forms.GroupBox();
            this.btnBrowseFolder = new System.Windows.Forms.Button();
            this.btnBrowseFile = new System.Windows.Forms.Button();
            this.txtSourcePath = new System.Windows.Forms.TextBox();
            this.lblSource = new System.Windows.Forms.Label();
            this.grpOptions = new System.Windows.Forms.GroupBox();
            this.chkAiProtection = new System.Windows.Forms.CheckBox();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.lblPassword = new System.Windows.Forms.Label();
            this.chkEncrypt = new System.Windows.Forms.CheckBox();
            this.txtFilter = new System.Windows.Forms.TextBox();
            this.lblFilter = new System.Windows.Forms.Label();
            this.radIncremental = new System.Windows.Forms.RadioButton();
            this.radFull = new System.Windows.Forms.RadioButton();
            this.lblMode = new System.Windows.Forms.Label();
            this.grpTransfer = new System.Windows.Forms.GroupBox();
            this.cboChunkSize = new System.Windows.Forms.ComboBox();
            this.lblChunkConfig = new System.Windows.Forms.Label();
            this.lblEta = new System.Windows.Forms.Label();
            this.lblSpeed = new System.Windows.Forms.Label();
            this.lblChunks = new System.Windows.Forms.Label();
            this.lblCurrentFile = new System.Windows.Forms.Label();
            this.lblPercent = new System.Windows.Forms.Label();
            this.progressBarTransfer = new System.Windows.Forms.ProgressBar();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnOpenHistory = new System.Windows.Forms.Button();
            this.btnOpenRestore = new System.Windows.Forms.Button();
            this.grpLogs = new System.Windows.Forms.GroupBox();
            this.lstLogs = new System.Windows.Forms.ListBox();
            this.btnClearLogs = new System.Windows.Forms.Button();
            this.grpServer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
            this.grpSource.SuspendLayout();
            this.grpOptions.SuspendLayout();
            this.grpTransfer.SuspendLayout();
            this.grpLogs.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.lblTitle.Location = new System.Drawing.Point(20, 15);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(650, 37);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "🛡️ SAO LƯU QUA MẠNG TCP (CLIENT - SERVER)";
            // 
            // grpServer
            // 
            this.grpServer.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpServer.Controls.Add(this.btnPingServer);
            this.grpServer.Controls.Add(this.numPort);
            this.grpServer.Controls.Add(this.lblPort);
            this.grpServer.Controls.Add(this.txtServerIp);
            this.grpServer.Controls.Add(this.lblServerIp);
            this.grpServer.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpServer.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.grpServer.Location = new System.Drawing.Point(25, 60);
            this.grpServer.Name = "grpServer";
            this.grpServer.Size = new System.Drawing.Size(780, 80);
            this.grpServer.TabIndex = 1;
            this.grpServer.TabStop = false;
            this.grpServer.Text = "Cấu hình kết nối Máy chủ (TCP Socket)";
            // 
            // btnPingServer
            // 
            this.btnPingServer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPingServer.BackColor = System.Drawing.Color.SteelBlue;
            this.btnPingServer.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPingServer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPingServer.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnPingServer.ForeColor = System.Drawing.Color.White;
            this.btnPingServer.Location = new System.Drawing.Point(600, 30);
            this.btnPingServer.Name = "btnPingServer";
            this.btnPingServer.Size = new System.Drawing.Size(160, 35);
            this.btnPingServer.TabIndex = 4;
            this.btnPingServer.Text = "Kiểm tra kết nối";
            this.btnPingServer.UseVisualStyleBackColor = false;
            this.btnPingServer.Click += new System.EventHandler(this.btnPingServer_Click);
            // 
            // numPort
            // 
            this.numPort.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.numPort.Location = new System.Drawing.Point(440, 32);
            this.numPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            this.numPort.Minimum = new decimal(new int[] { 1024, 0, 0, 0 });
            this.numPort.Name = "numPort";
            this.numPort.Size = new System.Drawing.Size(120, 30);
            this.numPort.TabIndex = 3;
            this.numPort.Value = new decimal(new int[] { 5050, 0, 0, 0 });
            // 
            // lblPort
            // 
            this.lblPort.AutoSize = true;
            this.lblPort.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblPort.Location = new System.Drawing.Point(380, 35);
            this.lblPort.Name = "lblPort";
            this.lblPort.Size = new System.Drawing.Size(44, 23);
            this.lblPort.TabIndex = 2;
            this.lblPort.Text = "Port:";
            // 
            // txtServerIp
            // 
            this.txtServerIp.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.txtServerIp.Location = new System.Drawing.Point(120, 32);
            this.txtServerIp.Name = "txtServerIp";
            this.txtServerIp.Size = new System.Drawing.Size(230, 30);
            this.txtServerIp.TabIndex = 1;
            this.txtServerIp.Text = "127.0.0.1";
            // 
            // lblServerIp
            // 
            this.lblServerIp.AutoSize = true;
            this.lblServerIp.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblServerIp.Location = new System.Drawing.Point(20, 35);
            this.lblServerIp.Name = "lblServerIp";
            this.lblServerIp.Size = new System.Drawing.Size(81, 23);
            this.lblServerIp.TabIndex = 0;
            this.lblServerIp.Text = "Server IP:";
            // 
            // grpSource
            // 
            this.grpSource.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpSource.Controls.Add(this.btnBrowseFolder);
            this.grpSource.Controls.Add(this.btnBrowseFile);
            this.grpSource.Controls.Add(this.txtSourcePath);
            this.grpSource.Controls.Add(this.lblSource);
            this.grpSource.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpSource.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.grpSource.Location = new System.Drawing.Point(25, 150);
            this.grpSource.Name = "grpSource";
            this.grpSource.Size = new System.Drawing.Size(780, 85);
            this.grpSource.TabIndex = 2;
            this.grpSource.TabStop = false;
            this.grpSource.Text = "Chọn Dữ liệu cần Sao lưu (Hỗ trợ kéo thả)";
            // 
            // btnBrowseFolder
            // 
            this.btnBrowseFolder.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseFolder.BackColor = System.Drawing.Color.White;
            this.btnBrowseFolder.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBrowseFolder.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.btnBrowseFolder.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.btnBrowseFolder.Location = new System.Drawing.Point(650, 30);
            this.btnBrowseFolder.Name = "btnBrowseFolder";
            this.btnBrowseFolder.Size = new System.Drawing.Size(110, 35);
            this.btnBrowseFolder.TabIndex = 3;
            this.btnBrowseFolder.Text = "Thư mục...";
            this.btnBrowseFolder.UseVisualStyleBackColor = false;
            this.btnBrowseFolder.Click += new System.EventHandler(this.btnBrowseFolder_Click);
            // 
            // btnBrowseFile
            // 
            this.btnBrowseFile.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseFile.BackColor = System.Drawing.Color.White;
            this.btnBrowseFile.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBrowseFile.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.btnBrowseFile.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.btnBrowseFile.Location = new System.Drawing.Point(530, 30);
            this.btnBrowseFile.Name = "btnBrowseFile";
            this.btnBrowseFile.Size = new System.Drawing.Size(110, 35);
            this.btnBrowseFile.TabIndex = 2;
            this.btnBrowseFile.Text = "Chọn file...";
            this.btnBrowseFile.UseVisualStyleBackColor = false;
            this.btnBrowseFile.Click += new System.EventHandler(this.btnBrowseFile_Click);
            // 
            // txtSourcePath
            // 
            this.txtSourcePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSourcePath.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.txtSourcePath.Location = new System.Drawing.Point(120, 32);
            this.txtSourcePath.Name = "txtSourcePath";
            this.txtSourcePath.Size = new System.Drawing.Size(395, 30);
            this.txtSourcePath.TabIndex = 1;
            // 
            // lblSource
            // 
            this.lblSource.AutoSize = true;
            this.lblSource.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblSource.Location = new System.Drawing.Point(20, 35);
            this.lblSource.Name = "lblSource";
            this.lblSource.Size = new System.Drawing.Size(97, 23);
            this.lblSource.TabIndex = 0;
            this.lblSource.Text = "Đường dẫn:";
            // 
            // grpOptions
            // 
            this.grpOptions.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpOptions.Controls.Add(this.chkAiProtection);
            this.grpOptions.Controls.Add(this.txtPassword);
            this.grpOptions.Controls.Add(this.lblPassword);
            this.grpOptions.Controls.Add(this.chkEncrypt);
            this.grpOptions.Controls.Add(this.txtFilter);
            this.grpOptions.Controls.Add(this.lblFilter);
            this.grpOptions.Controls.Add(this.radIncremental);
            this.grpOptions.Controls.Add(this.radFull);
            this.grpOptions.Controls.Add(this.lblMode);
            this.grpOptions.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpOptions.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.grpOptions.Location = new System.Drawing.Point(25, 245);
            this.grpOptions.Name = "grpOptions";
            this.grpOptions.Size = new System.Drawing.Size(780, 135);
            this.grpOptions.TabIndex = 3;
            this.grpOptions.TabStop = false;
            this.grpOptions.Text = "Tùy chọn Sao lưu & Bảo mật nâng cao";
            // 
            // chkAiProtection
            // 
            this.chkAiProtection.AutoSize = true;
            this.chkAiProtection.Checked = true;
            this.chkAiProtection.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAiProtection.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.chkAiProtection.ForeColor = System.Drawing.Color.DarkRed;
            this.chkAiProtection.Location = new System.Drawing.Point(24, 98);
            this.chkAiProtection.Name = "chkAiProtection";
            this.chkAiProtection.Size = new System.Drawing.Size(647, 25);
            this.chkAiProtection.TabIndex = 8;
            this.chkAiProtection.Text = "🛡️ Bật AI Chống Ransomware (Tự động phát hiện mã hóa/đổi đuôi lạ & chặn sao lưu)";
            this.chkAiProtection.UseVisualStyleBackColor = true;
            // 
            // txtPassword
            // 
            this.txtPassword.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtPassword.Enabled = false;
            this.txtPassword.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.txtPassword.Location = new System.Drawing.Point(590, 62);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(170, 29);
            this.txtPassword.TabIndex = 7;
            this.txtPassword.UseSystemPasswordChar = true;
            // 
            // lblPassword
            // 
            this.lblPassword.AutoSize = true;
            this.lblPassword.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblPassword.Location = new System.Drawing.Point(505, 66);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(78, 21);
            this.lblPassword.TabIndex = 6;
            this.lblPassword.Text = "Mật khẩu:";
            // 
            // chkEncrypt
            // 
            this.chkEncrypt.AutoSize = true;
            this.chkEncrypt.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.chkEncrypt.Location = new System.Drawing.Point(330, 64);
            this.chkEncrypt.Name = "chkEncrypt";
            this.chkEncrypt.Size = new System.Drawing.Size(161, 25);
            this.chkEncrypt.TabIndex = 5;
            this.chkEncrypt.Text = "Mã hóa AES-256";
            this.chkEncrypt.UseVisualStyleBackColor = true;
            this.chkEncrypt.CheckedChanged += new System.EventHandler(this.chkEncrypt_CheckedChanged);
            // 
            // txtFilter
            // 
            this.txtFilter.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.txtFilter.Location = new System.Drawing.Point(120, 62);
            this.txtFilter.Name = "txtFilter";
            this.txtFilter.Size = new System.Drawing.Size(185, 29);
            this.txtFilter.TabIndex = 4;
            this.txtFilter.Text = "*.*";
            // 
            // lblFilter
            // 
            this.lblFilter.AutoSize = true;
            this.lblFilter.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblFilter.Location = new System.Drawing.Point(20, 66);
            this.lblFilter.Name = "lblFilter";
            this.lblFilter.Size = new System.Drawing.Size(89, 21);
            this.lblFilter.TabIndex = 3;
            this.lblFilter.Text = "Định dạng:";
            // 
            // radIncremental
            // 
            this.radIncremental.AutoSize = true;
            this.radIncremental.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.radIncremental.Location = new System.Drawing.Point(270, 28);
            this.radIncremental.Name = "radIncremental";
            this.radIncremental.Size = new System.Drawing.Size(395, 25);
            this.radIncremental.TabIndex = 2;
            this.radIncremental.Text = "Sao lưu gia tăng (Incremental - Chỉ gửi file thay đổi)";
            this.radIncremental.UseVisualStyleBackColor = true;
            // 
            // radFull
            // 
            this.radFull.AutoSize = true;
            this.radFull.Checked = true;
            this.radFull.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.radFull.Location = new System.Drawing.Point(120, 28);
            this.radFull.Name = "radFull";
            this.radFull.Size = new System.Drawing.Size(134, 25);
            this.radFull.TabIndex = 1;
            this.radFull.TabStop = true;
            this.radFull.Text = "Toàn bộ (Full)";
            this.radFull.UseVisualStyleBackColor = true;
            // 
            // lblMode
            // 
            this.lblMode.AutoSize = true;
            this.lblMode.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblMode.Location = new System.Drawing.Point(20, 30);
            this.lblMode.Name = "lblMode";
            this.lblMode.Size = new System.Drawing.Size(69, 21);
            this.lblMode.TabIndex = 0;
            this.lblMode.Text = "Chế độ:";
            // 
            // grpTransfer
            // 
            this.grpTransfer.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpTransfer.Controls.Add(this.btnOpenRestore);
            this.grpTransfer.Controls.Add(this.btnOpenHistory);
            this.grpTransfer.Controls.Add(this.cboChunkSize);
            this.grpTransfer.Controls.Add(this.lblChunkConfig);
            this.grpTransfer.Controls.Add(this.lblEta);
            this.grpTransfer.Controls.Add(this.lblSpeed);
            this.grpTransfer.Controls.Add(this.lblChunks);
            this.grpTransfer.Controls.Add(this.lblCurrentFile);
            this.grpTransfer.Controls.Add(this.lblPercent);
            this.grpTransfer.Controls.Add(this.progressBarTransfer);
            this.grpTransfer.Controls.Add(this.btnCancel);
            this.grpTransfer.Controls.Add(this.btnStart);
            this.grpTransfer.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpTransfer.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.grpTransfer.Location = new System.Drawing.Point(25, 390);
            this.grpTransfer.Name = "grpTransfer";
            this.grpTransfer.Size = new System.Drawing.Size(780, 185);
            this.grpTransfer.TabIndex = 4;
            this.grpTransfer.TabStop = false;
            this.grpTransfer.Text = "Tiến độ truyền tải (Multithreaded BackgroundWorker)";
            // 
            // cboChunkSize
            // 
            this.cboChunkSize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.cboChunkSize.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboChunkSize.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.cboChunkSize.FormattingEnabled = true;
            this.cboChunkSize.Items.AddRange(new object[] {
            "32 KB",
            "64 KB (Khuyến nghị)",
            "128 KB",
            "256 KB",
            "512 KB",
            "1024 KB (1 MB)"});
            this.cboChunkSize.Location = new System.Drawing.Point(580, 28);
            this.cboChunkSize.Name = "cboChunkSize";
            this.cboChunkSize.Size = new System.Drawing.Size(180, 29);
            this.cboChunkSize.TabIndex = 9;
            // 
            // lblChunkConfig
            // 
            this.lblChunkConfig.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblChunkConfig.AutoSize = true;
            this.lblChunkConfig.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblChunkConfig.Location = new System.Drawing.Point(440, 32);
            this.lblChunkConfig.Name = "lblChunkConfig";
            this.lblChunkConfig.Size = new System.Drawing.Size(130, 21);
            this.lblChunkConfig.TabIndex = 8;
            this.lblChunkConfig.Text = "Kích thước chunk:";
            // 
            // lblEta
            // 
            this.lblEta.AutoSize = true;
            this.lblEta.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblEta.Location = new System.Drawing.Point(540, 110);
            this.lblEta.Name = "lblEta";
            this.lblEta.Size = new System.Drawing.Size(95, 21);
            this.lblEta.TabIndex = 7;
            this.lblEta.Text = "Còn lại: --:--";
            // 
            // lblSpeed
            // 
            this.lblSpeed.AutoSize = true;
            this.lblSpeed.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblSpeed.Location = new System.Drawing.Point(280, 110);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new System.Drawing.Size(126, 21);
            this.lblSpeed.TabIndex = 6;
            this.lblSpeed.Text = "Tốc độ: 0.0 MB/s";
            // 
            // lblChunks
            // 
            this.lblChunks.AutoSize = true;
            this.lblChunks.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblChunks.Location = new System.Drawing.Point(20, 110);
            this.lblChunks.Name = "lblChunks";
            this.lblChunks.Size = new System.Drawing.Size(96, 21);
            this.lblChunks.TabIndex = 5;
            this.lblChunks.Text = "Gói tin: 0 / 0";
            // 
            // lblCurrentFile
            // 
            this.lblCurrentFile.AutoEllipsis = true;
            this.lblCurrentFile.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblCurrentFile.Location = new System.Drawing.Point(20, 32);
            this.lblCurrentFile.Name = "lblCurrentFile";
            this.lblCurrentFile.Size = new System.Drawing.Size(400, 23);
            this.lblCurrentFile.TabIndex = 4;
            this.lblCurrentFile.Text = "Trạng thái: Sẵn sàng";
            // 
            // lblPercent
            // 
            this.lblPercent.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblPercent.AutoSize = true;
            this.lblPercent.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblPercent.ForeColor = System.Drawing.Color.SteelBlue;
            this.lblPercent.Location = new System.Drawing.Point(710, 68);
            this.lblPercent.Name = "lblPercent";
            this.lblPercent.Size = new System.Drawing.Size(39, 25);
            this.lblPercent.TabIndex = 3;
            this.lblPercent.Text = "0%";
            // 
            // progressBarTransfer
            // 
            this.progressBarTransfer.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBarTransfer.Location = new System.Drawing.Point(20, 65);
            this.progressBarTransfer.Name = "progressBarTransfer";
            this.progressBarTransfer.Size = new System.Drawing.Size(680, 30);
            this.progressBarTransfer.TabIndex = 2;
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.BackColor = System.Drawing.Color.IndianRed;
            this.btnCancel.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCancel.Enabled = false;
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnCancel.ForeColor = System.Drawing.Color.White;
            this.btnCancel.Location = new System.Drawing.Point(600, 140);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(160, 38);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Hủy tác vụ";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnStart
            // 
            this.btnStart.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnStart.BackColor = System.Drawing.Color.SeaGreen;
            this.btnStart.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Location = new System.Drawing.Point(360, 140);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(220, 38);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "🚀 Bắt đầu sao lưu";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // btnOpenHistory
            // 
            this.btnOpenHistory.BackColor = System.Drawing.Color.SteelBlue;
            this.btnOpenHistory.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenHistory.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenHistory.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnOpenHistory.ForeColor = System.Drawing.Color.White;
            this.btnOpenHistory.Location = new System.Drawing.Point(20, 140);
            this.btnOpenHistory.Name = "btnOpenHistory";
            this.btnOpenHistory.Size = new System.Drawing.Size(155, 38);
            this.btnOpenHistory.TabIndex = 10;
            this.btnOpenHistory.Text = "📜 Lịch sử";
            this.btnOpenHistory.UseVisualStyleBackColor = false;
            this.btnOpenHistory.Click += new System.EventHandler(this.btnOpenHistory_Click);
            // 
            // btnOpenRestore
            // 
            this.btnOpenRestore.BackColor = System.Drawing.Color.DarkSlateBlue;
            this.btnOpenRestore.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenRestore.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnOpenRestore.ForeColor = System.Drawing.Color.White;
            this.btnOpenRestore.Location = new System.Drawing.Point(190, 140);
            this.btnOpenRestore.Name = "btnOpenRestore";
            this.btnOpenRestore.Size = new System.Drawing.Size(155, 38);
            this.btnOpenRestore.TabIndex = 11;
            this.btnOpenRestore.Text = "🔄 Khôi phục";
            this.btnOpenRestore.UseVisualStyleBackColor = false;
            this.btnOpenRestore.Click += new System.EventHandler(this.btnOpenRestore_Click);
            // 
            // grpLogs
            // 
            this.grpLogs.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLogs.Controls.Add(this.lstLogs);
            this.grpLogs.Controls.Add(this.btnClearLogs);
            this.grpLogs.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpLogs.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.grpLogs.Location = new System.Drawing.Point(25, 585);
            this.grpLogs.Name = "grpLogs";
            this.grpLogs.Size = new System.Drawing.Size(780, 185);
            this.grpLogs.TabIndex = 5;
            this.grpLogs.TabStop = false;
            this.grpLogs.Text = "Nhật ký truyền tin theo thời gian thực (TCP Stream Log)";
            // 
            // lstLogs
            // 
            this.lstLogs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstLogs.Font = new System.Drawing.Font("Consolas", 9F);
            this.lstLogs.FormattingEnabled = true;
            this.lstLogs.ItemHeight = 18;
            this.lstLogs.Location = new System.Drawing.Point(20, 30);
            this.lstLogs.Name = "lstLogs";
            this.lstLogs.Size = new System.Drawing.Size(740, 112);
            this.lstLogs.TabIndex = 0;
            // 
            // btnClearLogs
            // 
            this.btnClearLogs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearLogs.BackColor = System.Drawing.Color.White;
            this.btnClearLogs.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClearLogs.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.btnClearLogs.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.btnClearLogs.Location = new System.Drawing.Point(660, 148);
            this.btnClearLogs.Name = "btnClearLogs";
            this.btnClearLogs.Size = new System.Drawing.Size(100, 30);
            this.btnClearLogs.TabIndex = 1;
            this.btnClearLogs.Text = "Xóa nhật ký";
            this.btnClearLogs.UseVisualStyleBackColor = false;
            this.btnClearLogs.Click += new System.EventHandler(this.btnClearLogs_Click);
            // 
            // FormNetworkBackup
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(830, 785);
            this.Controls.Add(this.grpLogs);
            this.Controls.Add(this.grpTransfer);
            this.Controls.Add(this.grpOptions);
            this.Controls.Add(this.grpSource);
            this.Controls.Add(this.grpServer);
            this.Controls.Add(this.lblTitle);
            this.Name = "FormNetworkBackup";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Sao lưu dữ liệu qua mạng TCP Socket (Bảo mật AES-256 & AI Ransomware)";
            this.grpServer.ResumeLayout(false);
            this.grpServer.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
            this.grpSource.ResumeLayout(false);
            this.grpSource.PerformLayout();
            this.grpOptions.ResumeLayout(false);
            this.grpOptions.PerformLayout();
            this.grpTransfer.ResumeLayout(false);
            this.grpTransfer.PerformLayout();
            this.grpLogs.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.GroupBox grpServer;
        private System.Windows.Forms.Label lblServerIp;
        private System.Windows.Forms.TextBox txtServerIp;
        private System.Windows.Forms.NumericUpDown numPort;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.Button btnPingServer;
        private System.Windows.Forms.GroupBox grpSource;
        private System.Windows.Forms.Label lblSource;
        private System.Windows.Forms.TextBox txtSourcePath;
        private System.Windows.Forms.Button btnBrowseFolder;
        private System.Windows.Forms.Button btnBrowseFile;
        private System.Windows.Forms.GroupBox grpOptions;
        private System.Windows.Forms.Label lblMode;
        private System.Windows.Forms.RadioButton radFull;
        private System.Windows.Forms.RadioButton radIncremental;
        private System.Windows.Forms.Label lblFilter;
        private System.Windows.Forms.TextBox txtFilter;
        private System.Windows.Forms.CheckBox chkEncrypt;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.CheckBox chkAiProtection;
        private System.Windows.Forms.GroupBox grpTransfer;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnOpenHistory;
        private System.Windows.Forms.Button btnOpenRestore;
        private System.Windows.Forms.ProgressBar progressBarTransfer;
        private System.Windows.Forms.Label lblPercent;
        private System.Windows.Forms.Label lblCurrentFile;
        private System.Windows.Forms.Label lblSpeed;
        private System.Windows.Forms.Label lblChunks;
        private System.Windows.Forms.Label lblEta;
        private System.Windows.Forms.ComboBox cboChunkSize;
        private System.Windows.Forms.Label lblChunkConfig;
        private System.Windows.Forms.GroupBox grpLogs;
        private System.Windows.Forms.ListBox lstLogs;
        private System.Windows.Forms.Button btnClearLogs;
    }
}
