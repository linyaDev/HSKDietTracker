using HarmonyLib;
using Verse;

namespace HSKDietTracker;

[StaticConstructorOnStartup]
public static class HSKDietTrackerInit
{
    static HSKDietTrackerInit()
    {
        var harmony = new Harmony("linya.hskdiettracker");
        harmony.PatchAll();
        Log.Message("[HSKDietTracker] Patches applied.");
    }
}
