using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

/// <summary>
/// Resolves a drawable icon for a food / ingredient / source def, using the same multi-step
/// fallback the diet info window uses, plus a render of the animal's body graphic for defs that
/// carry a race but no uiIcon (HSK meat-source defs are category=Item, so the game never builds
/// their pawn icon). Prefers the specific creature/item; the generic category icon is last resort.
/// </summary>
public static class FoodIcon
{
    private static readonly Dictionary<string, KeyValuePair<Texture2D, Color>> raceTexCache
        = new Dictionary<string, KeyValuePair<Texture2D, Color>>();

    /// <summary>
    /// A *specific creature* icon only (animal body graphic). Returns false for humanlikes and
    /// for anything that isn't a non-humanlike race — callers then fall back to the meat texture.
    /// </summary>
    public static bool TryGetCreature(ThingDef def, out Texture2D tex, out Color color)
    {
        tex = null;
        color = Color.white;
        if (def?.race == null || def.race.Humanlike)
            return false;

        if (Valid(def.uiIcon))
        {
            tex = def.uiIcon;
            color = def.uiIconColor;
            return true;
        }
        return TryRaceGraphic(def, out tex, out color);
    }

    public static bool TryGet(ThingDef def, out Texture2D tex, out Color color)
    {
        tex = null;
        color = Color.white;
        if (def == null)
            return false;

        // 1. The def's own icon (raw item, veg, properly-iconned pawn).
        if (Valid(def.uiIcon))
        {
            tex = def.uiIcon;
            color = def.uiIconColor;
            return true;
        }

        // 2. Render the body graphic if this def carries a race (Item-category meat sources).
        if (TryRaceGraphic(def, out tex, out color))
            return true;

        // 3. Source creature for Corpse_/Meat_/Leather_ implied defs.
        var src = def.ingestible?.sourceDef;
        if (src != null)
        {
            if (Valid(src.uiIcon))
            {
                tex = src.uiIcon;
                color = src.uiIconColor;
                return true;
            }
            if (TryRaceGraphic(src, out tex, out color))
                return true;
        }

        // 4. Resolve the source race by defName prefix.
        string raceName = null;
        if (def.defName.StartsWith("Corpse_")) raceName = def.defName.Substring(7);
        else if (def.defName.StartsWith("Meat_")) raceName = def.defName.Substring(5);
        else if (def.defName.StartsWith("Leather_")) raceName = def.defName.Substring(8);
        if (raceName != null)
        {
            var raceDef = DefDatabase<ThingDef>.GetNamedSilentFail(raceName);
            if (raceDef != null)
            {
                if (Valid(raceDef.uiIcon))
                {
                    tex = raceDef.uiIcon;
                    color = raceDef.uiIconColor;
                    return true;
                }
                if (TryRaceGraphic(raceDef, out tex, out color))
                    return true;
            }
        }

        // 5. Generic category icon (reliable but not specific).
        if (def.thingCategories != null)
        {
            foreach (var cat in def.thingCategories)
            {
                if (Valid(cat.icon))
                {
                    tex = cat.icon;
                    color = Color.white;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Pulls the east-facing body texture from the race's representative pawnkind.</summary>
    private static bool TryRaceGraphic(ThingDef def, out Texture2D tex, out Color color)
    {
        tex = null;
        color = Color.white;
        if (def?.race == null)
            return false;
        // Humanlike body sprites (no head/apparel layers) look wrong as an icon — skip them.
        if (def.race.Humanlike)
            return false;

        if (raceTexCache.TryGetValue(def.defName, out var cached))
        {
            tex = cached.Key;
            color = cached.Value;
            return tex != null;
        }

        Texture2D result = null;
        Color resultColor = Color.white;
        var pk = def.race.AnyPawnKind;
        var stages = pk?.lifeStages;
        if (stages != null && stages.Count > 0)
        {
            var bgd = stages[stages.Count - 1]?.bodyGraphicData;
            var graphic = bgd?.Graphic;
            var mat = graphic?.MatAt(Rot4.East);
            if (mat?.mainTexture is Texture2D t2)
            {
                result = t2;
                resultColor = mat.color;
            }
        }

        raceTexCache[def.defName] = new KeyValuePair<Texture2D, Color>(result, resultColor);
        tex = result;
        color = resultColor;
        return result != null;
    }

    private static bool Valid(Texture2D t) => t != null && t != BaseContent.BadTex;
}
