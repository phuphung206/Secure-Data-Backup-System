
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SecureDataBackupSystem.CoreEngine
{
    public sealed class RestoreEngine
    {
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

            var history = manifests
                .Where(m => m != null &&
                    string.Equals(
                        m.SourceKey,
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.CreatedUtc)
                .ThenBy(m => m.BackupId)
                .ToList();

            var snapshot =
                new Dictionary<string, BackupManifestEntry>(
                    StringComparer.OrdinalIgnoreCase);

            bool found = false;

            foreach (var manifest in history)
            {
                foreach (var file in manifest.Files)
                {
                    if (file == null ||
                        string.IsNullOrWhiteSpace(file.RelativePath))
                        throw new InvalidDataException(
                            "Manifest chua duong dan file hop le.");

                    if (file.ChangeType == FileChangeType.Deleted)
                    {
                        snapshot.Remove(file.RelativePath);
                    }
                    else if (
                        file.ChangeType == FileChangeType.New ||
                        file.ChangeType == FileChangeType.Modified)
                    {
                        if (string.IsNullOrWhiteSpace(file.StoredFileName))
                            throw new InvalidDataException(
                                "File backup thieu ten luu tru.");

                        snapshot[file.RelativePath] = file;
                    }
                }

                if (string.Equals(
                    manifest.BackupId,
                    targetBackupId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                throw new InvalidOperationException(
                    "Khong tim thay phien ban Backup.");

            return snapshot;
        }

public void RestoreSnapshot(
    Dictionary<string, BackupManifestEntry> snapshot,
    string backupFolder,
    string restoreFolder)
{
    if (snapshot == null)
        throw new ArgumentNullException(nameof(snapshot));

    if (!Directory.Exists(backupFolder))
        throw new DirectoryNotFoundException(
            "Khong tim thay thu muc Backup.");

    string destination = Path.GetFullPath(restoreFolder);

    if (Directory.Exists(destination) || File.Exists(destination))
        throw new IOException(
            "Thu muc Restore da ton tai. Hay chon thu muc moi.");

    string tempFolder = destination + ".tmp-" +
                        Guid.NewGuid().ToString("N");

    Directory.CreateDirectory(tempFolder);

    try
    {
        string safeRoot = Path.GetFullPath(tempFolder)
            .TrimEnd(Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (var item in snapshot)
        {
            BackupManifestEntry file = item.Value;

            if (Path.IsPathRooted(item.Key))
                throw new InvalidDataException(
                    "Duong dan file khong hop le.");

            string outputPath = Path.GetFullPath(
                Path.Combine(tempFolder, item.Key));

            if (!outputPath.StartsWith(
                safeRoot,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Duong dan file vuot ngoai thu muc Restore.");

            if (string.IsNullOrWhiteSpace(file.StoredFileName) ||
                file.StoredFileName !=
                Path.GetFileName(file.StoredFileName))
                throw new InvalidDataException(
                    "Ten file Backup khong hop le.");

            string compressedPath = Path.Combine(
                backupFolder, file.StoredFileName);


            string parentFolder = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidDataException(
                    "Khong xac dinh duoc thu muc Restore.");

            Directory.CreateDirectory(parentFolder);


            CompressionHelper.DecompressFile(
                compressedPath, outputPath);

            using (var stream = File.OpenRead(outputPath))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string actualHash = BitConverter.ToString(
                    sha.ComputeHash(stream)).Replace("-", "");

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

        Directory.Move(tempFolder, destination);
    }
    finally
    {
        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, true);
    }
}


public void RestoreVersion(
    string backupFolder,
    string sourceKey,
    string backupId,
    string restoreFolder)
{
    if (!Directory.Exists(backupFolder))
        throw new DirectoryNotFoundException(
            "Khong tim thay thu muc Backup.");

    var manifests = new List<BackupManifest>();

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

    var snapshot = BuildSnapshot(
        manifests, sourceKey, backupId);

    RestoreSnapshot(
        snapshot, backupFolder, restoreFolder);
}

    }
}
