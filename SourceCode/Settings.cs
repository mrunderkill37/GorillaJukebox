using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GorillaJukebox;

[Serializable]
public class SettingsData
{
    public float volume = 0.5f;
    public int theme = 0;
    public bool showRoomCode = true;
    public bool shuffle = false;
    public int repeat = 0;        // 0 = off, 1 = all, 2 = one
    public string lastSong = "";
    public bool showFingerDot = true;                      // little dot that marks where a press happens
    // Per-song start points (seconds), stored by file name. JsonUtility cannot save a Dictionary, so two lists.
    public List<string> startNames = new List<string>();
    public List<float> startTimes = new List<float>();
    public Vector3 fingerTip = new Vector3(0f, -0.1f, 0f); // fingertip position relative to your right hand (hand axes, meters)
}

/// <summary>Saved to GorillaJukebox/settings.json next to the game's exe.</summary>
public static class Settings
{
    public static SettingsData Data = new SettingsData();

    public static string Folder =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "GorillaJukebox"));

    public static string MusicFolder => Path.Combine(Folder, "Music");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(MusicFolder);
            if (File.Exists(FilePath))
            {
                SettingsData loaded = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath));
                if (loaded != null) Data = loaded;
                if (Data.startNames == null) Data.startNames = new List<string>();
                if (Data.startTimes == null) Data.startTimes = new List<float>();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[{Constants.Name}] Could not load settings: {e}");
        }
    }

    /// <summary>Where this song should start playing, in seconds (0 = the beginning).</summary>
    public static float GetStart(string songPath)
    {
        int i = Data.startNames.IndexOf(Path.GetFileName(songPath));
        return i >= 0 && i < Data.startTimes.Count ? Data.startTimes[i] : 0f;
    }

    public static void SetStart(string songPath, float seconds)
    {
        string key = Path.GetFileName(songPath);
        int i = Data.startNames.IndexOf(key);

        if (seconds < 0.5f)
        {
            if (i >= 0)
            {
                Data.startNames.RemoveAt(i);
                if (i < Data.startTimes.Count) Data.startTimes.RemoveAt(i);
            }
        }
        else if (i >= 0)
        {
            while (Data.startTimes.Count <= i) Data.startTimes.Add(0f);
            Data.startTimes[i] = seconds;
        }
        else
        {
            // Keep both lists the same length, whatever state the file was in.
            while (Data.startTimes.Count < Data.startNames.Count) Data.startTimes.Add(0f);
            Data.startNames.Add(key);
            Data.startTimes.Add(seconds);
        }

        Save();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
        }
        catch (Exception e)
        {
            Debug.LogError($"[{Constants.Name}] Could not save settings: {e}");
        }
    }
}
