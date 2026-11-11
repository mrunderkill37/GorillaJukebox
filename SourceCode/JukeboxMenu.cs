using GorillaNetworking;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

namespace GorillaJukebox;

/// <summary>
/// The in-game menu. Press the left primary button (X) to open or close it. It floats above your
/// left hand and faces you. Press a button by touching it with the tip of your right index finger.
/// Right joystick click toggles pause even while the menu is closed.
/// </summary>
public class JukeboxMenu : MonoBehaviour
{
    private const float PanelW = 0.34f;
    private const float PanelH = 0.44f;
    private const float PanelRadius = 0.03f;
    private const float ButtonRadius = 0.016f;
    private const float MenuHeightAboveHand = 0.2f;
    private const int RowsPerPage = 5;

    // Depth layers, in meters from the panel face (negative = toward you).
    private const float HeaderZ = -0.002f;
    private const float ButtonZ = -0.003f;

    // The hand's origin sits in your palm; your fingertip is this far from it (in the hand's own axes,
    // -Y runs along the finger). Both values live in GorillaJukebox/settings.json ("fingerTip" and
    // "showFingerDot"), so you can tune them without rebuilding. Restart the game after editing.
    private static Vector3 FingerTipOffset => Settings.Data.fingerTip;
    private static bool ShowFingerDot => Settings.Data.showFingerDot;

    // Distances from the panel, in meters (negative = in front of it). Buttons light up once the fingertip is
    // closer than HoverFront, a press fires at TouchFront, and nothing counts more than TouchBack behind the panel.
    private const float HoverFront = -0.07f;
    private const float TouchFront = -0.01f;
    private const float TouchBack = 0.03f;

    private const int EqColumns = 8;
    private const int EqRows = 5;

    // Song progress bar on the main screen (also the touch area for "jump to").
    private const float ProgressY = 0.036f;
    private const float ProgressW = 0.17f;

    private class Theme
    {
        public string Name;
        public Color Back;
        public Color Button;

        public Theme(string name, Color back, Color button)
        {
            Name = name;
            Back = back;
            Button = button;
        }
    }

    private static readonly Theme[] Themes =
    {
        new Theme("Sky",      new Color(0.5f, 0.7f, 1f),   new Color(0.25f, 0.5f, 1f)),
        new Theme("Dark",     new Color(0.5f, 0.5f, 0.5f), new Color(0.2f, 0.2f, 0.2f)),
        new Theme("Green",    new Color(0.5f, 1f, 0.5f),   new Color(0f, 0.6f, 0f)),
        new Theme("Orange",   new Color(1f, 0.9f, 0.55f),  new Color(1f, 0.55f, 0f)),
        new Theme("Red",      new Color(1f, 0.45f, 0.45f), new Color(0.85f, 0f, 0f)),
        new Theme("Pink",     new Color(1f, 0.65f, 1f),    new Color(0.9f, 0f, 0.9f)),
        new Theme("Lavender", new Color(0.7f, 0.6f, 1f),   new Color(0.5f, 0.2f, 1f)),
        new Theme("Cyan",     new Color(0.4f, 1f, 1f),     new Color(0f, 0.6f, 0.9f)),
        new Theme("Purple",   new Color(0.5f, 0.4f, 1f),   new Color(0.4f, 0f, 1f)),
    };

    private Jukebox jukebox;
    private Transform root;
    private Plate panel;
    private GameObject fingerDot;
    private bool open;
    private int themeIndex;

    private float toggleCooldown;
    private float pressCooldown;
    private float joyCooldown;
    private bool joyWasDown;

    private readonly Dictionary<string, MenuScreen> screens = new Dictionary<string, MenuScreen>();
    private MenuScreen active;
    private readonly List<MenuButton> allButtons = new List<MenuButton>();
    private readonly List<Plate> headers = new List<Plate>();
    private readonly List<TextMeshPro> titleTexts = new List<TextMeshPro>();
    private readonly List<TextMeshPro> bodyTexts = new List<TextMeshPro>();

    // main screen
    private TextMeshPro songText;
    private TextMeshPro statusText;
    private MenuButton pauseButton;
    private MenuButton shuffleButton;
    private MenuButton repeatButton;
    private Plate eqScreen;
    private Plate progressTrack;
    private Transform progressFill;
    private TextMeshPro elapsedText;
    private TextMeshPro totalText;
    private int shownSecond = -1;
    private float shownLength = -1f;
    private MenuButton progressButton;
    private GameObject seekMarker;
    private Vector3 pressLocal;      // where the fingertip was (in menu space) when the last button was pressed
    private float pendingSeek;
    private TextMeshPro seekTimeText;
    private TextMeshPro seekOfText;

    // start-point screen
    private bool pickingStart;       // the song list is being used to choose a song for the start-point screen
    private string startPath;
    private TextMeshPro startSongText;
    private TextMeshPro startTimeText;
    private Material ledOnMaterial;
    private Material ledOffMaterial;
    private readonly Renderer[] leds = new Renderer[EqColumns * EqRows];
    private readonly bool[] ledLit = new bool[EqColumns * EqRows];
    private readonly float[] barValues = new float[EqColumns];
    private readonly float[] spectrum = new float[128];

    // song list screen
    private TextMeshPro listTitle;
    private readonly List<MenuButton> rowButtons = new List<MenuButton>();
    private int page;

    // settings + room screens
    private MenuButton themeButton;
    private MenuButton roomCodeButton;
    private TextMeshPro roomCodeText;
    private TextMeshPro roomStatusText;

    private string toast = "";
    private float toastUntil;

    // ------------------------------------------------------------------ setup

    private void Start()
    {
        jukebox = Jukebox.Instance;
        themeIndex = Mathf.Clamp(Settings.Data.theme, 0, Themes.Length - 1);

        Build();

        jukebox.Toast += OnToast;
        jukebox.Changed += OnJukeboxChanged;

        ApplyTheme();
        root.gameObject.SetActive(false);
        fingerDot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (jukebox == null) return;
        jukebox.Toast -= OnToast;
        jukebox.Changed -= OnJukeboxChanged;
    }

    private void Build()
    {
        GameObject rootGo = new GameObject("MenuRoot");
        rootGo.transform.SetParent(transform, false);
        root = rootGo.transform;

        // Framed panel: the rim is the border.
        panel = UiKit.MakePlate(root, "Panel", Vector3.zero, new Vector2(PanelW, PanelH), PanelRadius, 0.006f, 0f, Color.white);

        ledOnMaterial = UiKit.NewMaterial(Color.white);
        ledOffMaterial = UiKit.NewMaterial(Color.gray);
        fingerDot = UiKit.Dot(transform, "FingerDot", 0.012f, Color.white);

        BuildMain();
        BuildNoSongs();
        BuildSongList();
        BuildSettings();
        BuildRoom();
        BuildTime();
        BuildSeekConfirm();
        BuildStartPoint();
    }

    private MenuScreen NewScreen(string name)
    {
        GameObject go = new GameObject("Screen_" + name);
        go.transform.SetParent(root, false);
        go.SetActive(false);

        MenuScreen screen = new MenuScreen { Name = name, Root = go };
        screens[name] = screen;
        return screen;
    }

    private TextMeshPro AddText(MenuScreen s, string text, float x, float y, float w, float h, float size, bool title = false)
    {
        if (title)
        {
            // Coloured bar behind the heading.
            headers.Add(UiKit.MakePlate(s.Root.transform, "Header", new Vector3(x, y, HeaderZ),
                new Vector2(PanelW - 0.03f, 0.042f), 0.02f, 0.0015f, 0.001f, Color.gray));
        }

        TextMeshPro t = UiKit.Text(s.Root.transform, "Text", text, new Vector3(x, y, title ? -0.0045f : -0.004f),
            new Vector2(w, h), size, title ? Color.white : Color.black);
        if (title) t.fontStyle = FontStyles.Bold;

        (title ? titleTexts : bodyTexts).Add(t);
        return t;
    }

    private MenuButton AddButton(MenuScreen s, string text, float x, float y, float w, float h, Action onPress, float fontSize = 0.22f)
    {
        Plate plate = UiKit.MakePlate(s.Root.transform, "Button_" + text, new Vector3(x, y, ButtonZ),
            new Vector2(w, h), ButtonRadius, 0.002f, 0.0015f, Color.gray);

        TextMeshPro label = UiKit.Text(plate.Go.transform, "Label", text, new Vector3(0f, 0f, -0.0035f),
            new Vector2(w, h), fontSize, Color.white);

        MenuButton button = new MenuButton
        {
            Go = plate.Go,
            Plate = plate,
            Label = label,
            Center = new Vector2(x, y),
            Half = new Vector2(w / 2f, h / 2f),
            OnPress = onPress,
            ShownColor = Color.gray
        };

        s.Buttons.Add(button);
        allButtons.Add(button);
        return button;
    }

    // ------------------------------------------------------------------ screens

    private void BuildMain()
    {
        MenuScreen s = NewScreen("Main");

        AddText(s, "GORILLA JUKEBOX", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);
        songText = AddText(s, "", 0f, 0.141f, 0.31f, 0.04f, 0.24f);
        statusText = AddText(s, "", 0f, 0.106f, 0.31f, 0.03f, 0.17f);

        // Equaliser: a dark screen with columns of little rounded LEDs.
        const float eqY = 0.07f;
        eqScreen = UiKit.MakePlate(s.Root.transform, "EqScreen", new Vector3(0f, eqY, -0.0015f),
            new Vector2(0.29f, 0.034f), 0.012f, 0.0015f, 0f, Color.black);

        for (int i = 0; i < EqColumns; i++)
        {
            float x = (i - (EqColumns - 1) / 2f) * 0.035f;
            for (int j = 0; j < EqRows; j++)
            {
                float y = eqY + (j - (EqRows - 1) / 2f) * 0.0055f;
                leds[i * EqRows + j] = UiKit.Shape(s.Root.transform, "Led", new Vector3(x, y, -0.003f),
                    new Vector2(0.022f, 0.0045f), 0.00225f, ledOffMaterial);
            }
        }

        // Song progress: elapsed time, a bar that fills as the song plays, and the total length.
        const float barY = ProgressY;
        const float barW = ProgressW;
        elapsedText = AddText(s, "0:00", -0.125f, barY, 0.07f, 0.02f, 0.16f);
        totalText = AddText(s, "--:--", 0.125f, barY, 0.07f, 0.02f, 0.16f);

        progressTrack = UiKit.MakePlate(s.Root.transform, "ProgressTrack", new Vector3(0f, barY, -0.0015f),
            new Vector2(barW, 0.009f), 0.0045f, 0.001f, 0f, Color.black);

        // The fill is anchored on its left edge and stretched sideways as the song plays.
        GameObject pivot = new GameObject("ProgressFill");
        pivot.transform.SetParent(s.Root.transform, false);
        pivot.transform.localPosition = new Vector3(-barW / 2f + 0.002f, barY, -0.003f);
        UiKit.Shape(pivot.transform, "Fill", new Vector3((barW - 0.004f) / 2f, 0f, 0f),
            new Vector2(barW - 0.004f, 0.005f), 0.0025f, ledOnMaterial);
        progressFill = pivot.transform;

        // Touching the bar asks "jump here?". The touch area is taller than the bar so it is easy to hit.
        progressButton = new MenuButton
        {
            Go = progressTrack.Go,
            Plate = progressTrack,
            Center = new Vector2(0f, barY),
            Half = new Vector2(barW / 2f, 0.014f),
            OnPress = OnProgressPressed,
            KeepLook = true
        };
        s.Buttons.Add(progressButton);

        // Thin marker that shows where on the bar your fingertip would land.
        seekMarker = UiKit.Shape(s.Root.transform, "SeekMarker", new Vector3(0f, barY, -0.0045f),
            new Vector2(0.004f, 0.016f), 0.002f, UiKit.NewMaterial(Color.white)).gameObject;
        seekMarker.SetActive(false);

        AddButton(s, "Prev", -0.105f, -0.006f, 0.1f, 0.05f, () => jukebox.Previous());
        pauseButton = AddButton(s, "Play", 0f, -0.006f, 0.1f, 0.05f, () => jukebox.TogglePause());
        AddButton(s, "Next", 0.105f, -0.006f, 0.1f, 0.05f, () => jukebox.Next());

        AddButton(s, "Vol -", -0.125f, -0.064f, 0.06f, 0.05f, () => jukebox.ChangeVolume(-0.05f), 0.19f);
        shuffleButton = AddButton(s, "Shuffle", -0.045f, -0.064f, 0.08f, 0.05f, () => jukebox.ToggleShuffle(), 0.16f);
        repeatButton = AddButton(s, "Repeat", 0.045f, -0.064f, 0.08f, 0.05f, () => jukebox.CycleRepeat(), 0.16f);
        AddButton(s, "Vol +", 0.125f, -0.064f, 0.06f, 0.05f, () => jukebox.ChangeVolume(0.05f), 0.19f);

        AddButton(s, "Songs", -0.08f, -0.122f, 0.15f, 0.05f, () => { pickingStart = false; page = 0; Show("SongList"); });
        AddButton(s, "Refresh", 0.08f, -0.122f, 0.15f, 0.05f, () => jukebox.Refresh());

        AddButton(s, "Settings", -0.105f, -0.18f, 0.1f, 0.05f, () => Show("Settings"), 0.18f);
        AddButton(s, "Room", 0f, -0.18f, 0.1f, 0.05f, () => Show("Room"), 0.18f);
        AddButton(s, "Time", 0.105f, -0.18f, 0.1f, 0.05f, () => Show("Time"), 0.18f);
    }

    private void BuildNoSongs()
    {
        MenuScreen s = NewScreen("NoSongs");

        AddText(s, "NO SONGS FOUND", 0f, 0.15f, 0.32f, 0.03f, 0.28f, true);
        AddText(s, "Put .mp3 .wav .ogg or .aiff files in this folder, then press Refresh:\n\n" + Settings.MusicFolder,
            0f, 0.04f, 0.3f, 0.16f, 0.15f);

        AddButton(s, "Open Folder", 0f, -0.07f, 0.22f, 0.05f, OpenMusicFolder);
        AddButton(s, "Refresh", 0f, -0.13f, 0.22f, 0.05f, () => jukebox.Refresh());
    }

    private void BuildSongList()
    {
        MenuScreen s = NewScreen("SongList");
        s.OnShow = RefreshList;

        listTitle = AddText(s, "SONGS", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);

        for (int i = 0; i < RowsPerPage; i++)
        {
            int row = i;
            float y = 0.125f - i * 0.058f;
            rowButtons.Add(AddButton(s, "-", 0f, y, 0.31f, 0.05f, () => PlayRow(row), 0.2f));
        }

        AddButton(s, "< Page", -0.105f, -0.175f, 0.1f, 0.05f, () => { page--; RefreshList(); }, 0.18f);
        AddButton(s, "Back", 0f, -0.175f, 0.1f, 0.05f, () =>
        {
            if (pickingStart) { pickingStart = false; Show("StartPoint"); }
            else Show("Main");
        }, 0.18f);
        AddButton(s, "Page >", 0.105f, -0.175f, 0.1f, 0.05f, () => { page++; RefreshList(); }, 0.18f);
    }

    private void BuildSettings()
    {
        MenuScreen s = NewScreen("Settings");
        s.OnShow = RefreshSettingsLabels;

        AddText(s, "SETTINGS", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);
        AddButton(s, "Song start points", 0f, -0.07f, 0.3f, 0.06f, OpenStartPoint, 0.2f);

        themeButton = AddButton(s, "Theme", 0f, 0.08f, 0.3f, 0.06f, NextTheme, 0.2f);
        roomCodeButton = AddButton(s, "Room code", 0f, 0.005f, 0.3f, 0.06f, () =>
        {
            Settings.Data.showRoomCode = !Settings.Data.showRoomCode;
            Settings.Save();
            RefreshSettingsLabels();
        }, 0.2f);

        AddButton(s, "Back", 0f, -0.17f, 0.12f, 0.05f, () => Show("Main"), 0.2f);
    }

    private void BuildRoom()
    {
        MenuScreen s = NewScreen("Room");

        AddText(s, "ROOM", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);
        roomCodeText = AddText(s, "----", 0f, 0.11f, 0.3f, 0.06f, 0.5f);
        roomStatusText = AddText(s, "", 0f, 0.06f, 0.3f, 0.03f, 0.17f);

        AddButton(s, "Join Public", 0f, -0.02f, 0.3f, 0.06f, JoinPublic, 0.2f);
        AddButton(s, "Disconnect", 0f, -0.09f, 0.3f, 0.06f, Disconnect, 0.2f);
        AddButton(s, "Back", 0f, -0.17f, 0.12f, 0.05f, () => Show("Main"), 0.2f);
    }

    private void BuildTime()
    {
        MenuScreen s = NewScreen("Time");

        AddText(s, "TIME & WEATHER", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);
        AddText(s, "Only changes what you see.", 0f, 0.145f, 0.32f, 0.03f, 0.15f);

        AddButton(s, "Morning", -0.08f, 0.09f, 0.15f, 0.055f, () => SetTime(1), 0.2f);
        AddButton(s, "Day", 0.08f, 0.09f, 0.15f, 0.055f, () => SetTime(3), 0.2f);
        AddButton(s, "Evening", -0.08f, 0.03f, 0.15f, 0.055f, () => SetTime(7), 0.2f);
        AddButton(s, "Night", 0.08f, 0.03f, 0.15f, 0.055f, () => SetTime(0), 0.2f);

        AddButton(s, "Toggle Rain", 0f, -0.04f, 0.3f, 0.055f, ToggleRain, 0.2f);
        AddButton(s, "Default", 0f, -0.1f, 0.3f, 0.055f, ResetTime, 0.2f);
        AddButton(s, "Back", 0f, -0.17f, 0.12f, 0.05f, () => Show("Main"), 0.2f);
    }

    private void BuildSeekConfirm()
    {
        MenuScreen s = NewScreen("SeekConfirm");
        s.OnShow = RefreshSeekLabels;

        AddText(s, "JUMP TO?", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);
        seekTimeText = AddText(s, "0:00", 0f, 0.115f, 0.3f, 0.07f, 0.5f);
        seekOfText = AddText(s, "", 0f, 0.065f, 0.3f, 0.03f, 0.17f);

        AddButton(s, "-5s", -0.105f, 0f, 0.1f, 0.05f, () => NudgeSeek(-5f), 0.2f);
        AddButton(s, "+5s", 0.105f, 0f, 0.1f, 0.05f, () => NudgeSeek(5f), 0.2f);

        AddButton(s, "Yes, jump", 0f, -0.07f, 0.3f, 0.06f, ConfirmSeek, 0.2f);
        AddButton(s, "Cancel", 0f, -0.14f, 0.3f, 0.06f, () => Show("Main"), 0.2f);
    }

    private void BuildStartPoint()
    {
        MenuScreen s = NewScreen("StartPoint");
        s.OnShow = RefreshStartLabels;

        AddText(s, "START POINT", 0f, 0.185f, 0.32f, 0.03f, 0.26f, true);
        startSongText = AddText(s, "", 0f, 0.13f, 0.31f, 0.04f, 0.22f);
        AddButton(s, "Choose song", 0f, 0.075f, 0.3f, 0.05f, () => { pickingStart = true; page = 0; Show("SongList"); }, 0.2f);
        startTimeText = AddText(s, "", 0f, 0.025f, 0.31f, 0.04f, 0.2f);

        AddButton(s, "-5s", -0.11f, -0.04f, 0.09f, 0.05f, () => AdjustStart(-5f), 0.2f);
        AddButton(s, "Use now", 0f, -0.04f, 0.11f, 0.05f, UseNowAsStart, 0.15f);
        AddButton(s, "+5s", 0.11f, -0.04f, 0.09f, 0.05f, () => AdjustStart(5f), 0.2f);

        AddButton(s, "Play", -0.08f, -0.1f, 0.15f, 0.05f, PlayStartSong, 0.2f);
        AddButton(s, "Reset", 0.08f, -0.1f, 0.15f, 0.05f, ResetStart, 0.2f);

        AddButton(s, "Back", 0f, -0.17f, 0.12f, 0.05f, () => Show("Settings"), 0.2f);
    }

    // ------------------------------------------------------------------ jump to a spot in the song

    private void OnProgressPressed()
    {
        AudioClip clip = jukebox.Source.clip;
        if (clip == null || jukebox.IsLoading)
        {
            OnToast("Play a song first");
            return;
        }

        // Where along the bar was the fingertip? Nothing happens until you confirm.
        float fraction = Mathf.Clamp01((pressLocal.x + ProgressW / 2f) / ProgressW);
        pendingSeek = Mathf.Round(fraction * clip.length);
        Show("SeekConfirm");
    }

    private void NudgeSeek(float delta)
    {
        AudioClip clip = jukebox.Source.clip;
        float length = clip != null ? clip.length : 0f;
        pendingSeek = Mathf.Clamp(pendingSeek + delta, 0f, length);
        RefreshSeekLabels();
    }

    private void ConfirmSeek()
    {
        jukebox.Seek(pendingSeek);
        Show("Main");
    }

    private void RefreshSeekLabels()
    {
        AudioClip clip = jukebox.Source.clip;
        SetText(seekTimeText, FormatTime(pendingSeek));
        SetText(seekOfText, clip != null ? "of " + FormatTime(clip.length) : "");
    }

    // Shows a thin marker on the bar where your fingertip would land.
    private void UpdateSeekMarker(Vector3 local)
    {
        if (seekMarker == null || progressButton == null) return;

        bool show = active != null && active.Name == "Main" && IsOver(progressButton, local, HoverFront);
        if (seekMarker.activeSelf != show) seekMarker.SetActive(show);
        if (!show) return;

        float limit = ProgressW / 2f - 0.002f;
        Vector3 position = seekMarker.transform.localPosition;
        position.x = Mathf.Clamp(local.x, -limit, limit);
        seekMarker.transform.localPosition = position;
    }

    // ------------------------------------------------------------------ per-song start point

    private void OpenStartPoint()
    {
        startPath = jukebox.Current != null ? jukebox.Current.Path : null;
        Show("StartPoint");
    }

    private Song StartSong()
    {
        return startPath == null ? null : jukebox.Songs.Find(x => x.Path == startPath);
    }

    private void RefreshStartLabels()
    {
        Song song = StartSong();
        if (song == null && jukebox.Songs.Count > 0)
        {
            song = jukebox.Songs[0];
            startPath = song.Path;
        }

        SetText(startSongText, song == null ? "No songs" : UiKit.Trim(song.Name, 28));

        if (Time.time < toastUntil)
        {
            SetText(startTimeText, toast);
        }
        else if (song == null)
        {
            SetText(startTimeText, "");
        }
        else
        {
            float start = Settings.GetStart(song.Path);
            SetText(startTimeText, start >= 0.5f ? "Starts at <b>" + FormatTime(start) + "</b>" : "Starts at the beginning");
        }
    }

    private void AdjustStart(float delta)
    {
        Song song = StartSong();
        if (song == null) return;

        float value = Mathf.Max(0f, Settings.GetStart(song.Path) + delta);
        if (jukebox.Current == song && jukebox.Source.clip != null)
            value = Mathf.Min(value, Mathf.Max(0f, jukebox.Source.clip.length - 1f));

        Settings.SetStart(song.Path, value);
    }

    // Uses wherever the song is right now, so you can play it, wait for the good part, and set it.
    private void UseNowAsStart()
    {
        Song song = StartSong();
        if (song == null) return;

        if (jukebox.Current != song || jukebox.Source.clip == null)
        {
            OnToast("Play this song first");
            return;
        }

        Settings.SetStart(song.Path, jukebox.Source.time);
        OnToast("Start point saved");
    }

    private void PlayStartSong()
    {
        Song song = StartSong();
        if (song != null) jukebox.Play(song);
    }

    private void ResetStart()
    {
        Song song = StartSong();
        if (song == null) return;

        Settings.SetStart(song.Path, 0f);
        OnToast("Start point cleared");
    }

    // ------------------------------------------------------------------ per-frame

    private void Update()
    {
        if (ControllerInputPoller.instance == null || GorillaTagger.Instance == null) return;

        if (ControllerInputPoller.instance.leftControllerPrimaryButton && Time.time > toggleCooldown)
        {
            toggleCooldown = Time.time + 0.4f;
            SetOpen(!open);
        }

        // Right joystick click = pause / play, menu open or not.
        bool joyDown = GetJoystickClick();
        if (joyDown && !joyWasDown && Time.time > joyCooldown)
        {
            joyCooldown = Time.time + 0.5f;
            jukebox.TogglePause();
        }
        joyWasDown = joyDown;

        if (!open) return;

        FollowHand();
        HandlePresses();
        RefreshLive();
    }

    private void SetOpen(bool value)
    {
        open = value;
        root.gameObject.SetActive(open);
        if (fingerDot != null) fingerDot.SetActive(open && ShowFingerDot);
        if (!open) return;

        pickingStart = false;
        FollowHand();
        Show(jukebox.Songs.Count > 0 ? "Main" : "NoSongs");
    }

    private void FollowHand()
    {
        Transform hand = GorillaTagger.Instance.leftHandTransform;
        Camera cam = Camera.main;
        if (hand == null) return;

        Vector3 position = hand.position + Vector3.up * MenuHeightAboveHand;
        root.position = position;

        if (cam != null)
        {
            Vector3 away = position - cam.transform.position; // menu's front faces you
            if (away.sqrMagnitude > 0.0001f)
                root.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }

    private void HandlePresses()
    {
        Transform rightHand = GorillaTagger.Instance.rightHandTransform;
        if (rightHand == null || active == null) return;

        // Work from the fingertip, not the hand's origin.
        Vector3 tip = rightHand.TransformPoint(FingerTipOffset);
        if (fingerDot != null) fingerDot.transform.position = tip;

        Vector3 local = root.InverseTransformPoint(tip);
        MenuButton pressed = null;

        foreach (MenuButton b in active.Buttons)
        {
            if (!b.Go.activeSelf) continue;

            bool touching = IsOver(b, local, TouchFront);
            if (touching && !b.Inside && Time.time > pressCooldown)
                pressed = b;

            b.Inside = touching;
            UpdateButtonLook(b, IsOver(b, local, HoverFront));
        }

        UpdateSeekMarker(local);

        if (pressed != null)
        {
            pressLocal = local;
            pressCooldown = Time.time + 0.25f;
            pressed.FlashUntil = Time.time + 0.15f;
            GorillaTagger.Instance.StartVibration(false, 0.2f, 0.05f);

            try { pressed.OnPress?.Invoke(); }
            catch (Exception e) { Debug.LogError($"[{Constants.Name}] Button error: {e}"); }
        }
    }

    // Is the fingertip over this button and between zFront and TouchBack (in meters from the panel)?
    private static bool IsOver(MenuButton b, Vector3 local, float zFront)
    {
        return Mathf.Abs(local.x - b.Center.x) < b.Half.x
            && Mathf.Abs(local.y - b.Center.y) < b.Half.y
            && local.z > zFront && local.z < TouchBack;
    }

    private void UpdateButtonLook(MenuButton b, bool hovering)
    {
        if (b.KeepLook) return;

        Color baseColor = Themes[themeIndex].Button;
        bool pressed = Time.time < b.FlashUntil;

        Color target = pressed ? Color.Lerp(baseColor, Color.white, 0.7f)
                     : hovering ? Color.Lerp(baseColor, Color.white, 0.35f)
                     : baseColor;

        if (target != b.ShownColor)
        {
            b.ShownColor = target;
            b.Plate.SetColor(target);
        }

        // Slide toward you while hovered, push back while pressed.
        float goal = pressed ? -0.002f : hovering ? 0.003f : 0f;
        b.Depth = Mathf.Lerp(b.Depth, goal, Time.deltaTime * 25f);

        Vector3 position = b.Go.transform.localPosition;
        position.z = ButtonZ - b.Depth;
        b.Go.transform.localPosition = position;
    }

    // After switching screens, treat buttons under the fingertip as already "touching" so one touch can't press two buttons.
    private void SyncInside(MenuScreen screen)
    {
        Transform rightHand = GorillaTagger.Instance != null ? GorillaTagger.Instance.rightHandTransform : null;
        Vector3 local = rightHand != null
            ? root.InverseTransformPoint(rightHand.TransformPoint(FingerTipOffset))
            : new Vector3(0f, 0f, 1f);

        foreach (MenuButton b in screen.Buttons)
            b.Inside = IsOver(b, local, TouchFront);

        pressCooldown = Mathf.Max(pressCooldown, Time.time + 0.25f);
    }

    private void Show(string name)
    {
        foreach (MenuScreen s in screens.Values) s.Root.SetActive(false);

        active = screens[name];
        active.Root.SetActive(true);
        active.OnShow?.Invoke();
        SyncInside(active);
    }

    // ------------------------------------------------------------------ live text

    private void RefreshLive()
    {
        if (active == null) return;

        if (active.Name == "Main")
        {
            string songLabel = jukebox.Current == null ? "No song"
                : jukebox.IsLoading ? "Loading..."
                : UiKit.Trim(jukebox.Current.Name, 28);
            SetText(songText, songLabel);

            bool playing = jukebox.Source.isPlaying;
            pauseButton.SetText(playing ? "Pause" : "Play");
            shuffleButton.SetText("Shuffle\n" + (jukebox.Shuffle ? "<b>On</b>" : "Off"));
            repeatButton.SetText("Repeat\n" + (jukebox.Repeat == RepeatMode.Off ? "Off" : $"<b>{jukebox.Repeat}</b>"));

            if (Time.time < toastUntil)
            {
                SetText(statusText, toast);
            }
            else
            {
                int index = jukebox.Songs.IndexOf(jukebox.Current) + 1;
                SetText(statusText, $"Vol {Mathf.RoundToInt(jukebox.Source.volume * 100f)}%   Track {index}/{jukebox.Songs.Count}");
            }

            UpdateBars(playing);
            UpdateProgress();
        }
        else if (active.Name == "Room")
        {
            bool inRoom = NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom;
            SetText(roomCodeText, inRoom && Settings.Data.showRoomCode ? NetworkSystem.Instance.RoomName : "----");
            SetText(roomStatusText, inRoom ? "Connected" : "Not in a room");
        }
        else if (active.Name == "StartPoint")
        {
            RefreshStartLabels();
        }
    }

    private static void SetText(TextMeshPro t, string value)
    {
        if (t.text != value) t.text = value;
    }

    private void UpdateProgress()
    {
        AudioClip clip = jukebox.Source.clip;
        float length = clip != null ? clip.length : 0f;
        float time = clip != null ? Mathf.Clamp(jukebox.Source.time, 0f, length) : 0f;

        // Only touch the text when it actually changes (once a second), so the menu stays cheap.
        int second = (int)time;
        if (second != shownSecond)
        {
            shownSecond = second;
            elapsedText.text = FormatTime(time);
        }

        if (Mathf.Abs(length - shownLength) > 0.01f)
        {
            shownLength = length;
            totalText.text = clip != null ? FormatTime(length) : "--:--";
        }

        float fraction = length > 0.01f ? time / length : 0f;
        progressFill.localScale = new Vector3(Mathf.Max(fraction, 0.001f), 1f, 1f);
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int hours = total / 3600;
        int minutes = (total % 3600) / 60;
        int secs = total % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{secs:00}" : $"{minutes}:{secs:00}";
    }

    private void UpdateBars(bool playing)
    {
        if (playing) jukebox.Source.GetSpectrumData(spectrum, 0, FFTWindow.Hanning);

        int perBar = spectrum.Length / (EqColumns * 4); // low frequencies carry most of the movement

        for (int i = 0; i < EqColumns; i++)
        {
            float target = 0f;
            if (playing)
            {
                float sum = 0f;
                int start = i * perBar;
                for (int j = start; j < start + perBar && j < spectrum.Length; j++) sum += spectrum[j];
                target = Mathf.Clamp01(Mathf.Sqrt(sum) * 2.5f);
            }

            barValues[i] = Mathf.Lerp(barValues[i], target, Time.deltaTime * 12f);

            // Light the bottom N LEDs of this column.
            int lit = Mathf.Clamp(Mathf.RoundToInt(barValues[i] * EqRows), 0, EqRows);
            for (int row = 0; row < EqRows; row++)
            {
                int index = i * EqRows + row;
                bool on = row < lit;
                if (ledLit[index] == on) continue;

                ledLit[index] = on;
                leds[index].sharedMaterial = on ? ledOnMaterial : ledOffMaterial;
            }
        }
    }

    private void OnToast(string message)
    {
        toast = message;
        toastUntil = Time.time + 2.5f;
    }

    private void OnJukeboxChanged()
    {
        if (!open || active == null) return;

        if (jukebox.Songs.Count == 0 && active.Name == "Main") Show("NoSongs");
        else if (jukebox.Songs.Count > 0 && active.Name == "NoSongs") Show("Main");
        else if (active.Name == "SongList") RefreshList();
    }

    // ------------------------------------------------------------------ song list

    private void RefreshList()
    {
        List<Song> songs = jukebox.Songs;
        int pages = Mathf.Max(1, Mathf.CeilToInt(songs.Count / (float)RowsPerPage));
        page = Mathf.Clamp(page, 0, pages - 1);
        listTitle.text = $"{(pickingStart ? "PICK SONG" : "SONGS")}  {page + 1}/{pages}";

        for (int i = 0; i < rowButtons.Count; i++)
        {
            int index = page * RowsPerPage + i;
            bool has = index < songs.Count;
            rowButtons[i].SetVisible(has);
            if (!has) continue;

            string prefix = songs[index] == jukebox.Current ? "> " : "";
            rowButtons[i].SetText(prefix + UiKit.Trim(songs[index].Name, 30));
        }
    }

    private void PlayRow(int row)
    {
        int index = page * RowsPerPage + row;
        if (index < 0 || index >= jukebox.Songs.Count) return;
        if (pickingStart)
        {
            // Choosing a song for the start-point screen instead of playing it.
            startPath = jukebox.Songs[index].Path;
            pickingStart = false;
            Show("StartPoint");
            return;
        }

        jukebox.Play(jukebox.Songs[index]);
        RefreshList();
    }

    // ------------------------------------------------------------------ settings

    private void NextTheme()
    {
        themeIndex = (themeIndex + 1) % Themes.Length;
        Settings.Data.theme = themeIndex;
        Settings.Save();
        ApplyTheme();
        RefreshSettingsLabels();
    }

    private void ApplyTheme()
    {
        Theme theme = Themes[themeIndex];

        panel.SetColors(theme.Back, UiKit.Darken(theme.Button, 0.6f));

        foreach (TextMeshPro t in titleTexts) t.color = Color.white;
        foreach (TextMeshPro t in bodyTexts) t.color = Color.black;
        foreach (Plate h in headers) h.SetColor(theme.Button);
        foreach (MenuButton b in allButtons)
        {
            b.ShownColor = theme.Button;
            b.Plate.SetColor(theme.Button);
        }

        // Equaliser: dark screen, dim LEDs, and a brighter shade of the theme colour for lit ones.
        eqScreen.SetColors(UiKit.Darken(theme.Button, 0.35f), UiKit.Darken(theme.Button, 0.6f));
        progressTrack.SetColors(UiKit.Darken(theme.Button, 0.35f), UiKit.Darken(theme.Button, 0.6f));
        UiKit.SetColor(ledOffMaterial, UiKit.Darken(theme.Button, 0.55f));
        UiKit.SetColor(ledOnMaterial, Color.Lerp(theme.Button, Color.white, 0.3f));
    }

    private void RefreshSettingsLabels()
    {
        themeButton.SetText($"Theme: {Themes[themeIndex].Name}");
        roomCodeButton.SetText($"Show room code: {(Settings.Data.showRoomCode ? "On" : "Off")}");
    }

    // ------------------------------------------------------------------ no-songs screen

    private static void OpenMusicFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Settings.MusicFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Settings.MusicFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"[{Constants.Name}] Could not open the folder: {e}");
        }
    }

    // ------------------------------------------------------------------ room

    private void JoinPublic()
    {
        try
        {
            if (NetworkSystem.Instance.InRoom)
            {
                NetworkSystem.Instance.ReturnToSinglePlayer();
                StartCoroutine(Delayed(1.5f, JoinPublicNow));
            }
            else
            {
                JoinPublicNow();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[{Constants.Name}] Join failed: {e}");
        }
    }

    private void JoinPublicNow()
    {
        try
        {
            var trigger = PhotonNetworkController.Instance.currentJoinTrigger;
            if (trigger == null) trigger = GorillaComputer.instance.GetJoinTriggerForZone("forest");
            if (trigger == null) return;
            PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger);
        }
        catch (Exception e)
        {
            Debug.LogError($"[{Constants.Name}] Join failed: {e}");
        }
    }

    private void Disconnect()
    {
        // Short delay so a stray touch doesn't instantly drop you from a room.
        StartCoroutine(Delayed(1.5f, () =>
        {
            try { NetworkSystem.Instance.ReturnToSinglePlayer(); }
            catch (Exception e) { Debug.LogError($"[{Constants.Name}] Disconnect failed: {e}"); }
        }));
    }

    private IEnumerator Delayed(float seconds, Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    // ------------------------------------------------------------------ time & weather

    private void SetTime(int index)
    {
        if (BetterDayNightManager.instance == null) return;
        BetterDayNightManager.instance.SetTimeOfDay(index);
        HarmonyPatches.DayNightManagerPatch.enabled = true;
    }

    private void ToggleRain()
    {
        BetterDayNightManager manager = BetterDayNightManager.instance;
        if (manager == null) return;

        bool makeRain = manager.CurrentWeather() == BetterDayNightManager.WeatherType.None;
        for (int i = 0; i < manager.weatherCycle.Length; i++)
        {
            manager.weatherCycle[i] = makeRain
                ? BetterDayNightManager.WeatherType.Raining
                : BetterDayNightManager.WeatherType.None;
        }
        HarmonyPatches.DayNightManagerPatch.enabled = true;
    }

    private void ResetTime()
    {
        BetterDayNightManager manager = BetterDayNightManager.instance;
        if (manager == null) return;

        manager.ClearTimeOfDay();
        manager.ClearFixedWeather();
        HarmonyPatches.DayNightManagerPatch.enabled = false;
    }

    // ------------------------------------------------------------------ input

    private static bool GetJoystickClick()
    {
        try
        {
            if (PlayFabAuthenticator.instance != null && PlayFabAuthenticator.instance.platform.PlatformTag == "Steam")
                return SteamVR_Actions.gorillaTag_RightJoystickClick.state;

            bool click;
            ControllerInputPoller.instance.rightControllerDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out click);
            return click;
        }
        catch
        {
            return false;
        }
    }
}
