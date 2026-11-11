using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GorillaJukebox;

public enum RepeatMode { Off, All, One }

public class Song
{
    public string Name;
    public string Path;
}

/// <summary>
/// Audio playback and playlist logic: song library, play/pause, volume, next/previous,
/// shuffle, repeat, and auto-advance when a song ends. No UI in here.
/// </summary>
public class Jukebox : MonoBehaviour
{
    public static Jukebox Instance { get; private set; }

    private static readonly Dictionary<string, AudioType> AudioTypes = new Dictionary<string, AudioType>
    {
        { ".mp3",  AudioType.MPEG },
        { ".wav",  AudioType.WAV },
        { ".ogg",  AudioType.OGGVORBIS },
        { ".aiff", AudioType.AIFF },
        { ".aif",  AudioType.AIFF },
    };

    public readonly List<Song> Songs = new List<Song>();
    public Song Current { get; private set; }
    public AudioSource Source { get; private set; }
    public bool IsLoading { get; private set; }
    public bool Paused { get; private set; }
    public bool Shuffle { get; private set; }
    public RepeatMode Repeat { get; private set; }

    /// <summary>Raised whenever the song, play state, shuffle or repeat changes.</summary>
    public event Action Changed;

    /// <summary>Short status messages the menu shows for a couple of seconds.</summary>
    public event Action<string> Toast;

    // Songs still waiting to be played in the current shuffle cycle.
    private readonly List<Song> shuffleQueue = new List<Song>();

    // Songs played earlier, so "Previous" can step back while shuffling.
    private readonly List<Song> history = new List<Song>();
    private const int MaxHistory = 50;

    private int loadToken;
    private bool wasPlaying;
    private bool suppressHistory;

    private void Awake()
    {
        Instance = this;

        Source = gameObject.AddComponent<AudioSource>();
        Source.playOnAwake = false;
        Source.loop = false;
        Source.spatialBlend = 0f; // plain stereo, only you can hear it
        Source.volume = Mathf.Clamp01(Settings.Data.volume);

        Shuffle = Settings.Data.shuffle;
        Repeat = (RepeatMode)Mathf.Clamp(Settings.Data.repeat, 0, 2);
    }

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        // Detect "song reached the end by itself": it was playing, now it isn't,
        // the user didn't pause, and playback rewound to the start.
        if (!IsLoading && !Paused && Source.clip != null
            && wasPlaying && !Source.isPlaying && Source.timeSamples <= 0)
        {
            OnSongFinished();
        }

        wasPlaying = Source.isPlaying;
    }

    // ------------------------------------------------------------------ library

    /// <summary>Rescan the Music folder.</summary>
    public void Refresh()
    {
        string keepPath = Current != null ? Current.Path : Settings.Data.lastSong;

        Songs.Clear();
        try
        {
            Directory.CreateDirectory(Settings.MusicFolder);
            IEnumerable<string> files = Directory.GetFiles(Settings.MusicFolder)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (!AudioTypes.ContainsKey(ext)) continue;

                Songs.Add(new Song
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(file),
                    Path = file
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[{Constants.Name}] Could not read the music folder: {e}");
        }

        history.Clear();
        shuffleQueue.Clear();

        Song same = Songs.FirstOrDefault(s => s.Path == keepPath);
        if (same != null)
        {
            Current = same; // still there: keep playing / keep selection
        }
        else
        {
            StopAndClear();
            Current = Songs.FirstOrDefault();
        }

        RebuildShuffleQueue();
        Say($"Loaded {Songs.Count} song{(Songs.Count == 1 ? "" : "s")}");
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ playback

    public void Play(Song song)
    {
        if (song == null) return;

        // Remember what we're leaving, so Previous works while shuffling.
        if (Current != null && Current != song && Source.clip != null && !suppressHistory)
        {
            history.Add(Current);
            if (history.Count > MaxHistory) history.RemoveAt(0);
        }
        suppressHistory = false;

        StartCoroutine(LoadAndPlay(song));
    }

    private IEnumerator LoadAndPlay(Song song)
    {
        int token = ++loadToken;

        StopAndClear();
        Current = song;
        IsLoading = true;
        Paused = false;
        Changed?.Invoke();

        string url = new Uri(song.Path).AbsoluteUri;
        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioTypeFor(song.Path));
        yield return request.SendWebRequest();

        if (token != loadToken) yield break; // another song was picked meanwhile

        IsLoading = false;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Say($"Could not load {song.Name}");
            Debug.LogError($"[{Constants.Name}] Failed to load {song.Path}: {request.error}");
            Changed?.Invoke();
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        clip.name = song.Name;
        Source.clip = clip;
        Source.Play();
        ApplyStartPoint();
        wasPlaying = true;

        Settings.Data.lastSong = song.Path;
        Settings.Save();
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        if (Current == null || IsLoading) return;

        if (Source.clip == null)
        {
            Play(Current); // nothing loaded yet (first press after launch)
            return;
        }

        if (Source.isPlaying)
        {
            Source.Pause();
            Paused = true;
        }
        else if (Paused)
        {
            Source.UnPause();
            Paused = false;
        }
        else
        {
            Source.Play(); // song had finished: start it again
            ApplyStartPoint();
        }

        Changed?.Invoke();
    }

    /// <summary>If this song has a saved start point, jump to it. Call right after Source.Play().</summary>
    private void ApplyStartPoint()
    {
        if (Current == null || Source.clip == null) return;

        float start = Settings.GetStart(Current.Path);
        if (start > 0f)
            Source.time = Mathf.Min(start, Mathf.Max(0f, Source.clip.length - 0.5f));
    }

    /// <summary>Jump to a position in the song that is loaded now. Stays paused if it was paused.</summary>
    public void Seek(float seconds)
    {
        if (Source.clip == null || IsLoading) return;

        float target = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, Source.clip.length - 0.1f));
        if (!Source.isPlaying && !Paused) Source.Play(); // song had finished: wake it up first
        Source.time = target;
        Changed?.Invoke();
    }

    private void StopAndClear()
    {
        Source.Stop();
        AudioClip old = Source.clip;
        Source.clip = null;
        if (old != null) Destroy(old);
        Paused = false;
        wasPlaying = false;
    }

    private static AudioType AudioTypeFor(string path)
    {
        return AudioTypes.TryGetValue(System.IO.Path.GetExtension(path).ToLowerInvariant(), out AudioType t)
            ? t
            : AudioType.UNKNOWN;
    }

    // ------------------------------------------------------------------ next / previous

    public void Next(bool auto = false)
    {
        if (Songs.Count == 0) return;

        bool wrap = Repeat == RepeatMode.All;
        Song next = Shuffle
            ? NextShuffle(!auto || wrap)
            : NextLinear(wrap);

        if (next != null)
            Play(next);
        else if (auto)
            Say("End of playlist");
    }

    public void Previous()
    {
        if (Songs.Count == 0) return;

        // More than 3 seconds in: restart the song instead of leaving it.
        if (Source.clip != null && Source.time > 3f)
        {
            Source.time = 0f;
            return;
        }

        if (Shuffle)
        {
            while (history.Count > 0)
            {
                Song prev = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (prev == null || prev == Current || !Songs.Contains(prev)) continue;

                suppressHistory = true;
                Play(prev);
                return;
            }

            if (Source.clip != null) Source.time = 0f;
            return;
        }

        int index = Songs.IndexOf(Current);
        if (index > 0)
            Play(Songs[index - 1]);
        else if (Repeat == RepeatMode.All)
            Play(Songs[Songs.Count - 1]);
        else if (Source.clip != null)
            Source.time = 0f;
    }

    private Song NextLinear(bool wrap)
    {
        int index = Songs.IndexOf(Current);
        if (index < 0) return Songs[0];
        if (index + 1 < Songs.Count) return Songs[index + 1];
        return wrap ? Songs[0] : null;
    }

    private Song NextShuffle(bool allowRefill)
    {
        shuffleQueue.RemoveAll(s => s == null || s == Current || !Songs.Contains(s));

        if (shuffleQueue.Count == 0)
        {
            if (!allowRefill) return null; // every song has been played once

            shuffleQueue.AddRange(Songs.Where(s => s != Current));

            // Only one song in the library.
            if (shuffleQueue.Count == 0)
                return Repeat == RepeatMode.All ? Current : null;
        }

        int i = UnityEngine.Random.Range(0, shuffleQueue.Count);
        Song pick = shuffleQueue[i];
        shuffleQueue.RemoveAt(i);
        return pick;
    }

    private void RebuildShuffleQueue()
    {
        shuffleQueue.Clear();
        if (!Shuffle) return;
        shuffleQueue.AddRange(Songs.Where(s => s != Current));
    }

    private void OnSongFinished()
    {
        if (Repeat == RepeatMode.One)
        {
            Source.Play(); // restart the same song
            ApplyStartPoint();
            return;
        }

        Next(true);
    }

    // ------------------------------------------------------------------ settings

    public void ChangeVolume(float delta)
    {
        Source.volume = Mathf.Clamp01(Mathf.Round((Source.volume + delta) * 100f) / 100f);
        Settings.Data.volume = Source.volume;
        Settings.Save();
        Changed?.Invoke();
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        RebuildShuffleQueue();
        Settings.Data.shuffle = Shuffle;
        Settings.Save();
        Say($"Shuffle: {(Shuffle ? "On" : "Off")}");
        Changed?.Invoke();
    }

    public void CycleRepeat()
    {
        Repeat = (RepeatMode)(((int)Repeat + 1) % 3);
        Settings.Data.repeat = (int)Repeat;
        Settings.Save();
        Say($"Repeat: {Repeat}");
        Changed?.Invoke();
    }

    private void Say(string message)
    {
        Toast?.Invoke(message);
    }
}
