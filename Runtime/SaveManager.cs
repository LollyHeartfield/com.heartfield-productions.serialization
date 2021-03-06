using System;
using System.IO;
using UnityEngine;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Heartfield.Serialization
{
    public static class SaveManager
    {
        const int MAX_QUICK_SAVES_AMOUNT = 10;
        const int MAX_AUTO_SAVES_AMOUNT = 10;

        /// <summary>
        /// TKey is slot
        /// TValue is path
        /// </summary>
        static Dictionary<int, string> manualSavePaths = new Dictionary<int, string>(1);
        /// <summary>
        /// TKey is slot
        /// TValue is path
        /// </summary>
        static Dictionary<int, string> quickSavePaths = new Dictionary<int, string>(MAX_QUICK_SAVES_AMOUNT);
        /// <summary>
        /// TKey is slot
        /// TValue is path
        /// </summary>
        static Dictionary<int, string> autoSavePaths = new Dictionary<int, string>(MAX_AUTO_SAVES_AMOUNT);

        /// <summary>
        /// TKey is Type id
        /// TValue is Type Data
        /// </summary>
        static Dictionary<int, SaveData> datasToSave = new Dictionary<int, SaveData>();
        static Dictionary<ISaveable, int> saveables = new Dictionary<ISaveable, int>();
        static SaveData saveDataMaster = new SaveData();
        static GlobalSettingsData globalSettingsData = new GlobalSettingsData();

        const int DATA_PATH_ID = 1000;
        const string SETTINGS_DATA_NAME = "Settings";

        public delegate void SerializationEvents();
        public static SerializationEvents OnPopulateSave;
        public static SerializationEvents OnFinishPopulateSave;
        public static SerializationEvents OnLoadFromSave;
        public static SerializationEvents OnFinishLoadSaveData;
        public static SerializationEvents OnDeleteSaveData;

        struct PlayedTimeInterval
        {
            internal DateTime from;
            internal DateTime to;

            internal PlayedTimeInterval(PlayedTimeInterval time)
            {
                from = time.from;
                to = time.to;
            }

            internal TimeSpan GetTotalTime => to - from;
        }

        static HashSet<PlayedTimeInterval> playedTimeIntervals = new HashSet<PlayedTimeInterval>();
        static PlayedTimeInterval currentPlayedTimeInterval = new PlayedTimeInterval();
        static bool recordingPlayedTime;

        static void LoadGlobalData()
        {
            string path = SaveSettings.GetFilePath(SETTINGS_DATA_NAME);

            if (!File.Exists(path))
                globalSettingsData.Reset();
            else
                globalSettingsData = SerializationSystem.Deserialize<GlobalSettingsData>(path);
        }

        static void CheckFiles()
        {
            autoSavePaths.Clear();
            manualSavePaths.Clear();
            quickSavePaths.Clear();

            if (!Directory.Exists(SaveSettings.Directory))
                Directory.CreateDirectory(SaveSettings.Directory);

            var paths = Directory.GetFiles(SaveSettings.Directory, "*.sav").
                                            Where(file => Regex.IsMatch(Path.GetFileName(file), "^[0-9]+"));

            const string auto = "Auto";
            const string quick = "Quick";

            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                var sb = new StringBuilder(name);
                sb.Remove(2, name.Length - 2);
                int slot = int.Parse(sb.ToString());

                if (name.Contains(auto))
                    autoSavePaths.Add(slot, path);
                else if (name.Contains(quick))
                    quickSavePaths.Add(slot, path);
                else
                    manualSavePaths.Add(slot, path);
            }
        }

        static SaveManager()
        {
            CheckFiles();
            LoadGlobalData();
        }

        /// <summary>
        /// Total time in hours
        /// </summary>
        /// <returns></returns>
        public static int GetTotalPlayedTime()
        {
            var timeInterval = new PlayedTimeInterval(currentPlayedTimeInterval);

            if (recordingPlayedTime)
            {
                timeInterval.to = DateTime.Now;
            }

            return timeInterval.GetTotalTime.Hours;
        }

        public static void StopTotalPlayedTime()
        {
            if (!recordingPlayedTime)
                return;

            currentPlayedTimeInterval.to = DateTime.Now;

            playedTimeIntervals.Add(currentPlayedTimeInterval);
            currentPlayedTimeInterval = new PlayedTimeInterval();

            recordingPlayedTime = false;
        }

        public static void ResumeTotalPlayedTime()
        {
            if (recordingPlayedTime)
                return;

            currentPlayedTimeInterval.from = DateTime.Now;
            recordingPlayedTime = true;
        }

        #region ISaveable Extentions
        public static void Register(this ISaveable saveable, int id = default)
        {
            if (!saveables.ContainsKey(saveable))
            {
                var hash = new StringBuilder();
                hash.Append(saveable.GetType().MetadataToken);
                hash.Append(id);
                saveables.Add(saveable, int.Parse(hash.ToString()));
            }
        }

        public static void AddData<T>(this ISaveable saveable, T field)
        {
            var id = saveables[saveable];

            if (!datasToSave.ContainsKey(id))
                datasToSave.Add(id, new SaveData());

            datasToSave[id].AddData(field);         
        }

        public static void GetData<T>(this ISaveable saveable, ref T field)
        {
            var id = saveables[saveable];
            var data = saveDataMaster.GetData<SaveData>(id);
            field = data.GetData(field);
        }
        #endregion

        static void SerializeGlobalData(SaveType type, string path)
        {
            if (type == SaveType.Auto)
                globalSettingsData.lastAutoSavePath = path;
            else if (type == SaveType.Manual)
                globalSettingsData.lastManualSaveFilePath = path;
            else
                globalSettingsData.lastQuickSavePath = path;

            SerializationSystem.Serialize(globalSettingsData, SaveSettings.GetFilePath(SETTINGS_DATA_NAME));
        }

        static Dictionary<int, string> GetSavePaths(SaveType type)
        {
            if (type == SaveType.Auto)
                return autoSavePaths;
            else if (type == SaveType.Manual)
                return manualSavePaths;
            else
                return quickSavePaths;
        }

        static string GetSavePath(int slot, SaveType type) => GetSavePaths(type)[slot];

        static void SetupMasterData(string path)
        {
            foreach (var saveData in datasToSave)
            {
                saveDataMaster.AddData(saveData.Value, saveData.Key);
            }

            saveDataMaster.AddData(path, DATA_PATH_ID);
        }

        public static void SaveToFile(SaveType type, int slot = default)
        {
            CheckFiles();
            LoadGlobalData();

            int savesLimit = 0;
            int lastSavedSlot;

            if (type == SaveType.Auto)
            {
                savesLimit = MAX_AUTO_SAVES_AMOUNT;
                lastSavedSlot = globalSettingsData.lastAutoSaveSlot;
                slot = lastSavedSlot + 1;
                globalSettingsData.lastAutoSaveSlot = slot;
            }
            else if (type == SaveType.Quick)
            {
                savesLimit = MAX_QUICK_SAVES_AMOUNT;
                lastSavedSlot = globalSettingsData.lastQuickSaveSlot;
                slot = lastSavedSlot + 1;
                globalSettingsData.lastQuickSaveSlot = slot;
            }

            var fileName = new StringBuilder();
            fileName.Append(type);
            fileName.Append(DateTime.Now.ToOADate());
            fileName.Replace(',', '_');

            string path = Path.Combine(SaveSettings.Directory, $"{slot:00}{fileName}.sav");
            var savePaths = GetSavePaths(type);

            bool overSlotLimit = type != SaveType.Manual && savePaths.Count >= savesLimit;
            bool overrideSave = (savePaths.ContainsKey(slot) && type == SaveType.Manual) || overSlotLimit;

            if (overrideSave)
            {
                File.Move(savePaths[slot], path);
                savePaths[slot] = path;
            }
            else
                savePaths.Add(slot, path);

            foreach (var saveable in saveables.Keys)
            {
                saveable.OnPopulateSave();
            }

            SetupMasterData(path);

            OnPopulateSave?.Invoke();
            SerializationSystem.Serialize(saveDataMaster, path, Newtonsoft.Json.Formatting.Indented, false);

            SerializeGlobalData(type, path);

            OnFinishPopulateSave?.Invoke();

            Debug.Log($"Saved {type}\nSave files amount: {savePaths.Count}");
        }

        public static void LoadFromFile(SaveType type, int slot = default)
        {
            CheckFiles();
            LoadGlobalData();

            if (type == SaveType.Auto)
                slot = globalSettingsData.lastAutoSaveSlot;
            else if (type == SaveType.Quick)
                slot = globalSettingsData.lastQuickSaveSlot;

            LoadGlobalData();
            saveDataMaster = SerializationSystem.Deserialize<SaveData>(GetSavePath(slot, type));
            OnLoadFromSave?.Invoke();

            foreach (var saveable in saveables.Keys)
            {
                saveable.OnLoadFromSave();
            }

            Debug.Log($"Loaded {type}");
        }

        public static void DeleteFile(int slot, SaveType type)
        {
            OnDeleteSaveData?.Invoke();
            SerializationSystem.DeleteFile(GetSavePath(slot, type));
        }
    }
}