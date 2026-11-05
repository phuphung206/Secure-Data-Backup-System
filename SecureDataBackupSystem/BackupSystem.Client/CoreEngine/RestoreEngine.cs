
using System;
using System.Collections.Generic;
using System.IO;

namespace SecureDataBackupSystem.CoreEngine
{
    public sealed class RestoreEngine
    {
        // 1. Dung lai Snapshot tu lich su Backup
        public Dictionary<string, BackupManifestEntry> BuildSnapshot(
            IEnumerable<BackupManifest> manifests,
            string sourceKey,
            string targetBackupId)
        {
            if (manifests == null)
                throw new ArgumentNullException(nameof(manifests));

            if (string.IsNullOrWhiteSpace(sourceKey) ||
                string.IsNullOrWhiteSpace(targetBackupId))
                throw new ArgumentException(
                    "SourceKey va BackupId khong duoc rong.");

            var versions = new Dictionary<string, BackupManifest>(
                StringComparer.OrdinalIgnoreCase);

            // Loc cac Manifest cua dung thu muc nguon
            foreach (var manifest in manifests)
            {
                if (manifest == null ||
                    !string.Equals(
                        manifest.SourceKey,
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(manifest.BackupId) ||
                    versions.ContainsKey(manifest.BackupId))
                {
                    throw new InvalidDataException(
                        "BackupId rong hoac bi trung.");
                }

                versions.Add(manifest.BackupId, manifest);
            }

            // Tim phien ban muon Restore
            if (!versions.TryGetValue(
                targetBackupId, out var target))
            {
                throw new InvalidDataException(
                    "Khong tim thay phien ban can Restore.");
            }

            // Truy nguoc lich su ve ban Backup nen
            var chain = new Stack<BackupManifest>();

            var visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            BackupManifest current = target;

            while (true)
            {
                if (!visited.Add(current.BackupId))
                {
                    throw new InvalidDataException(
                        "Phat hien vong lap trong lich su Backup.");
                }

                chain.Push(current);

                if (current.IsBaseline)
                {
                    if (!string.IsNullOrWhiteSpace(
                        current.PreviousBackupId))
                    {
                        throw new InvalidDataException(
                            "Backup nen co lien ket khong hop le.");
                    }

                    break;
                }

                if (string.IsNullOrWhiteSpace(
                    current.PreviousBackupId) ||
                    !versions.TryGetValue(
                        current.PreviousBackupId,
                        out var previous))
                {
                    throw new InvalidDataException(
                        "Lich su Backup bi thieu phien ban.");
                }

                current = previous;
            }

            // Khoi tao Snapshot
            var snapshot =
                new Dictionary<string, BackupManifestEntry>(
                    StringComparer.OrdinalIgnoreCase);

            // Ap dung tung phien ban tu cu den moi
            while (chain.Count > 0)
            {
                BackupManifest version = chain.Pop();

                if (version.Files == null)
                {
                    throw new InvalidDataException(
                        "Manifest thieu danh sach file.");
                }

                foreach (var file in version.Files)
                {
                    if (file == null ||
                        string.IsNullOrWhiteSpace(file.RelativePath))
                    {
                        throw new InvalidDataException(
                            "Manifest chua duong dan khong hop le.");
                    }

                    if (file.ChangeType == FileChangeType.Deleted)
                    {
                        // Xoa file khoi Snapshot
                        snapshot.Remove(file.RelativePath);
                    }
                    else if (
                        file.ChangeType == FileChangeType.New ||
                        file.ChangeType == FileChangeType.Modified)
                    {
                        if (string.IsNullOrWhiteSpace(
                            file.StoredFileName))
                        {
                            throw new InvalidDataException(
                                "Thieu ten file Backup.");
                        }

                        // Cap nhat phien ban moi cua file
                        snapshot[file.RelativePath] = file;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            "Trang thai file khong hop le.");
                    }
                }
            }

            return snapshot;
        }

        // 2. Giai nen va phuc hoi file tu Snapshot
        public void RestoreSnapshot(
            Dictionary<string, BackupManifestEntry> snapshot,
            string backupFolder,
            string restoreFolder)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (!Directory.Exists(backupFolder))
            {
                throw new DirectoryNotFoundException(
                    "Khong tim thay thu muc Backup.");
            }

            if (string.IsNullOrWhiteSpace(restoreFolder))
            {
                throw new ArgumentException(
                    "Thu muc Restore khong duoc rong.");
            }

            string destination = Path.GetFullPath(restoreFolder);

            // Khong ghi de thu muc da ton tai
            if (Directory.Exists(destination) ||
                File.Exists(destination))
            {
                throw new IOException(
                    "Thu muc Restore da ton tai. Hay chon thu muc moi.");
            }

            // Phuc hoi vao thu muc tam truoc
            string tempFolder = destination + ".tmp-" +
                                Guid.NewGuid().ToString("N");

            Directory.CreateDirectory(tempFolder);

            try
            {
                string safeRoot = Path.GetFullPath(tempFolder)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                foreach (var item in snapshot)
                {
                    BackupManifestEntry file = item.Value;

                    if (file == null ||
                        string.IsNullOrWhiteSpace(item.Key) ||
                        Path.IsPathRooted(item.Key))
                    {
                        throw new InvalidDataException(
                            "Duong dan file khong hop le.");
                    }

                    string outputPath = Path.GetFullPath(
                        Path.Combine(tempFolder, item.Key));

                    // Ngan duong dan vuot ra khoi thu muc Restore
                    if (!outputPath.StartsWith(
                        safeRoot,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Duong dan file vuot ngoai thu muc Restore.");
                    }

                    // Kiem tra ten file nen
                    if (string.IsNullOrWhiteSpace(file.StoredFileName) ||
                        file.StoredFileName !=
                        Path.GetFileName(file.StoredFileName))
                    {
                        throw new InvalidDataException(
                            "Ten file Backup khong hop le.");
                    }

                    string compressedPath = Path.Combine(
                        backupFolder,
                        file.StoredFileName);

                    if (!File.Exists(compressedPath))
                    {
                        throw new FileNotFoundException(
                            "Khong tim thay file Backup.",
                            compressedPath);
                    }

                    string parentFolder =
                        Path.GetDirectoryName(outputPath)
                        ?? throw new InvalidDataException(
                            "Khong xac dinh duoc thu muc Restore.");

                    Directory.CreateDirectory(parentFolder);

                    // Giai nen file
                    CompressionHelper.DecompressFile(
                        compressedPath,
                        outputPath);

                    // Kiem tra SHA-256 sau khi phuc hoi
                    using (var stream = File.OpenRead(outputPath))
                    using (var sha =
                        System.Security.Cryptography.SHA256.Create())
                    {
                        string actualHash = BitConverter.ToString(
                            sha.ComputeHash(stream))
                            .Replace("-", "");

                        if (!string.Equals(
                            actualHash,
                            file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                "Restore that bai: SHA-256 khong khop.");
                        }
                    }
                }

                // Chi cong bo ket qua khi tat ca file hop le
                Directory.Move(tempFolder, destination);
            }
            finally
            {
                // Don thu muc tam neu Restore that bai
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }
            }
        }

        // 3. Restore theo phien ban BackupId
        public void RestoreVersion(
            string backupFolder,
            string sourceKey,
            string backupId,
            string restoreFolder)
        {
            if (!Directory.Exists(backupFolder))
            {
                throw new DirectoryNotFoundException(
                    "Khong tim thay thu muc Backup.");
            }

            var manifests = new List<BackupManifest>();

            // Doc lich su Manifest
            foreach (string path in Directory.EnumerateFiles(
                backupFolder, "manifest-*.json"))
            {
                string json = File.ReadAllText(path);

                BackupManifest manifest =
                    System.Text.Json.JsonSerializer
                        .Deserialize<BackupManifest>(json)
                    ?? throw new InvalidDataException(
                        "Manifest khong hop le: " + path);

                manifests.Add(manifest);
            }

            // Dung Snapshot cua phien ban duoc chon
            var snapshot = BuildSnapshot(
                manifests,
                sourceKey,
                backupId);

            // Phuc hoi file
            RestoreSnapshot(
                snapshot,
                backupFolder,
                restoreFolder);
        }
    }
}
