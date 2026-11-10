Gorilla Jukebox

A BepInEx mod for Gorilla Tag (PC / Steam) that plays your own music files through an in-game menu. The menu is built in code, so there is no Unity asset bundle. Music plays locally: other players can't hear it.

Works on Windows, and on Linux through Proton.

Written with Claude (Anthropic) and tested in game by the repo owner. Unofficial, and not affiliated with Another Axiom. Use it at your own risk.

Features
Plays .mp3, .wav, .ogg, .aiff and .aif files
Menu above your left hand: Prev / Play-Pause / Next, volume, paged song list, refresh
Progress bar with elapsed and total time
Jump to a spot: touch the progress bar and confirm. Nothing moves until you press "Yes, jump", and you can fine-tune the spot with -5s / +5s
Song start points: pick a song and choose where it starts. It starts there every time it plays
Shuffle: plays every song once in random order before reshuffling. Previous steps back through what you actually heard
Repeat: Off, All (wraps around) or One
Moves on by itself when a song ends
Live equaliser while playing
9 colour themes, optional room code display
Room screen: join a public room, disconnect, see the room code
Time & weather screen: morning / day / evening / night, toggle rain (only changes what you see)
Volume, theme, shuffle, repeat, last song and start points are saved between sessions
Songs are loaded one at a time when played, so a big library doesn't use lots of memory
Controls
Action	Input
Open / close the menu	Left primary button (X)
Press a button	Touch it with your right index fingertip (a small white dot shows the exact press point)
Pause / play	Right joystick click (works even with the menu closed)
Install

You need BepInEx for Gorilla Tag first.

Windows
Install BepInEx (64-bit, 5.x) into your Gorilla Tag folder and run the game once so it creates its folders. The default location is C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag. In Steam: right-click Gorilla Tag, then Manage, then Browse local files.
Download GorillaJukebox.dll from the Releases page.
Put it in Gorilla Tag\BepInEx\plugins.
Start the game. The mod creates Gorilla Tag\GorillaJukebox\Music the first time it loads.
Put your songs in that Music folder. In the menu, press Open Folder (Windows) and then Refresh.
Linux (Steam + Proton)

Gorilla Tag is a Windows game, so on Linux it runs through Proton. The mod is the same DLL.

Install BepInEx (the Windows 64-bit build) into the game folder:
Native Steam: ~/.local/share/Steam/steamapps/common/Gorilla Tag
Flatpak Steam: ~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Gorilla Tag
In Steam, open Gorilla Tag's properties and set the launch option:
   WINEDLLOVERRIDES="winhttp=n,b" %command%

Run the game once so BepInEx creates its folders. 3. Download GorillaJukebox.dll and put it in Gorilla Tag/BepInEx/plugins. 4. Start the game. The mod creates Gorilla Tag/GorillaJukebox/Music the first time it loads. 5. Copy your songs into that Music folder with your file manager, then press Refresh in the menu. (The Open Folder button only works on Windows.)

Using the menu
Main screen: Prev / Play / Next, Vol -/+, Shuffle, Repeat, Songs (the full list), Refresh (rescan the Music folder), and the Settings, Room and Time screens.
Progress bar: hover over it to see a marker where you would land. Touch it, check the time shown, then press Yes, jump (or Cancel).
Settings > Song start points: choose a song, then use -5s / +5s to adjust or Use now to take the song's current position. Play previews it from the start point and Reset clears it.
Settings file

Saved to GorillaJukebox/settings.json next to the game. Edit it with the game closed.

Field	Meaning
volume	0 to 1
theme	Theme number (0 to 8)
showRoomCode	Show the room code on the Room screen
shuffle, repeat	Last shuffle / repeat setting (repeat: 0 = off, 1 = all, 2 = one)
lastSong	The song you were on
showFingerDot	Show the white press-point dot
fingerTip	Where your fingertip is relative to your right hand, in meters (default 0, -0.1, 0)
startNames, startTimes	Per-song start points: file names and seconds, matched by position
Presses fire too early or too late, or the dot isn't on your fingertip

Change fingerTip in settings.json and restart the game. Try Y values between -0.08 and -0.12. If the dot appears off to the side instead of out along your finger, the axis differs on your setup, so open an issue and say where the dot shows up.

Troubleshooting
No menu: open BepInEx/LogOutput.log and search for GorillaJukebox. You should see a line saying it is ready. If the log has nothing at all, BepInEx isn't loading. On Linux, check the launch option above.
"No songs found": the files must be in GorillaJukebox/Music next to the game, and be one of the supported types. Press Refresh after adding files.
A song won't play: check the log for a Failed to load line. Some unusual encodings of mp3 / ogg files won't load. Try converting it.
A start point stopped working: start points are saved by file name, so renaming the song file removes it.
Build it yourself

Anyone who prefers not to download the DLL can build it from the source here.

Install the .NET SDK, and BepInEx in the game folder (run the game once).
From the folder containing GorillaJukebox.csproj, run one of these (first build needs internet for the NuGet package): Windows:
   dotnet build -c Release -p:GamePath="C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag"

Linux:

   dotnet build -c Release -p:GamePath="$HOME/.local/share/Steam/steamapps/common/Gorilla Tag"
The DLL is at bin/Release/netstandard2.1/GorillaJukebox.dll. If BepInEx/plugins exists, it is also copied there.

The build compiles against the game's own DLLs (from Gorilla Tag_Data/Managed and BepInEx/core), which can't be included in this repo.

CS0012 "type is defined in an assembly that is not referenced": add a <Reference> for the DLL it names to the csproj, the same way the others are listed.
Warnings about Fusion.* not found: harmless, the code doesn't use them.
Missing member errors after a game update (for example GorillaTagger.leftHandTransform): the game changed. Open an issue with the exact error.
Files
Plugin.cs: BepInEx entry point
Jukebox.cs: audio, playlist, shuffle, repeat, start points, seeking
JukeboxMenu.cs: the menu and its screens
UiKit.cs: helpers that build the menu from generated rounded shapes and 3D text
Settings.cs: saved settings and folder paths
HarmonyPatches.cs: keeps your chosen time/weather when you join a room
Constants.cs: name, GUID, version
Notes
Joining rooms and changing time/weather call the game's own code. Only use it the way the game intends.
Music plays only on your end.
