using System;
using System.Linq;
using System.Reflection;
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
        PatchRimHUD(harmony);
    }

    /// <summary>
    /// RimHUD draws its own inspect pane buttons, bypassing MainTabWindow_Inspect.
    /// Patch its InspectPaneButtons.Draw via reflection so there is no hard dependency.
    /// </summary>
    private static void PatchRimHUD(Harmony harmony)
    {
        try
        {
            var mod = LoadedModManager.RunningMods.FirstOrDefault(m => m.PackageId == "jaxe.rimhud");
            if (mod == null)
                return;

            var asm = mod.assemblies?.loadedAssemblies?.FirstOrDefault(a => a.GetName().Name == "RimHUD");
            var method = asm?.GetType("RimHUD.Interface.Screen.InspectPaneButtons")
                ?.GetMethod("Draw", BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                Log.Warning("[HSKDietTracker] RimHUD detected but InspectPaneButtons.Draw not found — diet button won't show on its pane");
                return;
            }

            harmony.Patch(method,
                postfix: new HarmonyMethod(typeof(Patch_RimHUDInspectPaneButtons), nameof(Patch_RimHUDInspectPaneButtons.Postfix)));
        }
        catch (Exception e)
        {
            Log.Warning("[HSKDietTracker] RimHUD integration failed: " + e.Message);
        }
    }
}
