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

        // Luxury items are tracked by slots, not meal history (they never become "eaten"
        // there, which used to give them a permanent +15). Encourage them only while the
        // pawn's slot for that category is still empty; otherwise leave vanilla optimality.
        var techLevel = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Neolithic;
        bool luxuryEnabled = (HSKDietTrackerMod.Settings?.luxuryEnabled ?? true) && techLevel > TechLevel.Neolithic;
        if (luxuryEnabled)
        {
            if (!LuxurySlotLoader.DefToCategory.TryGetValue(foodDef.defName, out string luxCategory))
                luxCategory = LuxurySlotLoader.DetectCategory(foodDef);
            if (luxCategory != null)
            {
                // Drugs (alcohol, psychite tea, smokes) are consumed via drug policy / joy,
                // not hunger — boosting them here would fight the drug policy and push
                // hungry pawns toward addiction. Only edible non-drug luxuries get the nudge.
                if (!foodDef.IsDrug)
                {
                    var cat = LuxurySlotLoader.Categories.Find(c => c.name == luxCategory);
                    int slots = cat?.slots ?? 1;
                    var filledItems = data.FilledLuxuryItems(luxCategory);
                    if (filledItems.Count < slots && !filledItems.Contains(foodDef.defName))
                        __result += 15f;
                }
                return;
            }
        }

        // Prefer food not eaten recently
        if (data.HasEatenMeal(foodDef.defName))
            __result -= 30f;
        else
            __result += 15f;
    }
}
