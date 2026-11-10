# Gorilla Jukebox

A BepInEx mod for Gorilla Tag (PC / Steam) that plays your own music files through an in-game menu.
Written from scratch. The menu is built in code, so there is no Unity asset bundle.

> **Status: untested.** This was written without a compiler or the game's DLLs, so expect to fix a
> few compile errors or in-game quirks on first build. See "If it doesn't build" below.

## Features
- Plays `.mp3`, `.wav`, `.ogg`, `.aiff` / `.aif` from `GorillaJukebox/Music` (next to the game's exe)
- Menu on your left hand with Prev / Play-Pause / Next, volume, song list (paged), refresh
- **Shuffle**: plays every song once in random order before reshuffling; Previous steps back through what you actually heard
- **Repeat**: Off, All (wraps around) or One
- Auto-advances when a song ends
- Live equaliser bars while playing
- Themes (9 colours), optional room-code display
- Room screen: join a public room, disconnect, see the room code
- Time & weather screen: morning / day / evening / night, toggle rain (local only, only affects what you see)
- Right joystick click = pause / play, even with the menu closed
- Volume, theme, shuffle, repeat and last song are saved to `GorillaJukebox/settings.json`
- Songs are loaded one at a time when played, not all at startup, so a big library doesn't eat memory

## Controls
| Action | Input |
|---|---|
| Open / close menu | Left primary button (X) |
| Press a menu button | Touch it with your right hand |
| Pause / play | Right joystick click |

## Build
1. Install BepInEx for Gorilla Tag and run the game once.
2. Open `GorillaJukebox.csproj` and set `GamePath` to your Gorilla Tag folder (or run
   `dotnet build -c Release -p:GamePath="D:\path\to\Gorilla Tag"`).
3. Build. If `BepInEx\plugins` exists, the DLL is copied there automatically.
4. Start the game, open the menu, press **Open Folder**, drop in some songs, press **Refresh**.

Needs the .NET SDK and internet access for the first build (it downloads the AssemblyPublicizer package).

## If it doesn't build
- **CS0012 "type is defined in an assembly that is not referenced"**: add a `<Reference>` for the DLL it names
  to the csproj, the same way the others are listed (look in `Gorilla Tag_Data\Managed`).
- **Missing member** (for example `GorillaTagger.leftHandTransform`): the game changes between updates.
  Tell me the exact error and I'll adjust the code.

## Files
- `Plugin.cs`: BepInEx entry point
- `Jukebox.cs`: audio, playlist, shuffle, repeat
- `JukeboxMenu.cs`: the menu and its screens
- `UiKit.cs`: helpers that build the menu from cubes and 3D text
- `Settings.cs`: saved settings and folder paths
- `HarmonyPatches.cs`: keeps your chosen time/weather when you join a room
- `Constants.cs`: name, GUID, version

## Notes
- Music plays locally. Other players can't hear it.
- Joining rooms and changing time/weather call the game's own code. Only use it the way the game intends.
