using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

public class HSKDietTrackerSettings : ModSettings
{
    // Epoch base max
    public int epochNeolithic = 30;
    public int epochMedieval = 40;
    public int epochIndustrial = 50;
    public int epochSpacer = 60;

    // Window position
    public float windowX = -1f;
    public float windowY = -1f;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref epochNeolithic, "epochNeolithic", 30);
        Scribe_Values.Look(ref epochMedieval, "epochMedieval", 40);
        Scribe_Values.Look(ref epochIndustrial, "epochIndustrial", 50);
        Scribe_Values.Look(ref epochSpacer, "epochSpacer", 60);
        Scribe_Values.Look(ref windowX, "windowX", -1f);
        Scribe_Values.Look(ref windowY, "windowY", -1f);
        base.ExposeData();
    }
}

public class HSKDietTrackerMod : Mod
{
    public static HSKDietTrackerSettings Settings;

    public HSKDietTrackerMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<HSKDietTrackerSettings>();
    }

    private string[] epochBuffers = new string[4];
    private string currentBiomeBuffer;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var list = new Listing_Standard();
        list.ColumnWidth = inRect.width;
        list.Begin(inRect);

        // === Epoch table ===
#if V15
        list.Label("DT_SettingsEpoch".Translate(), -1f, (string)null);
#else
        list.Label("DT_SettingsEpoch".Translate(), -1f, (TipSignal?)null);
#endif
        list.Gap(2f);
        float y = list.CurHeight;

        float labelW = 110f;
        float fieldW = 50f;
        float colW = labelW + fieldW + 10f;

        Settings.epochNeolithic = DrawField(inRect, y, 0, "DT_EpochNeolithic".Translate(), Settings.epochNeolithic, epochBuffers, 0, labelW, fieldW, colW);
        Settings.epochMedieval = DrawField(inRect, y, 1, "DT_EpochMedieval".Translate(), Settings.epochMedieval, epochBuffers, 1, labelW, fieldW, colW);
        y += 28f;
        Settings.epochIndustrial = DrawField(inRect, y, 0, "DT_EpochIndustrial".Translate(), Settings.epochIndustrial, epochBuffers, 2, labelW, fieldW, colW);
        Settings.epochSpacer = DrawField(inRect, y, 1, "DT_EpochSpacer".Translate(), Settings.epochSpacer, epochBuffers, 3, labelW, fieldW, colW);
        y += 36f;

        // === Current biome ===
        Widgets.Label(new Rect(0f, y, inRect.width, 24f), "DT_SettingsBiome".Translate());
        y += 26f;

        var biome = Find.CurrentMap?.Biome;
        if (biome != null)
        {
            string biomeName = biome.defName;
            int currentBonus = BiomeBonusLoader.BiomeBonuses.TryGetValue(biomeName, out int b) ? b : 0;

            Widgets.Label(new Rect(0f, y, labelW + 60f, 24f),
                "DT_CurrentBiome".Translate() + ": " + biome.LabelCap + " (" + biomeName + ")");

            if (currentBiomeBuffer == null)
                currentBiomeBuffer = currentBonus.ToString();

            currentBiomeBuffer = Widgets.TextField(new Rect(labelW + 160f, y + 2f, fieldW, 22f), currentBiomeBuffer);

            if (int.TryParse(currentBiomeBuffer, out int newBonus) && newBonus != currentBonus)
            {
                BiomeBonusLoader.BiomeBonuses[biomeName] = Mathf.Clamp(newBonus, -50, 200);
            }

            y += 28f;

            // Info
            int epochMax = GetCurrentEpochMax();
            int biomeBonus = BiomeBonusLoader.BiomeBonuses.TryGetValue(biomeName, out int bb) ? bb : 0;
            int total = epochMax + biomeBonus;

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(0f, y, inRect.width, 20f),
                "DT_FormulaInfo".Translate(epochMax, biomeBonus, total, total / 2));
            GUI.color = Color.white;
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "DT_NoBiome".Translate());
            GUI.color = Color.white;
        }

        y += 28f;

        // === All biome bonuses ===
        GUI.color = new Color(1f, 1f, 1f, 0.4f);
        Widgets.DrawLineHorizontal(0f, y, inRect.width);
        GUI.color = Color.white;
        y += 6f;

#if V15
        Widgets.Label(new Rect(0f, y, inRect.width, 24f), "DT_AllBiomes".Translate());
#else
        Widgets.Label(new Rect(0f, y, inRect.width, 24f), "DT_AllBiomes".Translate());
#endif
        y += 26f;

        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        foreach (var kvp in BiomeBonusLoader.BiomeBonuses)
        {
            string sign = kvp.Value >= 0 ? "+" : "";
            Color col = kvp.Value > 0 ? new Color(0.4f, 0.95f, 0.4f, 0.8f) : kvp.Value < 0 ? new Color(0.95f, 0.4f, 0.4f, 0.8f) : new Color(1f, 1f, 1f, 0.5f);
            GUI.color = col;
            Widgets.Label(new Rect(10f, y, inRect.width - 20f, 20f), kvp.Key + ": " + sign + kvp.Value);
            y += 22f;
        }
        GUI.color = Color.white;

        list.End();
    }

    private int GetCurrentEpochMax()
    {
        var techLevel = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Neolithic;
        switch (techLevel)
        {
            case TechLevel.Animal:
            case TechLevel.Neolithic: return Settings.epochNeolithic;
            case TechLevel.Medieval: return Settings.epochMedieval;
            case TechLevel.Industrial: return Settings.epochIndustrial;
            default: return Settings.epochSpacer;
        }
    }

    private int DrawField(Rect inRect, float y, int col, string label, int value, string[] buffers, int bufIdx, float labelW, float fieldW, float colW)
    {
        float x = col * colW;
        if (bufIdx >= buffers.Length) return value;

        Widgets.Label(new Rect(x, y, labelW, 26f), label);

        if (buffers[bufIdx] == null)
            buffers[bufIdx] = value.ToString();

        buffers[bufIdx] = Widgets.TextField(new Rect(x + labelW, y + 2f, fieldW, 22f), buffers[bufIdx]);

        if (int.TryParse(buffers[bufIdx], out int result))
            return Mathf.Clamp(result, 0, 200);
        return value;
    }

    public override string SettingsCategory()
    {
        return "DT_SettingsCategory".Translate();
    }
}
