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
        if (!(HSKDietTrackerMod.Settings?.preventMealStacking ?? true))
            return;

        var other = otherStack.TryGetComp<CompIngredients>();
        if (other == null)
            return;

        // Allow stacking if both have no ingredients
        if (__instance.ingredients.Count == 0 && other.ingredients.Count == 0)
            return;

        // Different ingredient count → don't stack
        if (__instance.ingredients.Count != other.ingredients.Count)
        {
            __result = false;
            return;
        }

        // Same count but different ingredients → don't stack
        for (int i = 0; i < __instance.ingredients.Count; i++)
        {
            if (!other.ingredients.Contains(__instance.ingredients[i]))
            {
                __result = false;
                return;
            }
        }
    }
}
