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
    }
}
