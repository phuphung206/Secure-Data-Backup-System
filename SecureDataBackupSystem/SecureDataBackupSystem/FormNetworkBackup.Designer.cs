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
            this.grpLogs = new System.Windows.Forms.GroupBox();
            this.lstLogs = new System.Windows.Forms.ListBox();
            this.btnClearLogs = new System.Windows.Forms.Button();
            this.grpServer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
            this.grpSource.SuspendLayout();
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
            this.lblPort.Size = new System.Drawing.Size(55, 23);
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
            this.lblServerIp.Size = new System.Drawing.Size(84, 23);
            this.lblServerIp.TabIndex = 0;
            this.lblServerIp.Text = "Server IP:";
            // 
            // grpSource
            // 
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
            this.grpSource.Text = "Chọn Dữ liệu cần Sao lưu";
            // 
            // btnBrowseFolder
            // 
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
            this.lblSource.Size = new System.Drawing.Size(86, 23);
            this.lblSource.TabIndex = 0;
            this.lblSource.Text = "Đường dẫn:";
            // 
            // grpTransfer
            // 
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
            this.grpTransfer.Location = new System.Drawing.Point(25, 245);
            this.grpTransfer.Name = "grpTransfer";
            this.grpTransfer.Size = new System.Drawing.Size(780, 185);
            this.grpTransfer.TabIndex = 3;
            this.grpTransfer.TabStop = false;
            this.grpTransfer.Text = "Tiến độ truyền tải (Multithreaded BackgroundWorker)";
            // 
            // cboChunkSize
            // 
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
            this.lblChunkConfig.AutoSize = true;
            this.lblChunkConfig.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblChunkConfig.Location = new System.Drawing.Point(440, 32);
            this.lblChunkConfig.Name = "lblChunkConfig";
            this.lblChunkConfig.Size = new System.Drawing.Size(126, 21);
            this.lblChunkConfig.TabIndex = 8;
            this.lblChunkConfig.Text = "Kích thước chunk:";
            // 
            // lblEta
            // 
            this.lblEta.AutoSize = true;
            this.lblEta.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblEta.Location = new System.Drawing.Point(540, 110);
            this.lblEta.Name = "lblEta";
            this.lblEta.Size = new System.Drawing.Size(120, 21);
            this.lblEta.TabIndex = 7;
            this.lblEta.Text = "Còn lại: --:--";
            // 
            // lblSpeed
            // 
            this.lblSpeed.AutoSize = true;
            this.lblSpeed.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblSpeed.Location = new System.Drawing.Point(280, 110);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new System.Drawing.Size(120, 21);
            this.lblSpeed.TabIndex = 6;
            this.lblSpeed.Text = "Tốc độ: 0.0 MB/s";
            // 
            // lblChunks
            // 
            this.lblChunks.AutoSize = true;
            this.lblChunks.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblChunks.Location = new System.Drawing.Point(20, 110);
            this.lblChunks.Name = "lblChunks";
            this.lblChunks.Size = new System.Drawing.Size(100, 21);
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
            this.lblPercent.AutoSize = true;
            this.lblPercent.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblPercent.ForeColor = System.Drawing.Color.SteelBlue;
            this.lblPercent.Location = new System.Drawing.Point(710, 68);
            this.lblPercent.Name = "lblPercent";
            this.lblPercent.Size = new System.Drawing.Size(40, 25);
            this.lblPercent.TabIndex = 3;
            this.lblPercent.Text = "0%";
            // 
            // progressBarTransfer
            // 
            this.progressBarTransfer.Location = new System.Drawing.Point(20, 65);
            this.progressBarTransfer.Name = "progressBarTransfer";
            this.progressBarTransfer.Size = new System.Drawing.Size(680, 30);
            this.progressBarTransfer.TabIndex = 2;
            // 
            // btnCancel
            // 
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
            this.btnStart.BackColor = System.Drawing.Color.SeaGreen;
            this.btnStart.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Location = new System.Drawing.Point(360, 140);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(220, 38);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "🚀 Bắt đầu sao lưu (TCP)";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // grpLogs
            // 
            this.grpLogs.Controls.Add(this.lstLogs);
            this.grpLogs.Controls.Add(this.btnClearLogs);
            this.grpLogs.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.grpLogs.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(50)))), ((int)(((byte)(90)))));
            this.grpLogs.Location = new System.Drawing.Point(25, 440);
            this.grpLogs.Name = "grpLogs";
            this.grpLogs.Size = new System.Drawing.Size(780, 200);
            this.grpLogs.TabIndex = 4;
            this.grpLogs.TabStop = false;
            this.grpLogs.Text = "Nhật ký truyền tin theo thời gian thực (TCP Stream Log)";
            // 
            // lstLogs
            // 
            this.lstLogs.Font = new System.Drawing.Font("Consolas", 9F);
            this.lstLogs.FormattingEnabled = true;
            this.lstLogs.ItemHeight = 18;
            this.lstLogs.Location = new System.Drawing.Point(20, 30);
            this.lstLogs.Name = "lstLogs";
            this.lstLogs.Size = new System.Drawing.Size(740, 125);
            this.lstLogs.TabIndex = 0;
            // 
            // btnClearLogs
            // 
            this.btnClearLogs.BackColor = System.Drawing.Color.White;
            this.btnClearLogs.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClearLogs.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.btnClearLogs.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.btnClearLogs.Location = new System.Drawing.Point(660, 160);
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
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(830, 655);
            this.Controls.Add(this.grpLogs);
            this.Controls.Add(this.grpTransfer);
            this.Controls.Add(this.grpSource);
            this.Controls.Add(this.grpServer);
            this.Controls.Add(this.lblTitle);
            this.Name = "FormNetworkBackup";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Sao lưu dữ liệu qua mạng TCP Socket";
            this.grpServer.ResumeLayout(false);
            this.grpServer.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
            this.grpSource.ResumeLayout(false);
            this.grpSource.PerformLayout();
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
        private System.Windows.Forms.GroupBox grpTransfer;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnStart;
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

