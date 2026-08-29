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

    public static void Postfix(Rect rect, ref float lineEndWidth)
    {
        if (Find.Selector.NumSelected != 1)
            return;
        if (!(Find.Selector.SingleSelectedThing is Pawn pawn))
            return;
        if (!pawn.IsColonist || pawn.IsQuestLodger())
            return;

        float x = rect.width - 48f - lineEndWidth;
        var btnRect = new Rect(x + 2f, 1.5f, 20f, 20f);
        MouseoverSounds.DoRegion(btnRect);
        TooltipHandler.TipRegion(btnRect, "DT_Title".Translate());
        if (Widgets.ButtonImage(btnRect, Icon))
        {
            Find.WindowStack.Add(new Dialog_DietInfo(pawn));
        }
        lineEndWidth += 24f;
    }
}
