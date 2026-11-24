using System;
using System.IO;
using NightDock.Sim;
using UnityEngine;

namespace NightDock.Game
{
    /// <summary>One save slot: the career, written after every shift report and every purchase.</summary>
    public static class SaveSystem
    {
        public static string PathOverride;
        public static string FilePath => PathOverride ?? Path.Combine(Application.persistentDataPath, "career.json");

        public static bool Exists => File.Exists(FilePath);

        public static void Save(Career c)
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(c, true));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("Night Dock: save failed: " + e.Message);
            }
        }

        public static Career Load()
        {
            try
            {
                if (!Exists) return null;
                var c = JsonUtility.FromJson<Career>(File.ReadAllText(FilePath));
                if (c == null || c.Forklifts <= 0 || c.ShiftNumber <= 0) return null;
                if (c.ExtraDoorRoutes == null) c.ExtraDoorRoutes = new System.Collections.Generic.List<int>();
                return c;
            }
            catch (Exception e)
            {
                Debug.LogError("Night Dock: save unreadable, starting fresh: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try { if (Exists) File.Delete(FilePath); } catch { }
        }
    }
}
