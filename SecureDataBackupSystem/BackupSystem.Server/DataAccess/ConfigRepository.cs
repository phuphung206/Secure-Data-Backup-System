using System;
using System.Collections.Generic;
using BackupSystem.Shared.Models;
using Microsoft.Data.Sqlite;

namespace BackupSystem.Server.DataAccess
{
    public class ConfigRepository
    {
        // 1. [CREATE / UPDATE] Lưu hoặc cập nhật cấu hình thư mục cần Backup của Client
        public int SaveOrUpdateConfig(BackupConfig config)
        {
            using var conn = DbConnectionFactory.GetConnection();
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = @"
                SELECT ConfigId FROM BackupConfigs 
                WHERE UserId = $userId AND SourceFolderPath = $path LIMIT 1;";
            checkCmd.Parameters.AddWithValue("$userId", config.UserId);
            checkCmd.Parameters.AddWithValue("$path", config.SourceFolderPath);

            object? existingId = checkCmd.ExecuteScalar();

            if (existingId != null && existingId != DBNull.Value)
            {
                int configId = Convert.ToInt32(existingId);
                using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE BackupConfigs
                    SET AllowedExtensions = $allow,
                        ExcludedPatterns = $exclude,
                        ScheduleCron = $cron,
                        EnableRealtimeWatch = $watch,
                        IsActive = $active,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE ConfigId = $id;";

                updateCmd.Parameters.AddWithValue("$allow", config.AllowedExtensions);
                updateCmd.Parameters.AddWithValue("$exclude", config.ExcludedPatterns);
                updateCmd.Parameters.AddWithValue("$cron", config.ScheduleCron);
                updateCmd.Parameters.AddWithValue("$watch", config.EnableRealtimeWatch ? 1 : 0);
                updateCmd.Parameters.AddWithValue("$active", config.IsActive ? 1 : 0);
                updateCmd.Parameters.AddWithValue("$id", configId);
                updateCmd.ExecuteNonQuery();
                return configId;
            }
            else
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO BackupConfigs 
                    (UserId, SourceFolderPath, AllowedExtensions, ExcludedPatterns, ScheduleCron, EnableRealtimeWatch, IsActive)
                    VALUES ($userId, $path, $allow, $exclude, $cron, $watch, $active);
                    SELECT last_insert_rowid();";

                insertCmd.Parameters.AddWithValue("$userId", config.UserId);
                insertCmd.Parameters.AddWithValue("$path", config.SourceFolderPath);
                insertCmd.Parameters.AddWithValue("$allow", config.AllowedExtensions);
                insertCmd.Parameters.AddWithValue("$exclude", config.ExcludedPatterns);
                insertCmd.Parameters.AddWithValue("$cron", config.ScheduleCron);
                insertCmd.Parameters.AddWithValue("$watch", config.EnableRealtimeWatch ? 1 : 0);
                insertCmd.Parameters.AddWithValue("$active", config.IsActive ? 1 : 0);

                return Convert.ToInt32(insertCmd.ExecuteScalar());
            }
        }

        // 2. [READ] Lấy danh sách cấu hình đang kích hoạt của User (Đồng bộ về Client khi vừa mở App)
        public List<BackupConfig> GetConfigsByUser(int userId)
        {
            var list = new List<BackupConfig>();
            using var conn = DbConnectionFactory.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ConfigId, UserId, SourceFolderPath, AllowedExtensions, ExcludedPatterns, 
                       ScheduleCron, EnableRealtimeWatch, IsActive, UpdatedAt
                FROM BackupConfigs
                WHERE UserId = $userId;";
            cmd.Parameters.AddWithValue("$userId", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new BackupConfig
                {
                    ConfigId = reader.GetInt32(0),
                    UserId = reader.GetInt32(1),
                    SourceFolderPath = reader.GetString(2),
                    AllowedExtensions = reader.GetString(3),
                    ExcludedPatterns = reader.GetString(4),
                    ScheduleCron = reader.GetString(5),
                    EnableRealtimeWatch = reader.GetInt32(6) == 1,
                    IsActive = reader.GetInt32(7) == 1,
                    UpdatedAt = DateTime.Parse(reader.GetString(8))
                });
            }
            return list;
        }
    }
}
