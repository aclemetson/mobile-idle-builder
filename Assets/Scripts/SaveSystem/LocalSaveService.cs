using System;
using System.IO;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Reads and writes the save file to the device's persistent data path.
    /// All operations are synchronous — call from a background thread or coroutine if needed.
    /// </summary>
    public class LocalSaveService
    {
        const string FileName = "save.json";

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

        public LocalSaveService()
        {
            _filePath = Path.Combine(SaveDirectory, FileName);
        }

        public bool Exists() => File.Exists(_filePath);

        public SaveData Load()
        {
            if (!Exists())
                return null;

            string json = File.ReadAllText(_filePath);
            return JsonUtility.FromJson<SaveData>(json);
        }

        public void Save(SaveData data)
        {
            data.lastSaved = DateTime.UtcNow.ToString("o");
            string json = JsonUtility.ToJson(data, prettyPrint: false);
            File.WriteAllText(_filePath, json);
        }

        /// <summary>Keeps the previous save as a .bak file before overwriting.</summary>
        public void SaveWithBackup(SaveData data)
        {
            if (Exists())
            {
                string backup = _filePath + ".bak";
                File.Copy(_filePath, backup, overwrite: true);
            }
            Save(data);
        }

        /// <summary>Deletes the save file and its .bak backup so a delete is a full wipe.</summary>
        public void Delete()
        {
            if (Exists())
                File.Delete(_filePath);

            string backup = _filePath + ".bak";
            if (File.Exists(backup))
                File.Delete(backup);
        }
    }
}
