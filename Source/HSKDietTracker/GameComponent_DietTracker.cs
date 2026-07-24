using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

public class DietRecord : IExposable
{
    public int tick;
    public string mealDef;
    public bool isMeal;
    public List<string> ingredients = new List<string>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref tick, "tick");
        Scribe_Values.Look(ref mealDef, "mealDef");
        Scribe_Values.Look(ref isMeal, "isMeal");
        Scribe_Collections.Look(ref ingredients, "ingredients", LookMode.Value);
        if (ingredients == null)
            ingredients = new List<string>();
    }
}

public class LuxuryRecord : IExposable
{
    public int tick;
    public string defName;
    public string category;

    public void ExposeData()
    {
        Scribe_Values.Look(ref tick, "tick");
        Scribe_Values.Look(ref defName, "defName");
        Scribe_Values.Look(ref category, "category");
    }
}

public class PawnDietData : IExposable
{
    public List<DietRecord> records = new List<DietRecord>();
    public List<LuxuryRecord> luxuryRecords = new List<LuxuryRecord>();
    public int firstSeenTick = -1;

    public const int MealWeight = 2;
    public const int IngredientWeight = 3;
    private const int SevenDaysTicks = 420000;
    public const int GracePeriodTicks = 180000; // 3 days

    private IEnumerable<DietRecord> ValidRecords
    {
        get
        {
            int cutoff = Find.TickManager.TicksGame - SevenDaysTicks + 2500; // 1 hour buffer
            return records.Where(r => r.tick >= cutoff);
        }
    }

    private IEnumerable<LuxuryRecord> ValidLuxuryRecords
    {
        get
        {
            int cutoff = Find.TickManager.TicksGame - SevenDaysTicks + 2500;
            return luxuryRecords.Where(r => r.tick >= cutoff);
        }
    }

    public int UniqueMeals => ValidRecords.Where(r => r.isMeal).Select(r => r.mealDef).Distinct().Count();

    public HashSet<string> UniqueMealSet => new HashSet<string>(ValidRecords.Where(r => r.isMeal).Select(r => r.mealDef));

    public int UniqueIngredients
    {
        get
        {
            var all = new HashSet<string>();
            foreach (var r in ValidRecords)
            {
                if (r.isMeal)
                {
                    foreach (var ing in r.ingredients)
                        all.Add(ing);
                }
                else
                {
                    all.Add(r.mealDef);
                }
            }
            return all.Count;
        }
    }

    public int Score => UniqueMeals * MealWeight + UniqueIngredients * IngredientWeight;

    /// <summary>All distinct ingredient/raw-food defNames eaten within the last 7 days.</summary>
    public HashSet<string> RecentEatenSet
    {
        get
        {
            var all = new HashSet<string>();
            foreach (var r in ValidRecords)
            {
                if (r.isMeal)
                {
                    foreach (var ing in r.ingredients)
                        all.Add(ing);
                }
                else
                {
                    all.Add(r.mealDef);
                }
            }
            return all;
        }
    }

    public int LuxuryScore
    {
        get
        {
            int total = 0;
            foreach (var cat in LuxurySlotLoader.Categories)
            {
                int filled = FilledSlots(cat.name);
                total += filled * cat.pointsPerSlot;
            }
            return total;
        }
    }

    public int FilledSlots(string categoryName)
    {
        var cat = LuxurySlotLoader.Categories.Find(c => c.name == categoryName);
        if (cat == null) return 0;
        int unique = ValidLuxuryRecords
            .Where(r => r.category == categoryName)
            .Select(r => r.defName)
            .Distinct()
            .Count();
        return Mathf.Min(unique, cat.slots);
    }

    public List<string> FilledLuxuryItems(string categoryName)
    {
        return ValidLuxuryRecords
            .Where(r => r.category == categoryName)
            .Select(r => r.defName)
            .Distinct()
            .ToList();
    }

    public Dictionary<string, int> FilledLuxuryItemsWithTicks(string categoryName)
    {
        var result = new Dictionary<string, int>();
        foreach (var r in ValidLuxuryRecords)
        {
            if (r.category != categoryName) continue;
            if (!result.ContainsKey(r.defName) || r.tick > result[r.defName])
                result[r.defName] = r.tick;
        }
        return result;
    }

    public int TotalFilledSlots
    {
        get
        {
            int total = 0;
            foreach (var cat in LuxurySlotLoader.Categories)
                total += FilledSlots(cat.name);
            return total;
        }
    }

    public bool HasEatenMeal(string mealDefName)
    {
        int cutoff = Find.TickManager.TicksGame - SevenDaysTicks;
        return records.Any(r => r.mealDef == mealDefName && r.tick >= cutoff);
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref records, "records", LookMode.Deep);
        Scribe_Collections.Look(ref luxuryRecords, "luxuryRecords", LookMode.Deep);
        Scribe_Values.Look(ref firstSeenTick, "firstSeenTick", -1);
        if (records == null)
            records = new List<DietRecord>();
        if (luxuryRecords == null)
            luxuryRecords = new List<LuxuryRecord>();
    }
}

public class GameComponent_DietTracker : GameComponent
{
    private Dictionary<int, PawnDietData> pawnData = new Dictionary<int, PawnDietData>();
    private int cleanupCounter;
    private const int SevenDaysTicks = 420000;

    public GameComponent_DietTracker(Game game) : base()
    {
    }

    public PawnDietData GetData(Pawn pawn)
    {
        if (!pawnData.TryGetValue(pawn.thingIDNumber, out var data))
        {
            data = new PawnDietData { firstSeenTick = Find.TickManager.TicksGame };
            pawnData[pawn.thingIDNumber] = data;
        }
        if (data.firstSeenTick < 0)
            data.firstSeenTick = data.records.Count > 0 ? 0 : Find.TickManager.TicksGame;
        return data;
    }

    public void RecordMeal(Pawn pawn, string mealDef, bool isMeal, List<string> ingredients)
    {
        var data = GetData(pawn);
        data.records.Add(new DietRecord
        {
            tick = Find.TickManager.TicksGame,
            mealDef = mealDef,
            isMeal = isMeal,
            ingredients = ingredients ?? new List<string>()
        });
    }

    public void RecordLuxury(Pawn pawn, string defName)
    {
        if (!LuxurySlotLoader.DefToCategory.TryGetValue(defName, out string category))
            return;
        RecordLuxury(pawn, defName, category);
    }

    public void RecordLuxury(Pawn pawn, string defName, string category)
    {
        var data = GetData(pawn);
        data.luxuryRecords.Add(new LuxuryRecord
        {
            tick = Find.TickManager.TicksGame,
            defName = defName,
            category = category
        });
    }

    public override void GameComponentTick()
    {
        cleanupCounter++;
        if (cleanupCounter < 60000)
            return;
        cleanupCounter = 0;

        int cutoff = Find.TickManager.TicksGame - SevenDaysTicks;
        foreach (var data in pawnData.Values)
        {
            data.records.RemoveAll(r => r.tick < cutoff);
            data.luxuryRecords.RemoveAll(r => r.tick < cutoff);
        }
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref pawnData, "pawnData", LookMode.Value, LookMode.Deep);
        if (pawnData == null)
            pawnData = new Dictionary<int, PawnDietData>();
    }
}
