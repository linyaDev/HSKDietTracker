using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HSKDietTracker;

/// <summary>
/// Cached diet-filter state: per-colonist "eaten in the last 7 days" sets, and "top 3"
/// ingredient keys — the 3 ingredients available on the map that the fewest colonists
/// have eaten (i.e. the best for diet variety). When a bill ingredient search is running,
/// the top-3 is computed only among ingredients that bill's filters accept (soup gets a
/// vegetable top-3, not fruits it can't use). Refreshed periodically to stay cheap.
/// </summary>
internal static class DietFilterState
{
    private static readonly List<(Pawn pawn, HashSet<string> eaten)> colonistEaten = new List<(Pawn, HashSet<string>)>();
    private static HashSet<string> top3Meat = new HashSet<string>();
    private static HashSet<string> top3Veg = new HashSet<string>();
    private static int lastEatenTick = -999999;
    private static int lastGlobalTick = -999999;
    private const int Interval = 200; // ~5 in-game minutes
    private const int TopN = 3;
    // Below this total stock an ingredient can't realistically be cooked into a meal,
    // so it shouldn't waste one of the variety slots.
    private const int MinCookableCount = 10;

    // ==== Bill context (set around WorkGiver_DoBill.TryFindBestBillIngredients) ====

    private static Bill contextBill;

    public static void SetContextBill(Bill bill) => contextBill = bill;

    private class BillTop3
    {
        public int tick = -999999;
        public HashSet<string> meat = new HashSet<string>();
        public HashSet<string> veg = new HashSet<string>();
        public string lastLogKey;
    }

    private static readonly Dictionary<Bill, BillTop3> billCache = new Dictionary<Bill, BillTop3>();

    private static void EnsureEaten()
    {
        int tick = Find.TickManager?.TicksGame ?? 0;
        if (lastEatenTick >= 0 && tick - lastEatenTick < Interval)
            return;
        lastEatenTick = tick;

        colonistEaten.Clear();
        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        foreach (var p in PawnsFinder.AllMaps_FreeColonists)
        {
            if (p == null || p.IsQuestLodger())
                continue;
            colonistEaten.Add((p, comp.GetData(p).RecentEatenSet));
        }
    }

    /// <summary>
    /// The "eaten by" sets that matter for a dish: only colonists whose food restriction
    /// allows the produced meal. Colonists who can't eat the dish must not influence
    /// which ingredients count as variety for it. Falls back to everyone.
    /// </summary>
    private static List<HashSet<string>> RelevantEatenSets(ThingDef producedDef)
    {
        var all = new List<HashSet<string>>();
        var restricted = producedDef == null ? null : new List<HashSet<string>>();
        foreach (var (pawn, eaten) in colonistEaten)
        {
            all.Add(eaten);
            if (restricted != null && (pawn.foodRestriction?.CurrentFoodPolicy?.Allows(producedDef) ?? true))
                restricted.Add(eaten);
        }
        return restricted != null && restricted.Count > 0 ? restricted : all;
    }

    /// <summary>
    /// Tally map stock of raw ingredients, meat and vegetables apart so each category
    /// gets its own variety slots. `allowed` restricts the tally (bill filters).
    /// </summary>
    private static void Tally(System.Predicate<ThingDef> allowed,
        Dictionary<string, int> meatCount, Dictionary<string, int> vegCount)
    {
        var maps = Find.Maps;
        for (int i = 0; i < maps.Count; i++)
        {
            var map = maps[i];
            if (map == null)
                continue;
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (!IsRawIngredient(thing.def))
                    continue;
                if (allowed != null && !allowed(thing.def))
                    continue;
                // Forbidden stock can't be cooked with, so it mustn't occupy a variety slot
                if (thing.IsForbidden(Faction.OfPlayer))
                    continue;
                var counts = thing.def.IsMeat ? meatCount : vegCount;
                foreach (var key in IngredientKeys(thing))
                {
                    counts.TryGetValue(key, out int c);
                    counts[key] = c + thing.stackCount;
                }
            }
        }
    }

    private static void EnsureGlobal()
    {
        int tick = Find.TickManager?.TicksGame ?? 0;
        if (lastGlobalTick >= 0 && tick - lastGlobalTick < Interval)
            return;
        lastGlobalTick = tick;

        EnsureEaten();
        top3Meat = new HashSet<string>();
        top3Veg = new HashSet<string>();

        var meatCount = new Dictionary<string, int>();
        var vegCount = new Dictionary<string, int>();
        Tally(null, meatCount, vegCount);

        var eatenSets = AllEatenSets();
        AddTopN(meatCount, top3Meat, eatenSets);
        AddTopN(vegCount, top3Veg, eatenSets);

        DebugLogState(meatCount, vegCount, eatenSets);
    }

    /// <summary>Top-3 restricted to what this bill's filters accept (def level).</summary>
    private static BillTop3 GetBillTop3(Bill bill)
    {
        int tick = Find.TickManager?.TicksGame ?? 0;
        if (!billCache.TryGetValue(bill, out var bt))
        {
            if (billCache.Count > 20)
                billCache.Clear(); // bills come and go; drop stale entries wholesale
            bt = new BillTop3();
            billCache[bill] = bt;
        }
        if (tick - bt.tick < Interval)
            return bt;
        bt.tick = tick;

        EnsureEaten();
        bt.meat.Clear();
        bt.veg.Clear();

        var fixedFilter = bill.recipe?.fixedIngredientFilter;
        var billFilter = bill.ingredientFilter;
        var meatCount = new Dictionary<string, int>();
        var vegCount = new Dictionary<string, int>();
        // Def-level Allows only — it doesn't consult special filter workers, no recursion
        Tally(def => (fixedFilter == null || fixedFilter.Allows(def))
                     && (billFilter == null || billFilter.Allows(def)),
            meatCount, vegCount);

        // Variety is judged by the colonists who are actually allowed to eat this dish
        var eatenSets = RelevantEatenSets(bill.recipe?.ProducedThingDef);
        AddTopN(meatCount, bt.meat, eatenSets);
        AddTopN(vegCount, bt.veg, eatenSets);

        if (Prefs.DevMode)
        {
            string key = "top3Meat=[" + string.Join(", ", bt.meat.OrderBy(k => k))
                + "] top3Veg=[" + string.Join(", ", bt.veg.OrderBy(k => k))
                + "] colonists=" + eatenSets.Count + "/" + colonistEaten.Count;
            if (key != bt.lastLogKey)
            {
                bt.lastLogKey = key;
                Log.Message("[HSKDietTracker] VarietyFilter bill '" + bill.Label + "': " + key);
            }
        }
        return bt;
    }

    private static string lastDebugKey;

    private static void DebugLogState(Dictionary<string, int> meatCount, Dictionary<string, int> vegCount, List<HashSet<string>> eatenSets)
    {
        if (!Prefs.DevMode)
            return;

        bool fb = HSKDietTrackerMod.Settings?.varietyFilterFallback ?? false;
        string key = "top3Meat=[" + string.Join(", ", top3Meat.OrderBy(k => k))
            + "] top3Veg=[" + string.Join(", ", top3Veg.OrderBy(k => k))
            + "] fallbackSetting=" + fb;
        if (key == lastDebugKey)
            return;
        lastDebugKey = key;

        string Stock(Dictionary<string, int> counts) => counts.Count == 0
            ? "(empty)"
            : string.Join(", ", counts
                .OrderBy(kv => EatenByCount(kv.Key, eatenSets)).ThenBy(kv => kv.Key)
                .Select(kv => kv.Key + " x" + kv.Value
                    + (kv.Value < MinCookableCount ? " (<" + MinCookableCount + ", skip)" : "")
                    + " eatenBy " + EatenByCount(kv.Key, eatenSets) + "/" + eatenSets.Count));

        Log.Message("[HSKDietTracker] VarietyFilter (global): " + key
            + "\n  meat stock: " + Stock(meatCount)
            + "\n  veg stock: " + Stock(vegCount));
    }

    private static void AddTopN(Dictionary<string, int> counts, HashSet<string> dest, List<HashSet<string>> eatenSets)
    {
        foreach (var key in counts
            .Where(kv => kv.Value >= MinCookableCount)
            .Select(kv => kv.Key)
            .OrderBy(k => EatenByCount(k, eatenSets))
            .ThenBy(k => k)
            .Take(TopN))
        {
            dest.Add(key);
        }
    }

    private static int EatenByCount(string key, List<HashSet<string>> eatenSets)
    {
        int n = 0;
        foreach (var set in eatenSets)
        {
            if (set.Contains(key))
                n++;
        }
        return n;
    }

    private static List<HashSet<string>> AllEatenSets()
    {
        var all = new List<HashSet<string>>(colonistEaten.Count);
        foreach (var (_, eaten) in colonistEaten)
            all.Add(eaten);
        return all;
    }

    public static bool IsRawIngredient(ThingDef def)
    {
        if (def?.ingestible == null || def.IsCorpse || def.IsDrug)
            return false;
        if (!def.ingestible.HumanEdible)
            return false;
        if ((def.ingestible.foodType & FoodTypeFlags.Kibble) != 0)
            return false;
        var pref = def.ingestible.preferability;
        if (pref <= FoodPreferability.NeverForNutrition || pref > FoodPreferability.RawTasty)
            return false;
        if (LuxurySlotLoader.AllLuxuryDefNames.Contains(def.defName))
            return false;
        return true;
    }

    /// <summary>The variety keys a stack contributes: source animals for meat, else the def.</summary>
    private static IEnumerable<string> IngredientKeys(Thing t)
    {
        var def = t.def;
        if (def.IsMeat)
        {
            var ci = t.TryGetComp<CompIngredients>();
            if (ci?.ingredients != null)
            {
                foreach (var src in ci.ingredients)
                {
                    if (src?.race != null && !IgnoredLoader.Ignored.Contains(src.defName))
                        yield return src.defName;
                }
            }
        }
        else if (!IgnoredLoader.Ignored.Contains(def.defName))
        {
            yield return def.defName;
        }
    }

    /// <summary>True if this ingredient key is one of the current top-3 variety ingredients.</summary>
    public static bool IsTop3Key(string key)
    {
        EnsureGlobal();
        return key != null && (top3Meat.Contains(key) || top3Veg.Contains(key));
    }

    /// <summary>
    /// Diagnostic: the current top-3 variety ingredients, each with the number of colonists
    /// that have eaten it. Empty when no game is loaded or no raw ingredients are on the map.
    /// </summary>
    public static string DebugTop3Summary()
    {
        EnsureGlobal();
        if (top3Meat.Count == 0 && top3Veg.Count == 0)
            return "(none)";
        var eatenSets = AllEatenSets();
        return string.Join(", ", top3Meat.Concat(top3Veg)
            .OrderBy(k => EatenByCount(k, eatenSets))
            .ThenBy(k => k)
            .Select(k => k + " (eaten by " + EatenByCount(k, eatenSets) + ")"));
    }

    /// <summary>True if this stack provides one of the current top-3 variety ingredients.</summary>
    public static bool ProvidesTop3(Thing t)
    {
        if (t?.def == null)
            return false;

        HashSet<string> top3;
        if (contextBill != null)
        {
            // Inside a bill ingredient search: variety is judged among what THIS bill accepts
            var bt = GetBillTop3(contextBill);
            top3 = t.def.IsMeat ? bt.meat : bt.veg;
        }
        else
        {
            EnsureGlobal();
            top3 = t.def.IsMeat ? top3Meat : top3Veg;
        }

        if (top3.Count == 0)
            return false;
        foreach (var key in IngredientKeys(t))
        {
            if (top3.Contains(key))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Bill filter "allow monotonous food": matches everything except the top-3 varied ingredients.
/// Uncheck to cook ONLY from the 3 most variety-adding ingredients (available on the map).
/// </summary>
public class SpecialThingFilterWorker_RecentlyEaten : SpecialThingFilterWorker
{
    private static int lastAliveLogTick = -99999;

    public override bool Matches(Thing t)
    {
        // Heartbeat: proves bills actually consult this filter (only unchecked filters are asked)
        if (Prefs.DevMode)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick - lastAliveLogTick > 2500)
            {
                lastAliveLogTick = tick;
                Log.Message("[HSKDietTracker] VarietyFilter.Matches is being consulted (thing=" + t?.def?.defName + ")");
            }
        }

        // Retry pass after a failed ingredient search — the variety filter yields
        if (Patch_VarietyFallback.Bypass)
            return false;

        if (t?.def == null || !DietFilterState.IsRawIngredient(t.def))
            return false;
        return !DietFilterState.ProvidesTop3(t);
    }

    public override bool CanEverMatch(ThingDef def) => def?.IsNutritionGivingIngestible == true;
}
