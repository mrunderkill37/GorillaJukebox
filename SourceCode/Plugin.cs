using BepInEx;
using System.Collections;
using UnityEngine;

namespace GorillaJukebox;

[BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        Settings.Load();
        HarmonyPatches.Patch();
    }

    private void Start()
    {
        StartCoroutine(WaitForGame());
    }

    // The menu needs the player's hands and controller input, so wait until they exist.
    private IEnumerator WaitForGame()
    {
        while (GorillaTagger.Instance == null || ControllerInputPoller.instance == null)
            yield return null;

        yield return new WaitForSeconds(1f);

        GameObject go = new GameObject(Constants.Name);
        DontDestroyOnLoad(go);
        go.AddComponent<Jukebox>();     // audio + playlist logic (must come first)
        go.AddComponent<JukeboxMenu>(); // the in-game menu

        Logger.LogInfo($"{Constants.Name} {Constants.Version} ready. Press the left primary button (X) to open the menu.");
    }

    private void OnApplicationQuit()
    {
        Settings.Save();
    }
}
