using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKDietTracker;

/// <summary>
/// Fallback for the "monotonous food" bill filter. The filter worker has no bill
/// context, so its top-3 variety picks can be ingredients a recipe doesn't even
/// accept (soup wants vegetables, top-3 veg are fruits) — then every allowed
/// ingredient is cut as monotonous and the cook stands idle. Instead of guessing
/// per-category, retry the failed ingredient search once with the variety filter
/// bypassed: the recipe's own requirements stay in force, only our filter yields.
/// </summary>
[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
public static class Patch_VarietyFallback
{
    /// <summary>While true, SpecialThingFilterWorker_RecentlyEaten matches nothing.</summary>
    public static bool Bypass;

    private static bool retrying;

    private static readonly System.Reflection.MethodInfo TryFind =
        AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");

    private static SpecialThingFilterDef cachedFilterDef;
    private static SpecialThingFilterDef FilterDef
    {
        get
        {
            if (cachedFilterDef == null)
                cachedFilterDef = DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail("HSKDT_AllowRecentlyEaten");
            return cachedFilterDef;
        }
    }

    // While the search runs, the variety filter judges "monotonous" among what this bill accepts
    public static void Prefix(Bill bill)
    {
        DietFilterState.SetContextBill(bill);
    }

    // Runs even if the original throws — never leave a stale bill context behind
    public static void Finalizer()
    {
        DietFilterState.SetContextBill(null);
    }

    public static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver,
        List<ThingCount> chosen, List<IngredientCount> missingIngredients)
    {
        if (__result || retrying)
            return;
        if (!(HSKDietTrackerMod.Settings?.varietyFilterFallback ?? false))
            return;
        var filterDef = FilterDef;
        if (filterDef == null || bill?.ingredientFilter == null || bill.ingredientFilter.Allows(filterDef))
            return; // the variety filter isn't active on this bill

        retrying = true;
        Bypass = true;
        try
        {
            __result = (bool)TryFind.Invoke(null, new object[] { bill, pawn, billGiver, chosen, missingIngredients });
        }
        finally
        {
            Bypass = false;
            retrying = false;
        }

        if (__result && Prefs.DevMode)
            Log.Message("[HSKDietTracker] VarietyFilter fallback: bill '" + bill.Label
                + "' found ingredients only after bypassing the variety filter.");
    }
}
