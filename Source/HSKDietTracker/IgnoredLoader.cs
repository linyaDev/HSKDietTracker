using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Verse;

namespace HSKDietTracker;

/// <summary>
/// Loads ingredient defNames to skip in the map food overlay (e.g. seasonings like Salt).
/// Mirrors BiomeBonusLoader / LuxurySlotLoader: reads Data/IgnoredDefs/Ignored.xml directly.
/// </summary>
[StaticConstructorOnStartup]
public static class IgnoredLoader
{
    public static HashSet<string> Ignored = new HashSet<string>();

    static IgnoredLoader()
    {
        Load();
    }

    public static void Load()
    {
        Ignored.Clear();

        foreach (var mod in LoadedModManager.RunningMods)
        {
            if (mod.PackageId != "linya.hskdiettracker")
                continue;

            string path = Path.Combine(mod.RootDir, "Data", "IgnoredDefs", "Ignored.xml");
            if (!File.Exists(path))
            {
                Log.Warning("[HSKDietTracker] Ignored.xml not found at " + path);
                break;
            }

            try
            {
                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root != null)
                {
                    foreach (var el in root.Elements("Item"))
                    {
                        string dn = el.Value.Trim();
                        if (!string.IsNullOrEmpty(dn))
                            Ignored.Add(dn);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error("[HSKDietTracker] Error loading Ignored.xml: " + e.Message);
            }
            break;
        }
    }
}
