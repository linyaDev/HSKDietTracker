using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKDietTracker;

[HarmonyPatch(typeof(CompIngredients), nameof(CompIngredients.AllowStackWith))]
public static class Patch_MealStacking
{
    public static void Postfix(CompIngredients __instance, Thing otherStack, ref bool __result)
    {
        if (!__result)
            return;
        if (!(HSKDietTrackerMod.Settings?.preventMealStacking ?? false))
            return;

        var other = otherStack.TryGetComp<CompIngredients>();
        if (other == null)
            return;

        // Allow stacking if both have no ingredients
        if (__instance.ingredients.Count == 0 && other.ingredients.Count == 0)
            return;

        // Compare unique ingredient sets by defName (ignore duplicates)
        var setA = new HashSet<string>(__instance.ingredients.Select(i => i.defName));
        var setB = new HashSet<string>(other.ingredients.Select(i => i.defName));

        // Combined unique ingredients must fit in 3 slots
        var combined = new HashSet<string>(setA);
        combined.UnionWith(setB);
        if (combined.Count > 3)
        {
            if (Prefs.DevMode)
                Log.Message("[HSKDietTracker] NoStack: >3 combined | " + __instance.parent?.def?.defName
                    + " stackLimit=" + (__instance.parent?.def?.stackLimit ?? 0)
                    + " A(" + __instance.parent.stackCount + "): " + string.Join(", ", setA)
                    + " B(" + otherStack.stackCount + "): " + string.Join(", ", setB));
            __result = false;
            return;
        }

        // If ingredients fully match — always stack
        if (setA.SetEquals(setB))
            return;

        // Different ingredients — check stack size tolerance
        var def = __instance.parent?.def;
        bool isMeal = def?.ingestible != null && def.ingestible.preferability >= FoodPreferability.MealAwful;
        int tolerance = isMeal ? 1 : 10;

        if (System.Math.Abs(__instance.parent.stackCount - otherStack.stackCount) > tolerance)
        {
            if (Prefs.DevMode)
                Log.Message("[HSKDietTracker] NoStack: size diff | " + __instance.parent?.def?.defName
                    + " stackLimit=" + (__instance.parent?.def?.stackLimit ?? 0)
                    + " A(" + __instance.parent.stackCount + "): " + string.Join(", ", setA)
                    + " B(" + otherStack.stackCount + "): " + string.Join(", ", setB)
                    + " | tolerance=" + tolerance);
            __result = false;
        }
    }
}
