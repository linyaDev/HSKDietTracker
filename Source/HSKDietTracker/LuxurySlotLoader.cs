using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace HSKDietTracker;

public class LuxuryCategory
{
    public string name;
    public string labelKey;
    public int slots;
    public int pointsPerSlot;
    public HashSet<string> items = new HashSet<string>();
}

[StaticConstructorOnStartup]
public static class LuxurySlotLoader
{
    public static List<LuxuryCategory> Categories = new List<LuxuryCategory>();
    public static HashSet<string> AllLuxuryDefNames = new HashSet<string>();
    public static Dictionary<string, string> DefToCategory = new Dictionary<string, string>();

    static LuxurySlotLoader()
    {
        LoadCategories();
    }

    public static void LoadCategories()
    {
        Categories.Clear();
        AllLuxuryDefNames.Clear();
        DefToCategory.Clear();

        foreach (var mod in LoadedModManager.RunningMods)
        {
            if (mod.PackageId != "linya.hskdiettracker")
                continue;

            string path = Path.Combine(mod.RootDir, "Data", "LuxuryDefs", "Luxuries.xml");
            if (!File.Exists(path))
            {
                Log.Warning("[HSKDietTracker] Luxuries.xml not found at " + path);
                continue;
            }

            try
            {
                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null) continue;

                foreach (var catEl in root.Elements("Category"))
                {
                    var cat = new LuxuryCategory
                    {
                        name = catEl.Attribute("name")?.Value ?? "Unknown",
                        labelKey = catEl.Attribute("labelKey")?.Value ?? "DT_LuxUnknown",
                        slots = int.TryParse(catEl.Attribute("slots")?.Value, out int s) ? s : 1,
                        pointsPerSlot = int.TryParse(catEl.Attribute("pointsPerSlot")?.Value, out int p) ? p : 5
                    };

                    foreach (var itemEl in catEl.Elements("Item"))
                    {
                        string defName = itemEl.Value.Trim();
                        if (string.IsNullOrEmpty(defName)) continue;
                        cat.items.Add(defName);
                        AllLuxuryDefNames.Add(defName);
                        DefToCategory[defName] = cat.name;
                    }

                    Categories.Add(cat);
                    Log.Message("[HSKDietTracker] Luxury category loaded: " + cat.name + " (" + cat.items.Count + " items, " + cat.slots + " slots)");
                }
            }
            catch (System.Exception e)
            {
                Log.Error("[HSKDietTracker] Error loading Luxuries.xml: " + e.Message);
            }
            break;
        }
    }

    public static int TotalSlots
    {
        get
        {
            int total = 0;
            foreach (var cat in Categories)
                total += cat.slots;
            return total;
        }
    }

    public static int MaxLuxuryScore
    {
        get
        {
            int total = 0;
            foreach (var cat in Categories)
                total += cat.slots * cat.pointsPerSlot;
            return total;
        }
    }

    /// <summary>
    /// Auto-detect luxury category by item properties (fallback when defName not in XML).
    /// </summary>
    public static string DetectCategory(ThingDef def)
    {
        if (def == null) return null;

        // Check drug chemicals
        if (def.IsDrug && def.ingestible?.outcomeDoers != null)
        {
            foreach (var doer in def.ingestible.outcomeDoers)
            {
                if (doer is IngestionOutcomeDoer_GiveHediff giveHediff)
                {
                    string chemical = giveHediff.toleranceChemical?.defName;
                    if (chemical == "Alcohol") return "Alcohol";
                }
            }
        }

        return null;
    }
}
