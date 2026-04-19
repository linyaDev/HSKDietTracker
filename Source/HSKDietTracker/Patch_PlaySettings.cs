using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
public static class Patch_PlaySettings
{
    private static Texture2D iconTexture;
    private static Texture2D Icon => iconTexture ??= ContentFinder<Texture2D>.Get("UI/Designators/Hunt", true);

    public static void Postfix(WidgetRow row, bool worldView)
    {
        if (worldView)
            return;

        row.ToggleableIcon(ref MapComponent_DietOverlay.Enabled, Icon,
            "DT_OverlayToggle".Translate(), SoundDefOf.Mouseover_ButtonToggle);
    }
}
