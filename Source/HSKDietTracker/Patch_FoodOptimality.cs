using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKDietTracker;

[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
public static class Patch_FoodOptimality
{
    public static void Postfix(ref float __result, Pawn eater, Thing foodSource, ThingDef foodDef)
    {
        if (eater == null || !eater.IsColonist || foodDef == null)
            return;

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        // Skip rotten food entirely
        if (foodSource != null)
        {
            var rottable = foodSource.TryGetComp<CompRottable>();
            if (rottable != null && rottable.Stage != RotStage.Fresh)
                return;

            // Prefer food about to spoil (max +12, so new food +15 still wins)
            if (rottable != null)
            {
                int ticksLeft = rottable.TicksUntilRotAtCurrentTemp;
                if (ticksLeft < 180000) // last 3 days
                    __result += 12f * (1f - (float)ticksLeft / 180000f);
            }
        }

        var data = comp.GetData(eater);

        // Prefer food not eaten recently
        if (data.HasEatenMeal(foodDef.defName))
            __result -= 30f;
        else
            __result += 15f;
    }
}
