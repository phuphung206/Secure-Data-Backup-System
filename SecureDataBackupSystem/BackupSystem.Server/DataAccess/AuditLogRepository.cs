using System;
using System.Collections.Generic;
using BackupSystem.Shared.Models;

namespace BackupSystem.Server.DataAccess
{
    public class AuditLogRepository
    {
        // ==================== 1. QUẢN LÝ RESTORE LOGS ====================

        // Ghi nhận mỗi khi người dùng yêu cầu Phục hồi (Restore 1 file lẻ hoặc cả thư mục)
        public int LogRestoreOperation(int userId, int versionId, string restoreScope, string targetFilePath, string status = "COMPLETED")
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO RestoreLogs (UserId, VersionId, RestoreScope, TargetFilePath, Status)
                VALUES ($userId, $verId, $scope, $target, $status);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$verId", versionId);
            cmd.Parameters.AddWithValue("$scope", restoreScope); // 'FULL_FOLDER' hoặc 'SINGLE_FILE'
            cmd.Parameters.AddWithValue("$target", targetFilePath);
            cmd.Parameters.AddWithValue("$status", status);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Lấy lịch sử các lần Restore của User
        public List<RestoreLog> GetRestoreHistoryByUser(int userId)
        {
            var list = new List<RestoreLog>();
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT RestoreId, UserId, VersionId, RestoreScope, TargetFilePath, Status, RestoredAt
                FROM RestoreLogs
                WHERE UserId = $userId
                ORDER BY RestoreId DESC;";
            cmd.Parameters.AddWithValue("$userId", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RestoreLog
                {
                    RestoreId = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    VersionId = reader.GetInt32(2),
                    RestoreScope = reader.GetString(3),
                    TargetFilePath = reader.GetString(4),
                    Status = reader.GetString(5),
                    RestoredAt = DateTime.Parse(reader.GetString(6))
                });
            }
            return list;
        }

        // ==================== 2. QUẢN LÝ SECURITY & AI LOGS ====================

        // Ghi nhận cảnh báo khi AI của Nhất phát hiện dấu hiệu Ransomware
        public int LogSecurityAlert(int userId, string eventType, double anomalyScore, int affectedFilesCount, string description)
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO SecurityLogs (UserId, EventType, AnomalyScore, AffectedFilesCount, Description)
                VALUES ($userId, $event, $score, $count, $desc);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$event", eventType);
            cmd.Parameters.AddWithValue("$score", anomalyScore);
            cmd.Parameters.AddWithValue("$count", affectedFilesCount);
            cmd.Parameters.AddWithValue("$desc", description);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Lấy danh sách cảnh báo Ransomware để hiển thị trên màn hình Admin Server
        public List<SecurityLog> GetSecurityLogsByUser(int userId)
        {
            var list = new List<SecurityLog>();
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT LogId, UserId, EventType, AnomalyScore, AffectedFilesCount, Description, CreatedAt
                FROM SecurityLogs
                WHERE UserId = $userId
                ORDER BY LogId DESC;";
            cmd.Parameters.AddWithValue("$userId", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SecurityLog
                {
                    LogId = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    EventType = reader.GetString(2),
                    AnomalyScore = reader.GetDouble(3),
                    AffectedFilesCount = reader.GetInt32(4),
                    Description = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                });
            }
            return list;
        }
    }
}
