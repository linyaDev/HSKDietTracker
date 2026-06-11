using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HSKDietTracker;

/// <summary>
/// Cached diet-filter state: per-colonist "eaten in the last 7 days" sets, and the current
/// "top 3" ingredient keys — the 3 ingredients available on the map that the fewest colonists
/// have eaten (i.e. the best for diet variety). Refreshed periodically so the bill ingredient
/// filter stays cheap.
/// </summary>
internal static class DietFilterState
{
    private static readonly List<HashSet<string>> colonistEaten = new List<HashSet<string>>();
    private static HashSet<string> top3 = new HashSet<string>();
    private static int lastTick = -999999;
    private const int Interval = 200; // ~5 in-game minutes
    private const int TopN = 3;

    private static void Ensure()
    {
        int tick = Find.TickManager?.TicksGame ?? 0;
        if (lastTick >= 0 && tick - lastTick < Interval)
            return;
        lastTick = tick;

        colonistEaten.Clear();
        top3 = new HashSet<string>();

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        foreach (var p in PawnsFinder.AllMaps_FreeColonists)
        {
            if (p == null || p.IsQuestLodger())
                continue;
            colonistEaten.Add(comp.GetData(p).RecentEatenSet);
        }

        // Collect ingredient keys available to cooks on the map.
        var available = new HashSet<string>();
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
                foreach (var key in IngredientKeys(thing))
                    available.Add(key);
            }
        }

        // The 3 available ingredients eaten by the fewest colonists (ties broken by name).
        top3 = available
            .OrderBy(EatenByCount)
            .ThenBy(k => k)
            .Take(TopN)
            .ToHashSet();
    }

    private static int EatenByCount(string key)
    {
        int n = 0;
        foreach (var set in colonistEaten)
        {
            if (set.Contains(key))
                n++;
        }
        return n;
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
        Ensure();
        return key != null && top3.Contains(key);
    }

    /// <summary>
    /// Diagnostic: the current top-3 variety ingredients, each with the number of colonists
    /// that have eaten it. Empty when no game is loaded or no raw ingredients are on the map.
    /// </summary>
    public static string DebugTop3Summary()
    {
        Ensure();
        if (top3.Count == 0)
            return "(none)";
        return string.Join(", ", top3
            .OrderBy(EatenByCount)
            .ThenBy(k => k)
            .Select(k => k + " (eaten by " + EatenByCount(k) + ")"));
    }

    /// <summary>True if this stack provides one of the current top-3 variety ingredients.</summary>
    public static bool ProvidesTop3(Thing t)
    {
        if (t?.def == null)
            return false;
        Ensure();
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
    public override bool Matches(Thing t)
    {
        if (t?.def == null || !DietFilterState.IsRawIngredient(t.def))
            return false;
        return !DietFilterState.ProvidesTop3(t);
    }

    public override bool CanEverMatch(ThingDef def) => def?.IsNutritionGivingIngestible == true;
}
