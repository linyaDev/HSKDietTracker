using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKDietTracker;

[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet_NoMix")]
public static class Patch_CookingSpoilage
{
    private static List<HashSet<string>> cachedEaten;
    private static int cachedTick = -99999;
    private const int CacheInterval = 500;

    // Cooking history — tracks how many times each ingredient was cooked, resets each day
    private static Dictionary<string, int> cookingHistory = new Dictionary<string, int>();
    private static int lastDayReset = -1;

    private static void RefreshEaten()
    {
        int tick = Find.TickManager?.TicksGame ?? 0;
        if (cachedEaten != null && tick - cachedTick < CacheInterval)
            return;
        cachedTick = tick;

        cachedEaten = new List<HashSet<string>>();
        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        foreach (var p in PawnsFinder.AllMaps_FreeColonists)
        {
            if (p == null || p.IsQuestLodger())
                continue;
            cachedEaten.Add(comp.GetData(p).RecentEatenSet);
        }
    }

    private static int EatenByCount(string defName)
    {
        if (cachedEaten == null)
            return 0;
        int n = 0;
        for (int i = 0; i < cachedEaten.Count; i++)
        {
            if (cachedEaten[i].Contains(defName))
                n++;
        }
        return n;
    }

    private static void CheckDayReset()
    {
        int day = GenDate.DaysPassed;
        if (day != lastDayReset)
        {
            lastDayReset = day;
            cookingHistory.Clear();
        }
    }

    public static void RecordCooked(List<ThingCount> chosen)
    {
        CheckDayReset();
        foreach (var tc in chosen)
        {
            string defName = tc.Thing?.def?.defName;
            if (defName == null) continue;
            cookingHistory.TryGetValue(defName, out int c);
            cookingHistory[defName] = c + 1;
        }
    }

    private static int CookedCount(string defName)
    {
        cookingHistory.TryGetValue(defName, out int c);
        return c;
    }

    public static void Prefix(List<Thing> availableThings, Bill bill, IntVec3 rootCell, ref bool alreadySorted)
    {
        if (!(HSKDietTrackerMod.Settings?.preventMealStacking ?? true))
            return;
        if (bill?.recipe?.workSkill != SkillDefOf.Cooking)
            return;
        if (bill.recipe.ProducedThingDef?.ingestible == null)
            return;

        RefreshEaten();
        CheckDayReset();
        int colonistCount = cachedEaten?.Count ?? 1;
        if (colonistCount < 1) colonistCount = 1;

        availableThings.Sort((t1, t2) =>
        {
            float d1 = (t1.Position - rootCell).LengthHorizontalSquared;
            float d2 = (t2.Position - rootCell).LengthHorizontalSquared;

            // Prefer spoiling food
            var rot1 = t1.TryGetComp<CompRottable>();
            var rot2 = t2.TryGetComp<CompRottable>();
            if (rot1 != null && rot1.Stage == RotStage.Fresh)
                d1 += (1f - rot1.RotProgressPct) * 10f;
            if (rot2 != null && rot2.Stage == RotStage.Fresh)
                d2 += (1f - rot2.RotProgressPct) * 10f;

            // Prefer ingredients fewer colonists have eaten
            float eaten1 = (float)EatenByCount(t1.def.defName) / colonistCount;
            float eaten2 = (float)EatenByCount(t2.def.defName) / colonistCount;
            d1 += eaten1 * 400f;
            d2 += eaten2 * 400f;

            // Penalize recently cooked ingredients
            d1 += CookedCount(t1.def.defName) * 100f;
            d2 += CookedCount(t2.def.defName) * 100f;

            return d1.CompareTo(d2);
        });

        alreadySorted = true;
    }

    // Record chosen ingredients after successful bill ingredient search
    public static void Postfix(bool __result, Bill bill, List<ThingCount> chosen)
    {
        if (!__result)
            return;
        if (!(HSKDietTrackerMod.Settings?.preventMealStacking ?? true))
            return;
        if (bill?.recipe?.workSkill != SkillDefOf.Cooking)
            return;
        if (bill.recipe.ProducedThingDef?.ingestible == null)
            return;

        RecordCooked(chosen);
    }
}
