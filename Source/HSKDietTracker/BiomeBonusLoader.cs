using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Verse;

namespace HSKDietTracker;

[StaticConstructorOnStartup]
public static class BiomeBonusLoader
{
    public static Dictionary<string, int> BiomeBonuses = new Dictionary<string, int>();

    static BiomeBonusLoader()
    {
        LoadBonuses();
    }

    public static void LoadBonuses()
    {
        BiomeBonuses.Clear();

        foreach (var mod in LoadedModManager.RunningMods)
        {
            if (mod.PackageId != "linya.hskdiettracker")
                continue;

            string path = Path.Combine(mod.RootDir, "Data", "BiomeBonusDefs", "BiomeBonuses.xml");
            if (!File.Exists(path))
            {
                Log.Warning("[HSKDietTracker] BiomeBonuses.xml not found at " + path);
                continue;
            }

            try
            {
                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null) continue;

                foreach (var element in root.Elements())
                {
                    string biomeName = element.Name.LocalName;
                    if (int.TryParse(element.Value, out int bonus))
                    {
                        BiomeBonuses[biomeName] = bonus;
                    }
                }

                Log.Message($"[HSKDietTracker] Loaded {BiomeBonuses.Count} biome bonuses.");
            }
            catch (System.Exception e)
            {
                Log.Error("[HSKDietTracker] Error loading BiomeBonuses.xml: " + e.Message);
            }
            break;
        }
    }
}
