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

        var data = comp.GetData(eater);

        // Prefer food not eaten recently
        if (data.HasEatenMeal(foodDef.defName))
            __result -= 30f;
        else
            __result += 15f;

        // Prefer food about to spoil
        if (foodSource != null)
        {
            var rottable = foodSource.TryGetComp<CompRottable>();
            if (rottable != null)
            {
                float rotProgress = rottable.RotProgress / rottable.PropsRot.TicksToRotStart;
                if (rotProgress > 0.5f)
                    __result += 20f * rotProgress;
            }
        }
    }
}
