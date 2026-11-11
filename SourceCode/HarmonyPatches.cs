using HarmonyLib;

namespace GorillaJukebox;

internal static class HarmonyPatches
{
    /// <summary>
    /// When you join a room the game normally resets the time of day and weather.
    /// While "enabled" is true we skip that reset so the time/weather you picked in the
    /// menu stays. The Time screen's "Default" button turns this back off.
    /// </summary>
    [HarmonyPatch(typeof(BetterDayNightManager), "OnRoomJoin")]
    public static class DayNightManagerPatch
    {
        public static bool enabled;

        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !enabled;
        }
    }

    private static Harmony _harmony;

    private static Harmony HarmonyInstance
    {
        get
        {
            if (_harmony == null) _harmony = new Harmony(Constants.Guid);
            return _harmony;
        }
    }

    public static void Patch() => HarmonyInstance.PatchAll();

    public static void Unpatch() => HarmonyInstance.UnpatchSelf();
}
