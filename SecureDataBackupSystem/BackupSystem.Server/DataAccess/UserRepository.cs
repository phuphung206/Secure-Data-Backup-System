using System;

namespace BackupSystem.Server.DataAccess
{
    public sealed class ServerUser
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public long QuotaBytes { get; set; }
        public long UsedBytes { get; set; }
    }

    /// <summary>
    /// Quản lý tài khoản người dùng trên Server (bảng Users): đăng nhập, tạo tài khoản, thống kê dung lượng.
    /// </summary>
    public class UserRepository
    {
        private readonly string? _dbFilePath;

        public UserRepository(string? dbFilePath = null)
        {
            _dbFilePath = dbFilePath;
        }

        /// <summary>
        /// Xác thực tài khoản. Trả về null nếu sai tên đăng nhập hoặc mật khẩu.
        /// Mật khẩu định dạng cũ (SHA-256 không salt) được tự động nâng cấp lên PBKDF2 sau khi đăng nhập đúng.
        /// </summary>
        public ServerUser? Authenticate(string username, string password, string? machineName = null)
        {
            if (string.IsNullOrWhiteSpace(username) || password == null)
                return null;

            ServerUser? user = null;
            string storedHash = string.Empty;

            using (var conn = DbConnectionFactory.GetConnection(_dbFilePath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT UserId, Username, PasswordHash, IFNULL(MachineName, ''), QuotaBytes, UsedBytes
                    FROM Users WHERE Username = $username LIMIT 1;";
                cmd.Parameters.AddWithValue("$username", username.Trim());

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    user = new ServerUser
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        MachineName = reader.GetString(3),
                        QuotaBytes = reader.GetInt64(4),
                        UsedBytes = reader.GetInt64(5)
                    };
                    storedHash = reader.GetString(2);
                }
            }

            if (user == null || !DbConnectionFactory.VerifyPassword(password, storedHash))
                return null;

            using (var conn = DbConnectionFactory.GetConnection(_dbFilePath))
            using (var cmd = conn.CreateCommand())
            {
                if (DbConnectionFactory.IsLegacyPasswordHash(storedHash))
                {
                    cmd.CommandText = "UPDATE Users SET PasswordHash = $hash, MachineName = IFNULL($machine, MachineName) WHERE UserId = $id;";
                    cmd.Parameters.AddWithValue("$hash", DbConnectionFactory.HashPassword(password));
                }
                else
                {
                    cmd.CommandText = "UPDATE Users SET MachineName = IFNULL($machine, MachineName) WHERE UserId = $id;";
                }
                cmd.Parameters.AddWithValue("$machine", (object?)machineName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$id", user.UserId);
                cmd.ExecuteNonQuery();
            }

            return user;
        }

        /// <summary>
        /// Tạo tài khoản mới (dùng từ màn hình console của Server).
        /// </summary>
        public int CreateUser(string username, string password, long quotaBytes = 10737418240)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Tên đăng nhập không được để trống.", nameof(username));
            if (string.IsNullOrEmpty(password) || password.Length < 6)
                throw new ArgumentException("Mật khẩu phải có ít nhất 6 ký tự.", nameof(password));

            using var conn = DbConnectionFactory.GetConnection(_dbFilePath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, QuotaBytes) VALUES ($username, $hash, $quota);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$username", username.Trim());
            cmd.Parameters.AddWithValue("$hash", DbConnectionFactory.HashPassword(password));
            cmd.Parameters.AddWithValue("$quota", quotaBytes);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void ChangePassword(string username, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
                throw new ArgumentException("Mật khẩu phải có ít nhất 6 ký tự.", nameof(newPassword));

            using var conn = DbConnectionFactory.GetConnection(_dbFilePath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET PasswordHash = $hash WHERE Username = $username;";
            cmd.Parameters.AddWithValue("$hash", DbConnectionFactory.HashPassword(newPassword));
            cmd.Parameters.AddWithValue("$username", username.Trim());
            if (cmd.ExecuteNonQuery() == 0)
                throw new InvalidOperationException($"Không tìm thấy tài khoản '{username}'.");
        }

        /// <summary>
        /// Cập nhật dung lượng đã dùng của người dùng = tổng dung lượng vật lý của các file do người dùng tải lên.
        /// </summary>
        public void RecalculateUsedBytes(int userId)
        {
            using var conn = DbConnectionFactory.GetConnection(_dbFilePath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Users SET UsedBytes = IFNULL((
                    SELECT SUM(f.EncryptedSizeBytes)
                    FROM BackedUpFiles f JOIN BackupVersions v ON v.VersionId = f.VersionId
                    WHERE v.UserId = $id AND f.ChangeType IN ('ADDED', 'MODIFIED')), 0)
                WHERE UserId = $id;";
            cmd.Parameters.AddWithValue("$id", userId);
            cmd.ExecuteNonQuery();
        }
    }
}
