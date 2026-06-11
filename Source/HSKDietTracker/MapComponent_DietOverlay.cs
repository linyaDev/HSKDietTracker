using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

public class MapComponent_DietOverlay : MapComponent
{
    public static bool Enabled = true;

    // All colonists need — gold ring, darker fill, amber "!"
    private static readonly Color RingAll = new Color(0.85f, 0.7f, 0.25f);
    private static readonly Color FillAll = new Color(0.35f, 0.25f, 0.1f, 0.95f);
    private static readonly Color ExclaimAll = new Color(0.9f, 0.65f, 0.2f);

    // Some colonists need — gold ring, green fill, green "!"
    private static readonly Color RingSome = new Color(0.85f, 0.7f, 0.25f);
    private static readonly Color FillSome = new Color(0.18f, 0.4f, 0.2f, 0.95f);
    private static readonly Color ExclaimSome = new Color(0.3f, 0.8f, 0.35f);

    private static Material matAll;
    private static Material matSome;

    private int lastCacheTick = -99999;
    private const int CacheInterval = 7500;

    private Dictionary<int, byte> animalHighlight = new Dictionary<int, byte>();

    private static Texture2D texAll;
    private static Texture2D texSome;

    // === Food item icons on the ground ===
    // Translucent backing disc colors (icon drawn on top in its natural color).
    private static readonly Color BackRingSome = new Color(0.3f, 0.8f, 0.35f, 0.9f);
    private static readonly Color BackFillSome = new Color(0.12f, 0.3f, 0.14f, 0.55f);
    private static readonly Color BackRingAll = new Color(0.9f, 0.65f, 0.2f, 0.95f);
    private static readonly Color BackFillAll = new Color(0.3f, 0.2f, 0.06f, 0.6f);

    private static Material backMatSome;
    private static Material backMatAll;
    private static Texture2D backTexSome;
    private static Texture2D backTexAll;

    // Top-3 "best for variety" ingredients get a distinct (bright yellow) ring, fill still encodes need.
    private static readonly Color BackRingTop3 = new Color(1f, 0.95f, 0.1f, 1f);
    private static Material backMatTop3Some;
    private static Material backMatTop3All;
    private static Texture2D backTexTop3Some;
    private static Texture2D backTexTop3All;

    private struct ItemMarker
    {
        public Thing thing;
        public Material backMat;
        public Material iconMat;
        public string tip;
        public float dx;
    }

    private readonly List<ItemMarker> itemMarkers = new List<ItemMarker>();

    public MapComponent_DietOverlay(Map map) : base(map)
    {
    }

    private static Texture2D MakeMarkerTexture(Color ringColor, Color fillColor, Color exclaimColor)
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        float center = size / 2f;
        float outerR = 28f;
        float innerR = 22f;
        Color bg = fillColor;
        Color exclaim = exclaimColor;

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.clear;

        // Draw filled circle (bg) + ring
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                if (dist <= outerR && dist > innerR)
                    pixels[y * size + x] = ringColor;
                else if (dist <= innerR)
                    pixels[y * size + x] = bg;
            }
        }

        // Draw "!" in center
        int cx = size / 2;
        // Vertical bar: from y=15 to y=38
        for (int y = 15; y <= 38; y++)
        {
            for (int dx = -2; dx <= 2; dx++)
                SetPx(pixels, size, cx + dx, y, exclaim);
        }
        // Dot: y=43 to y=47
        for (int y = 43; y <= 47; y++)
        {
            for (int dx = -2; dx <= 2; dx++)
                SetPx(pixels, size, cx + dx, y, exclaim);
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static void SetPx(Color[] pixels, int size, int x, int y, Color c)
    {
        if (x >= 0 && x < size && y >= 0 && y < size)
            pixels[y * size + x] = c;
    }

    private static Material GetMat(ref Material cached, ref Texture2D cachedTex, Color ring, Color fill, Color exclaim)
    {
        if (cached == null)
        {
            if (cachedTex == null)
                cachedTex = MakeMarkerTexture(ring, fill, exclaim);
            cached = MaterialPool.MatFrom(new MaterialRequest
            {
                mainTex = cachedTex,
                color = Color.white,
                shader = ShaderDatabase.MetaOverlay
            });
        }
        return cached;
    }

    private static Texture2D MakeDiscTexture(Color ringColor, Color fillColor)
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        float center = size / 2f;
        float outerR = 30f;
        float innerR = 28f; // thinner ring

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.clear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                if (dist <= outerR && dist > innerR)
                    pixels[y * size + x] = ringColor;
                else if (dist <= innerR)
                    pixels[y * size + x] = fillColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    private static Material GetBackMat(ref Material cached, ref Texture2D cachedTex, Color ring, Color fill)
    {
        if (cached == null)
        {
            if (cachedTex == null)
                cachedTex = MakeDiscTexture(ring, fill);
            cached = MaterialPool.MatFrom(new MaterialRequest
            {
                mainTex = cachedTex,
                color = Color.white,
                shader = ShaderDatabase.MetaOverlay
            });
        }
        return cached;
    }

    public override void MapComponentUpdate()
    {
        if (!Enabled)
            return;

        int tick = Find.TickManager.TicksGame;
        if (tick - lastCacheTick >= CacheInterval)
        {
            lastCacheTick = tick;
            RebuildCache();
        }

        foreach (var kvp in animalHighlight)
        {
            if (kvp.Value == 0)
                continue;

            var animal = FindAnimalById(kvp.Key);
            if (animal == null || animal.Dead || !animal.Spawned)
                continue;

            bool all = kvp.Value == 2;
            Material mat = all
                ? GetMat(ref matAll, ref texAll, RingAll, FillAll, ExclaimAll)
                : GetMat(ref matSome, ref texSome, RingSome, FillSome, ExclaimSome);

            Vector3 pos = animal.DrawPos;
            pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            pos.z += 0.85f;

            float markerSize = 0.7f;
            Matrix4x4 matrix = default;
            matrix.SetTRS(pos, Quaternion.identity, new Vector3(markerSize, 1f, markerSize));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }

        DrawItemMarkers();
    }

    private void DrawItemMarkers()
    {
        for (int i = 0; i < itemMarkers.Count; i++)
        {
            var m = itemMarkers[i];
            if (m.thing == null || m.thing.Destroyed || !m.thing.Spawned || m.thing.Map != map)
                continue;

            Vector3 pos = m.thing.DrawPos;
            pos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            pos.z += 0.55f;
            pos.x += m.dx;

            // Backing disc (green/amber) so the natural-colored icon stays readable.
            float backSize = 0.55f;
            Matrix4x4 mb = default;
            mb.SetTRS(pos, Quaternion.identity, new Vector3(backSize, 1f, backSize));
            Graphics.DrawMesh(MeshPool.plane10, mb, m.backMat, 0);

            // Icon on top, slightly higher altitude.
            Vector3 ip = pos;
            ip.y += 0.01f;
            float iconSize = 0.4f;
            Matrix4x4 mi = default;
            mi.SetTRS(ip, Quaternion.identity, new Vector3(iconSize, 1f, iconSize));
            Graphics.DrawMesh(MeshPool.plane10, mi, m.iconMat, 0);
        }
    }

    private Pawn FindAnimalById(int id)
    {
        var things = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
        for (int i = 0; i < things.Count; i++)
        {
            if (things[i].thingIDNumber == id)
                return things[i] as Pawn;
        }
        return null;
    }

    private void RebuildCache()
    {
        animalHighlight.Clear();
        itemMarkers.Clear();

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        var colonists = new List<Pawn>();
        var colonistEaten = new List<HashSet<string>>();

        foreach (var p in map.mapPawns.FreeColonistsSpawned)
        {
            if (p.IsQuestLodger())
                continue;

            colonists.Add(p);
            var data = comp.GetData(p);
            var eaten = new HashSet<string>();

            foreach (var r in data.records)
            {
                if (r.isMeal)
                {
                    foreach (var ing in r.ingredients)
                        eaten.Add(ing);
                }
                else
                {
                    eaten.Add(r.mealDef);
                }
            }
            colonistEaten.Add(eaten);
        }

        if (colonists.Count == 0)
            return;

        foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn))
        {
            var animal = thing as Pawn;
            if (animal == null || animal.Dead || !animal.RaceProps.Animal)
                continue;

            // Skip colony pets and caravan pack animals
            if (animal.Faction != null)
                continue;

            // Skip aggressive/manhunter animals
            if (animal.InAggroMentalState || animal.MentalStateDef == MentalStateDefOf.Manhunter || animal.MentalStateDef == MentalStateDefOf.ManhunterPermanent)
                continue;

            string meatDef = animal.RaceProps.meatDef?.defName;
            string raceDef = animal.def.defName;

            if (meatDef == null && raceDef == null)
                continue;

            int needCount = 0;
            for (int i = 0; i < colonistEaten.Count; i++)
            {
                bool hasMeat = meatDef != null && colonistEaten[i].Contains(meatDef);
                bool hasRace = colonistEaten[i].Contains(raceDef);
                if (!hasMeat && !hasRace)
                    needCount++;
            }

            if (needCount == 0)
                continue;

            byte level = needCount == colonists.Count ? (byte)2 : (byte)1;
            animalHighlight[animal.thingIDNumber] = level;
        }

        RebuildItemMarkers(colonists, colonistEaten);
    }

    private sealed class MarkerCandidate
    {
        public Thing thing;
        public int stack;
        public Texture2D tex;
        public Color color;
        public string label;
        public int needCount;
        public bool isTop3;
    }

    private void RebuildItemMarkers(List<Pawn> colonists, List<HashSet<string>> colonistEaten)
    {
        // One marker per *ingredient* colonists are missing. For meat the ingredients are the
        // source animals recorded on the stack (HSK lumps many animals into one meat tier, but
        // each stack's CompIngredients lists what it's actually made of). For everything else
        // the item itself is the ingredient.
        var byIngredient = new Dictionary<string, MarkerCandidate>();
        int scanned = 0, meatStacks = 0, meatNoComp = 0, meatSrc = 0, vegSeen = 0;

        foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
        {
            var def = thing.def;
            if (def?.ingestible == null || def.IsCorpse || def.IsDrug)
                continue;
            // Skip animal-only feed (hay, etc.) and kibble.
            if (!def.ingestible.HumanEdible)
                continue;
            if ((def.ingestible.foodType & FoodTypeFlags.Kibble) != 0)
                continue;
            // Raw ingredients only: exclude cooked meals (>= MealAwful) and non-food.
            var pref = def.ingestible.preferability;
            if (pref <= FoodPreferability.NeverForNutrition || pref > FoodPreferability.RawTasty)
                continue;
            // Luxuries are tracked separately.
            if (LuxurySlotLoader.AllLuxuryDefNames.Contains(def.defName))
                continue;

            scanned++;

            if (def.IsMeat)
            {
                meatStacks++;
                var ci = thing.TryGetComp<CompIngredients>();
                if (ci?.ingredients == null)
                {
                    meatNoComp++;
                    continue;
                }
                foreach (var src in ci.ingredients)
                {
                    if (src == null)
                        continue;

                    Texture2D tex;
                    Color col;
                    string label;
                    if (FoodIcon.TryGetCreature(src, out tex, out col))
                    {
                        // Specific animal (fox, muffalo, fish…).
                        label = src.LabelCap;
                    }
                    else
                    {
                        // No creature icon (e.g. humanlike flesh) → draw the meat item's own texture.
                        tex = thing.def.uiIcon;
                        col = thing.def.uiIconColor;
                        label = thing.def.LabelCap;
                        if (tex == null || tex == BaseContent.BadTex)
                            continue;
                    }

                    meatSrc++;
                    Consider(byIngredient, colonistEaten, thing, src.defName, tex, col, label);
                }
            }
            else
            {
                vegSeen++;
                if (!FoodIcon.TryGet(def, out var tex, out var col))
                    continue;
                Consider(byIngredient, colonistEaten, thing, def.defName, tex, col, def.LabelCap);
            }
        }

        // Record which ingredients actually get the yellow (top-3) ring, for the settings window.
        var yellow = new List<string>();
        foreach (var c in byIngredient.Values)
        {
            if (c.isTop3)
                yellow.Add(c.label);
        }
        yellow.Sort();
        if (HSKDietTrackerMod.Settings != null)
            HSKDietTrackerMod.Settings.lastYellowIngredients =
                yellow.Count > 0 ? string.Join(", ", yellow) : "(none)";

        if (Prefs.DevMode)
            Log.Message("[HSKDietTracker] overlay rebuild: colonists=" + colonists.Count
                + " scanned=" + scanned + " meatStacks=" + meatStacks + " meatNoComp=" + meatNoComp
                + " meatSrc=" + meatSrc + " vegSeen=" + vegSeen
                + " candidates=" + byIngredient.Count
                + " [" + string.Join(", ", new List<string>(byIngredient.Keys)) + "]");

        // Group icons that land on the same stack so we can fan them out instead of overlapping.
        var perThing = new Dictionary<Thing, List<MarkerCandidate>>();
        foreach (var c in byIngredient.Values)
        {
            if (!perThing.TryGetValue(c.thing, out var list))
            {
                list = new List<MarkerCandidate>();
                perThing[c.thing] = list;
            }
            list.Add(c);
        }

        foreach (var list in perThing.Values)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                bool all = c.needCount == colonists.Count;
                Material backMat;
                if (c.isTop3)
                    backMat = all
                        ? GetBackMat(ref backMatTop3All, ref backTexTop3All, BackRingTop3, BackFillAll)
                        : GetBackMat(ref backMatTop3Some, ref backTexTop3Some, BackRingTop3, BackFillSome);
                else
                    backMat = all
                        ? GetBackMat(ref backMatAll, ref backTexAll, BackRingAll, BackFillAll)
                        : GetBackMat(ref backMatSome, ref backTexSome, BackRingSome, BackFillSome);
                var iconMat = MaterialPool.MatFrom(new MaterialRequest
                {
                    mainTex = c.tex,
                    color = c.color,
                    shader = ShaderDatabase.MetaOverlay
                });

                float dx = (i - (list.Count - 1) / 2f) * 0.5f;
                itemMarkers.Add(new ItemMarker { thing = c.thing, backMat = backMat, iconMat = iconMat, tip = c.label, dx = dx });
            }
        }
    }

    private static void Consider(Dictionary<string, MarkerCandidate> byIngredient, List<HashSet<string>> eaten,
        Thing thing, string key, Texture2D tex, Color color, string label)
    {
        if (IgnoredLoader.Ignored.Contains(key))
            return;

        int needCount = 0;
        for (int i = 0; i < eaten.Count; i++)
        {
            if (!eaten[i].Contains(key))
                needCount++;
        }
        if (needCount == 0)
            return; // every colonist has eaten it — not interesting

        // Keep the largest stack as the representative for this ingredient.
        if (byIngredient.TryGetValue(key, out var cur) && cur.stack >= thing.stackCount)
            return;

        byIngredient[key] = new MarkerCandidate
        {
            thing = thing,
            stack = thing.stackCount,
            tex = tex,
            color = color,
            label = label,
            needCount = needCount,
            isTop3 = DietFilterState.IsTop3Key(key)
        };
    }

    public override void MapComponentOnGUI()
    {
        if (!Enabled || itemMarkers.Count == 0)
            return;
        if (Find.CurrentMap != map)
            return;

        for (int i = 0; i < itemMarkers.Count; i++)
        {
            var m = itemMarkers[i];
            if (m.thing == null || m.thing.Destroyed || !m.thing.Spawned || m.thing.Map != map || string.IsNullOrEmpty(m.tip))
                continue;

            Vector3 wp = m.thing.DrawPos;
            wp.z += 0.55f;
            wp.x += m.dx;
            Vector2 screen = wp.MapToUIPosition();
            float s = 26f;
            Rect r = new Rect(screen.x - s / 2f, screen.y - s / 2f, s, s);
            if (Mouse.IsOver(r))
                TooltipHandler.TipRegion(r, new TipSignal(m.tip, m.thing.thingIDNumber ^ 0x5D1E7));
        }
    }
}
