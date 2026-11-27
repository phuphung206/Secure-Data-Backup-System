using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackupSystem.Client.NetworkClient;
using BackupSystem.Shared.Network;

namespace SecureDataBackupSystem
{
    /// <summary>
    /// Cấu hình kết nối Server dùng chung cho Sao lưu mạng, Lập lịch (Task Scheduler) và Theo dõi Real-time.
    /// Lưu tại %AppData%\SecureDataBackupSystem\client_settings.json; mật khẩu được bảo vệ bằng DPAPI
    /// (chỉ tài khoản Windows hiện tại giải mã được) để tác vụ chạy ngầm có thể dùng mà không lưu plaintext.
    /// </summary>
    public sealed class ClientSettings
    {
        public string ServerHost { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = NetworkProtocolConstants.DefaultPort;
        public string Username { get; set; } = "admin";
        public string ProtectedPassword { get; set; } = string.Empty;
        public bool EncryptBeforeTransfer { get; set; }
        public string ProtectedEncryptionPassword { get; set; } = string.Empty;
        public bool EnableCompression { get; set; } = true;
        public bool EnableAiRansomwareProtection { get; set; } = true;
        public string AllowedExtensions { get; set; } = "*.*";
        public int ChunkSize { get; set; } = NetworkProtocolConstants.DefaultChunkSize;

        public bool HasServerAccount => !string.IsNullOrWhiteSpace(ServerHost) && !string.IsNullOrWhiteSpace(Username);

        public string GetPassword() => ClientSettingsStore.Unprotect(ProtectedPassword, "123456");
        public void SetPassword(string password) => ProtectedPassword = ClientSettingsStore.Protect(password);

        public string GetEncryptionPassword() => ClientSettingsStore.Unprotect(ProtectedEncryptionPassword, string.Empty);
        public void SetEncryptionPassword(string password) => ProtectedEncryptionPassword = ClientSettingsStore.Protect(password);

        /// <summary>
        /// Tạo tham số sao lưu mạng từ cấu hình đã lưu (dùng cho lịch tự động và Real-time).
        /// </summary>
        public NetworkBackupWorkerArgs ToWorkerArgs(string sourcePath, bool incremental, string triggerType)
        {
            string encryptionPassword = EncryptBeforeTransfer ? GetEncryptionPassword() : string.Empty;
            if (EncryptBeforeTransfer && string.IsNullOrWhiteSpace(encryptionPassword))
                throw new InvalidOperationException("Cấu hình yêu cầu mã hóa AES-256 nhưng chưa lưu mật khẩu mã hóa. Hãy chạy Sao lưu mạng một lần để lưu cấu hình.");

            return new NetworkBackupWorkerArgs
            {
                ServerHost = ServerHost,
                ServerPort = ServerPort,
                Username = Username,
                Password = GetPassword(),
                SourcePath = sourcePath,
                IsSingleFile = File.Exists(sourcePath),
                BackupId = triggerType.ToLowerInvariant() + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"),
                IsIncremental = incremental,
                AllowedExtensions = string.IsNullOrWhiteSpace(AllowedExtensions) ? "*.*" : AllowedExtensions,
                EncryptBeforeTransfer = EncryptBeforeTransfer,
                EncryptionPassword = encryptionPassword,
                EnableCompression = EnableCompression,
                EnableAiRansomwareProtection = EnableAiRansomwareProtection,
                ChunkSize = ChunkSize > 0 ? ChunkSize : NetworkProtocolConstants.DefaultChunkSize,
                TriggerType = triggerType
            };
        }
    }

    public static class ClientSettingsStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SecureDataBackupSystem:ClientSettings:v1");

        public static string SettingsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureDataBackupSystem");

        public static string SettingsPath => Path.Combine(SettingsFolder, "client_settings.json");

        public static bool Exists => File.Exists(SettingsPath);

        public static ClientSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                    if (settings != null)
                        return settings;
                }
            }
            catch
            {
                // File cấu hình hỏng → dùng mặc định
            }
            return new ClientSettings();
        }

        public static void Save(ClientSettings settings)
        {
            Directory.CreateDirectory(SettingsFolder);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            string temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json, Encoding.UTF8);
            if (File.Exists(SettingsPath))
                File.Delete(SettingsPath);
            File.Move(temp, SettingsPath);
        }

        internal static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return string.Empty;
            byte[] data = Encoding.UTF8.GetBytes(plain);
            byte[] protectedBytes = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(data, 0, data.Length);
            return Convert.ToBase64String(protectedBytes);
        }

        internal static string Unprotect(string protectedValue, string fallback)
        {
            if (string.IsNullOrEmpty(protectedValue))
                return fallback;
            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
                string result = Encoding.UTF8.GetString(plain);
                Array.Clear(plain, 0, plain.Length);
                return result;
            }
            catch (CryptographicException)
            {
                return fallback;
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }

    /// <summary>
    /// Cấu hình lịch sao lưu (thay cho schedule_path.txt cũ, vẫn đọc được file cũ).
    /// </summary>
    public sealed class ScheduleConfig
    {
        public string SourcePath { get; set; } = string.Empty;
        public bool UploadToServer { get; set; }
        public bool Incremental { get; set; }

        public static string ConfigFolder => ClientSettingsStore.SettingsFolder;
        public static string ConfigPath => Path.Combine(ConfigFolder, "schedule_config.json");
        public static string LegacyConfigPath => Path.Combine(ConfigFolder, "schedule_path.txt");

        public static ScheduleConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<ScheduleConfig>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (config != null && !string.IsNullOrWhiteSpace(config.SourcePath))
                    return config;
            }

            if (File.Exists(LegacyConfigPath))
            {
                string path = File.ReadAllText(LegacyConfigPath).Trim();
                if (!string.IsNullOrWhiteSpace(path))
                    return new ScheduleConfig { SourcePath = path };
            }

            return null;
        }

        public void Save()
        {
            Directory.CreateDirectory(ConfigFolder);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
    }
}
