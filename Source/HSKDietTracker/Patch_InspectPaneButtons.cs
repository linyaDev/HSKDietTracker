using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HSKDietTracker;

/// <summary>
/// Adds a diet-history button to the inspect pane of a selected colonist.
/// Postfix (not a replacing prefix, unlike e.g. Useful Marks) so it stacks with
/// vanilla and other mods: buttons are laid right-to-left from rect.width - 48f
/// and lineEndWidth tracks the occupied width, so the next free slot starts at
/// rect.width - 48f - (lineEndWidth - 24f).
/// </summary>
[HarmonyPatch(typeof(MainTabWindow_Inspect), "DoInspectPaneButtons")]
public static class Patch_InspectPaneButtons
{
    private static Texture2D cachedIcon;
    private static Texture2D Icon
    {
        get
        {
            if (cachedIcon == null)
                cachedIcon = ThingDefOf.MealSimple?.uiIcon
                             ?? ContentFinder<Texture2D>.Get("UI/Buttons/InfoButton");
            return cachedIcon;
        }
    }

    public static bool CanShow(Pawn pawn)
    {
        return pawn != null && pawn.IsColonist && !pawn.IsQuestLodger();
    }

    public static void DrawButton(Pawn pawn, Rect btnRect)
    {
        MouseoverSounds.DoRegion(btnRect);
        TooltipHandler.TipRegion(btnRect, "DT_Title".Translate());
        if (Widgets.ButtonImage(btnRect, Icon))
        {
            Find.WindowStack.Add(new Dialog_DietInfo(pawn));
        }
    }

    public static void Postfix(Rect rect, ref float lineEndWidth)
    {
        if (Find.Selector.NumSelected != 1)
            return;
        if (!(Find.Selector.SingleSelectedThing is Pawn pawn) || !CanShow(pawn))
            return;

        float x = rect.width - 48f - lineEndWidth;
        DrawButton(pawn, new Rect(x + 2f, 1.5f, 20f, 20f));
        lineEndWidth += 24f;
    }
}

/// <summary>
/// RimHUD replaces the vanilla inspect pane, so the DoInspectPaneButtons postfix
/// never draws there. Applied manually (reflection) when RimHUD is loaded — see
/// HSKDietTrackerInit. Target: RimHUD.Interface.Screen.InspectPaneButtons.Draw.
/// </summary>
public static class Patch_RimHUDInspectPaneButtons
{
    public static void Postfix(Rect bounds, ref float offset)
    {
        if (!(Find.Selector.SingleSelectedThing is Pawn pawn) || !Patch_InspectPaneButtons.CanShow(pawn))
            return;

        offset += 20f;
        var btnRect = new Rect(bounds.xMax - offset, bounds.y + (bounds.height - 20f) / 2f, 20f, 20f);
        offset += 4f;
        Patch_InspectPaneButtons.DrawButton(pawn, btnRect);
    }
}
