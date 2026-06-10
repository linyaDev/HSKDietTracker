using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

/// <summary>
/// After the vanilla bill ingredient search picks ingredients, surgically swap a chosen
/// meat/vegetable for an equal-nutrition alternative that the colony has eaten less recently,
/// as long as that alternative is already among the gathered candidates (no extra travel).
/// This nudges cooks toward variety without overriding vanilla feasibility logic.
/// </summary>
public static class IngredientDiversifier
{
    // defName -> how many colonists ate it in the last 7 days (higher = staler = less desirable).
    private static readonly Dictionary<string, int> staleness = new Dictionary<string, int>();
    private static int lastRebuildTick = -999999;
    private const int RebuildInterval = 2500; // ~1 in-game hour

    private static int Staleness(string defName)
        => staleness.TryGetValue(defName, out int c) ? c : 0;

    private static void DLog(string msg)
    {
        if (Prefs.DevMode)
            Log.Message("[HSKDietTracker/Cook] " + msg);
    }

    private static void EnsureFresh()
    {
        int tick = Find.TickManager.TicksGame;
        if (lastRebuildTick >= 0 && tick - lastRebuildTick < RebuildInterval)
            return;
        lastRebuildTick = tick;
        staleness.Clear();

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        foreach (var p in PawnsFinder.AllMaps_FreeColonists)
        {
            if (p == null || p.IsQuestLodger())
                continue;
            var data = comp.GetData(p);
            foreach (var def in data.RecentEatenSet)
                staleness[def] = Staleness(def) + 1;
        }
    }

    private static bool IsDiversifiable(ThingDef d)
    {
        if (d?.ingestible == null || !d.ingestible.HumanEdible)
            return false;
        return d.IsMeat || (d.ingestible.foodType & FoodTypeFlags.VegetableOrFruit) != 0;
    }

    /// <summary>Confirms the postfix actually fires for a food bill (dev-mode log only).</summary>
    public static void NotePostfix(string src, bool result, Bill bill)
    {
        if (!Prefs.DevMode)
            return;
        var produced = bill?.recipe?.ProducedThingDef;
        if (produced != null && produced.IsNutritionGivingIngestible)
            DLog($"[{src}] postfix fired: recipe={bill.recipe.defName} result={result}");
    }

    public static void TryDiversify(List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell, string src)
    {
        if (bill?.recipe == null || chosen == null || chosen.Count == 0 || availableThings == null)
            return;

        // Food-producing recipes only.
        var produced = bill.recipe.ProducedThingDef;
        if (produced == null || !produced.IsNutritionGivingIngestible)
        {
            DLog($"[{src}] skip recipe={bill.recipe.defName} produced={produced?.defName ?? "null"} (not nutrition ingestible)");
            return;
        }

        EnsureFresh();
        var valueGetter = bill.recipe.IngredientValueGetter;
        if (valueGetter == null)
            return;

        if (Prefs.DevMode)
        {
            var chosenStr = string.Join(", ", chosen.ConvertAll(tc => $"{tc.Thing?.def?.defName}x{tc.Count}"));
            var availStr = string.Join(", ", DistinctDefNames(availableThings));
            DLog($"[{src}] recipe={bill.recipe.defName} produced={produced.defName} chosen=[{chosenStr}] avail=[{availStr}]");
        }

        // Total chosen count per diversifiable def.
        var byDef = new Dictionary<ThingDef, int>();
        foreach (var tc in chosen)
        {
            var d = tc.Thing?.def;
            if (d == null || !IsDiversifiable(d))
                continue;
            byDef[d] = (byDef.TryGetValue(d, out int c) ? c : 0) + tc.Count;
        }
        if (byDef.Count == 0)
        {
            DLog($"[{src}] no diversifiable (meat/veg) ingredient among chosen");
            return;
        }

        foreach (var kv in byDef)
        {
            ThingDef curDef = kv.Key;
            int needed = kv.Value;
            int curStale = Staleness(curDef.defName);
            DLog($"  cur={curDef.defName} needed={needed} staleness={curStale} val={valueGetter.ValuePerUnitOf(curDef):0.###}");
            if (curStale == 0)
            {
                DLog($"  -> {curDef.defName} already novel (staleness 0), keep");
                continue; // already maximally novel — nothing to improve
            }

            // Recipe ingredient slot that allows the chosen def.
            IngredientCount slot = null;
            foreach (var ic in bill.recipe.ingredients)
            {
                if (ic.filter.Allows(curDef))
                {
                    slot = ic;
                    break;
                }
            }
            if (slot == null)
            {
                DLog($"  -> no recipe slot allows {curDef.defName}");
                continue;
            }

            float curVal = valueGetter.ValuePerUnitOf(curDef);

            // Best (least-stale) equal-nutrition alternative with enough stock on hand.
            ThingDef best = null;
            int bestStale = curStale;
            var seen = new HashSet<ThingDef>();
            foreach (var thing in availableThings)
            {
                var a = thing.def;
                if (a == curDef || !seen.Add(a))
                    continue;
                if (!IsDiversifiable(a))
                    continue;
                if (!slot.filter.Allows(a))
                {
                    DLog($"    alt {a.defName}: rejected (slot filter disallows)");
                    continue;
                }
                if (!slot.IsFixedIngredient && !bill.ingredientFilter.Allows(a))
                {
                    DLog($"    alt {a.defName}: rejected (bill filter disallows)");
                    continue;
                }
                float aVal = valueGetter.ValuePerUnitOf(a);
                if (Mathf.Abs(aVal - curVal) > 0.0001f)
                {
                    DLog($"    alt {a.defName}: rejected (nutrition {aVal:0.###} != {curVal:0.###})");
                    continue;
                }

                int aStale = Staleness(a.defName);
                int stock = AvailableStock(availableThings, chosen, a);
                if (aStale >= bestStale)
                {
                    DLog($"    alt {a.defName}: staleness {aStale} not better than {bestStale}, stock={stock}");
                    continue;
                }
                if (stock < needed)
                {
                    DLog($"    alt {a.defName}: staleness {aStale} better but stock {stock} < needed {needed}");
                    continue;
                }

                DLog($"    alt {a.defName}: candidate (staleness {aStale} < {bestStale}, stock {stock})");
                best = a;
                bestStale = aStale;
                if (bestStale == 0)
                    break;
            }
            if (best == null)
            {
                DLog($"  -> no better alternative for {curDef.defName}");
                continue;
            }

            DLog($"  -> SWAP {curDef.defName}(stale {curStale}) -> {best.defName}(stale {bestStale}) x{needed}");
            SwapChosen(availableThings, chosen, curDef, best, needed, rootCell);
        }
    }

    private static IEnumerable<string> DistinctDefNames(List<Thing> things)
    {
        var seen = new HashSet<string>();
        foreach (var t in things)
        {
            if (t?.def != null && seen.Add(t.def.defName))
                yield return t.def.defName;
        }
    }

    private static int AvailableStock(List<Thing> availableThings, List<ThingCount> chosen, ThingDef def)
    {
        int total = 0;
        foreach (var t in availableThings)
        {
            if (t.def != def)
                continue;
            int free = t.stackCount - ThingCountUtility.CountOf(chosen, t);
            if (free > 0)
                total += free;
        }
        return total;
    }

    private static void SwapChosen(List<Thing> availableThings, List<ThingCount> chosen, ThingDef from, ThingDef to, int amount, IntVec3 rootCell)
    {
        // Remove up to `amount` units of `from` from the chosen list.
        int removed = 0;
        for (int i = chosen.Count - 1; i >= 0 && removed < amount; i--)
        {
            if (chosen[i].Thing?.def != from)
                continue;

            int c = chosen[i].Count;
            if (removed + c <= amount)
            {
                removed += c;
                chosen.RemoveAt(i);
            }
            else
            {
                chosen[i] = chosen[i].WithCount(c - (amount - removed));
                removed = amount;
            }
        }

        // Fill the gap from `to` stacks, nearest first.
        var stacks = new List<Thing>();
        foreach (var t in availableThings)
        {
            if (t.def == to)
                stacks.Add(t);
        }
        stacks.Sort((x, y) =>
            (x.PositionHeld - rootCell).LengthHorizontalSquared
            .CompareTo((y.PositionHeld - rootCell).LengthHorizontalSquared));

        int remaining = removed;
        foreach (var t in stacks)
        {
            if (remaining <= 0)
                break;
            int free = t.stackCount - ThingCountUtility.CountOf(chosen, t);
            if (free <= 0)
                continue;
            int take = Mathf.Min(free, remaining);
            ThingCountUtility.AddToList(chosen, t, take);
            remaining -= take;
        }
    }
}

[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet_AllowMix")]
public static class Patch_BillIngredients_AllowMix
{
    public static void Postfix(bool __result, List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell)
    {
        IngredientDiversifier.NotePostfix("mix", __result, bill);
        if (!__result)
            return;
        IngredientDiversifier.TryDiversify(availableThings, bill, chosen, rootCell, "mix");
    }
}

[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestIngredientsInSet_NoMixHelper")]
public static class Patch_BillIngredients_NoMix
{
    public static void Postfix(bool __result, List<Thing> availableThings, List<ThingCount> chosen, IntVec3 rootCell, Bill bill)
    {
        IngredientDiversifier.NotePostfix("nomix", __result, bill);
        if (!__result || bill == null)
            return;
        IngredientDiversifier.TryDiversify(availableThings, bill, chosen, rootCell, "nomix");
    }
}
