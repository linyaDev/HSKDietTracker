using System.Collections.Generic;
using System.Linq;
using RimWorld;
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

public class PawnDietData : IExposable
{
    public List<DietRecord> records = new List<DietRecord>();

    public const int MealWeight = 1;
    public const int IngredientWeight = 3;
    private const int SevenDaysTicks = 420000;

    private IEnumerable<DietRecord> ValidRecords
    {
        get
        {
            int cutoff = Find.TickManager.TicksGame - SevenDaysTicks + 2500; // 1 hour buffer
            return records.Where(r => r.tick >= cutoff);
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

    public bool HasEatenMeal(string mealDefName)
    {
        int cutoff = Find.TickManager.TicksGame - SevenDaysTicks;
        return records.Any(r => r.mealDef == mealDefName && r.tick >= cutoff);
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref records, "records", LookMode.Deep);
        if (records == null)
            records = new List<DietRecord>();
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
            data = new PawnDietData();
            pawnData[pawn.thingIDNumber] = data;
        }
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
        }
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref pawnData, "pawnData", LookMode.Value, LookMode.Deep);
        if (pawnData == null)
            pawnData = new Dictionary<int, PawnDietData>();
    }
}
