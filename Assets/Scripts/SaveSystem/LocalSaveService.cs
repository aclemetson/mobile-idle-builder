using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Reads and writes the save file to the device's persistent data path.
    /// All operations are synchronous — call from a background thread or coroutine if needed.
    ///
    /// Crash safety (#280):
    /// - Writes are atomic: the JSON goes to <c>save.json.tmp</c>, is flushed to disk, then swapped
    ///   over <c>save.json</c>. A process kill mid-write leaves the previous save.json intact.
    /// - <see cref="Load"/> falls back <c>save.json</c> → <c>save.json.tmp</c> → <c>save.json.bak</c>,
    ///   skipping any file that is missing, empty or unparseable.
    /// - If save.json was found corrupt, the next <see cref="SaveWithBackup"/> does NOT rotate it over
    ///   the good .bak — the backup is kept until a good save.json has been written again.
    /// </summary>
    public class LocalSaveService
    {
        const string FileName  = "save.json";
        const string TempSuffix   = ".tmp";
        const string BackupSuffix = ".bak";

        // JsonUtility serializes a null/empty string as "". Serializing with lastSaved blanked gives
        // a timestamp-free body to hash, and the stamp is then spliced into that same string so the
        // save is serialized only once.
        const string BlankStampToken = "\"lastSaved\":\"\"";

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Which file <see cref="Load"/> produced its result from.</summary>
        public enum LoadSource { None, Main, Temp, Backup }

        /// <summary>
        /// Test seam: the test assemblies point this at a temp folder for the whole run, so no test
        /// can read, overwrite or delete the developer's real save and sidecar files.
        /// </summary>
        internal static string SaveDirectoryOverride;

        /// <summary>
        /// Folder that holds save.json and every sidecar file (settings, recipe knowledge, cached
        /// flags). Everything the save wipe deletes must resolve its path from here.
        /// </summary>
        public static string SaveDirectory => SaveDirectoryOverride ?? Application.persistentDataPath;

        readonly string _filePath;
        readonly string _tempPath;
        readonly string _backupPath;

        // True after Load() found save.json missing-but-recoverable or corrupt. While set, save.json is
        // not trusted as a backup source, so SaveWithBackup leaves the existing .bak alone.
        bool _mainUntrusted;

        /// <summary>Where the last <see cref="Load"/> call got its data from.</summary>
        public LoadSource LastLoadSource { get; private set; } = LoadSource.None;

        /// <summary>
        /// Hash of the last written save's content, excluding the <c>lastSaved</c> stamp. Two saves
        /// with identical game state have the same hash, so callers can skip redundant cloud pushes.
        /// 0 until the first write.
        /// </summary>
        public ulong LastWrittenContentHash { get; private set; }

        public LocalSaveService()
        {
            _filePath   = Path.Combine(SaveDirectory, FileName);
            _tempPath   = _filePath + TempSuffix;
            _backupPath = _filePath + BackupSuffix;
        }

        public bool Exists() => File.Exists(_filePath);

        /// <summary>
        /// Loads the newest readable save: save.json, else a fully-written save.json.tmp left by a
        /// write that was interrupted before the swap, else save.json.bak. Returns null only when no
        /// readable save exists (a genuine new game, or every copy is corrupt).
        /// </summary>
        public SaveData Load()
        {
            LastLoadSource = LoadSource.None;
            bool mainExists = File.Exists(_filePath);

            if (TryRead(_filePath, out var data))
            {
                _mainUntrusted = false;
                LastLoadSource = LoadSource.Main;
                return data;
            }

            if (TryRead(_tempPath, out data))
            {
                _mainUntrusted = true;
                LastLoadSource = LoadSource.Temp;
                GameLogger.Warning(mainExists
                    ? "[Save] save.json is corrupt — recovered from save.json.tmp."
                    : "[Save] save.json missing — recovered from save.json.tmp (interrupted write).");
                return data;
            }

            if (TryRead(_backupPath, out data))
            {
                _mainUntrusted = true;
                LastLoadSource = LoadSource.Backup;
                GameLogger.Warning(mainExists
                    ? "[Save] save.json is corrupt — recovered from save.json.bak."
                    : "[Save] save.json missing — recovered from save.json.bak.");
                return data;
            }

            if (mainExists)
            {
                // Every copy is unreadable. Keep the files for diagnosis: the next save rewrites
                // save.json atomically and, because main is untrusted, does not rotate it to .bak.
                _mainUntrusted = true;
                GameLogger.Error("[Save] save.json and every backup are unreadable — starting fresh.");
            }
            return null;
        }

        /// <summary>Atomically writes <paramref name="data"/> to save.json. Leaves the .bak untouched.</summary>
        public void Save(SaveData data) => Write(data, rotateBackup: false);

        /// <summary>
        /// Keeps the previous save as a .bak file, then atomically writes <paramref name="data"/>.
        /// The rotation is skipped while the current save.json is known to be corrupt, so a bad file
        /// never replaces the last good backup.
        /// </summary>
        public void SaveWithBackup(SaveData data) => Write(data, rotateBackup: true);

        /// <summary>Deletes the save file, its .bak backup and any leftover .tmp so a delete is a full wipe.</summary>
        public void Delete()
        {
            if (File.Exists(_filePath))   File.Delete(_filePath);
            if (File.Exists(_backupPath)) File.Delete(_backupPath);
            if (File.Exists(_tempPath))   File.Delete(_tempPath);
            _mainUntrusted         = false;
            LastWrittenContentHash = 0;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        void Write(SaveData data, bool rotateBackup)
        {
            string json = Serialize(data, DateTime.UtcNow.ToString("o"), out ulong contentHash);

            // 1. Full new content to the temp file, flushed to the device before anything is replaced.
            using (var fs = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Utf8NoBom.GetBytes(json);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            // 2. Rotate the backup. A copy, not a move, so save.json stays valid throughout.
            if (rotateBackup && !_mainUntrusted && File.Exists(_filePath))
                File.Copy(_filePath, _backupPath, overwrite: true);

            // 3. Swap the temp file into place.
            ReplaceWithTemp();

            _mainUntrusted         = false;
            LastWrittenContentHash = contentHash;
        }

        void ReplaceWithTemp()
        {
            if (!File.Exists(_filePath))
            {
                File.Move(_tempPath, _filePath);
                return;
            }

            try
            {
                File.Replace(_tempPath, _filePath, destinationBackupFileName: null);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException
                                       || ex is UnauthorizedAccessException)
            {
                // Some filesystems reject File.Replace. Fall back to delete + move: if the process dies
                // between the two, save.json is missing but the complete .tmp is there and Load() uses it.
                GameLogger.Warning($"[Save] File.Replace failed ({ex.GetType().Name}) — using delete + move.");
                File.Delete(_filePath);
                File.Move(_tempPath, _filePath);
            }
        }

        /// <summary>
        /// Serializes <paramref name="data"/> once with <c>lastSaved</c> blanked (to hash the content
        /// without the timestamp), then splices <paramref name="stamp"/> into the JSON. Sets
        /// <c>data.lastSaved</c> to the stamp, as the old write path did.
        /// </summary>
        internal static string Serialize(SaveData data, string stamp, out ulong contentHash)
        {
            data.lastSaved = null;
            string body = JsonUtility.ToJson(data, prettyPrint: false);
            contentHash = Fnv1a64(body);
            data.lastSaved = stamp;

            int at = body.IndexOf(BlankStampToken, StringComparison.Ordinal);
            if (at < 0)
                return JsonUtility.ToJson(data, prettyPrint: false); // field renamed/moved: serialize again

            return body.Substring(0, at)
                 + "\"lastSaved\":\"" + stamp + "\""
                 + body.Substring(at + BlankStampToken.Length);
        }

        static bool TryRead(string path, out SaveData data)
        {
            data = null;
            if (!File.Exists(path)) return false;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;
                data = JsonUtility.FromJson<SaveData>(json);
                return data != null;
            }
            catch (Exception ex)
            {
                GameLogger.Warning($"[Save] Could not read {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
                data = null;
                return false;
            }
        }

        // 64-bit FNV-1a over UTF-16 code units. Stable across runs (unlike string.GetHashCode).
        internal static ulong Fnv1a64(string s)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime  = 1099511628211UL;
            ulong h = offset;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= prime;
            }
            return h;
        }
    }
}
