using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

public class Need_DietVariety : Need
{
    public static int GetBiomeBonus()
    {
        var biome = Find.CurrentMap?.Biome;
        if (biome == null)
            return 0;

        string name = biome.defName;

        // Forest, tropical, swamp/bog: +10
        if (name.Contains("Forest") || name.Contains("forest")
            || name.Contains("Jungle") || name.Contains("jungle")
            || name.Contains("Tropical") || name.Contains("tropical")
            || name.Contains("Swamp") || name.Contains("swamp")
            || name.Contains("Bog") || name.Contains("bog"))
            return 10;

        // Tundra: +10
        if (name.Contains("Tundra") || name.Contains("tundra"))
            return 10;

        // Desert, ice, arid: -5
        if (name.Contains("Desert") || name.Contains("desert")
            || name.Contains("Ice") || name.Contains("ice")
            || name.Contains("Arid") || name.Contains("arid"))
            return -5;

        return 0;
    }

    public static float GetNeutralScore()
    {
        var techLevel = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Neolithic;
        switch (techLevel)
        {
            case TechLevel.Animal:
            case TechLevel.Neolithic:
                return 15f;
            case TechLevel.Medieval:
                return 25f;
            case TechLevel.Industrial:
                return 35f;
            default:
                return 45f;
        }
    }

    private static Texture2D cachedInfoIcon;
    private static Texture2D InfoIcon => cachedInfoIcon ??= ContentFinder<Texture2D>.Get("UI/Buttons/InfoButton", true);

    public Need_DietVariety(Pawn pawn) : base(pawn)
    {
    }

    public override bool ShowOnNeedList => true;

    public override int GUIChangeArrow => 0;

    public override void SetInitialLevel()
    {
        CurLevel = 0.5f;
    }

    public override void NeedInterval()
    {
        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        var data = comp.GetData(pawn);
        float neutral = GetNeutralScore() + GetBiomeBonus();
        float maxScore = neutral * 2f;
        float score = Mathf.Clamp(data.Score, 0f, maxScore);
        CurLevel = score / maxScore;
    }

    public override string GetTipString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(def.LabelCap);
        sb.AppendLine();
        sb.AppendLine(def.description);


        return sb.ToString();
    }

    public override void DrawOnGUI(Rect rect, int maxThresholdMarkers = int.MaxValue,
        float customMargin = -1f, bool drawArrows = true, bool doTooltip = true,
        Rect? rectForTooltip = null, bool drawLabel = true)
    {
        float margin = customMargin >= 0f ? customMargin : 29f;

        Rect btnRect = new Rect(rect.x + margin, rect.y + rect.height - 10f, 16f, 16f);
        GUI.DrawTexture(btnRect, InfoIcon, ScaleMode.ScaleToFit);
        if (Widgets.ButtonInvisible(btnRect))
        {
            Find.WindowStack.Add(new Dialog_DietInfo(pawn));
        }

        base.DrawOnGUI(rect, maxThresholdMarkers, customMargin, drawArrows, doTooltip, rectForTooltip, drawLabel);
    }

    public override void ExposeData()
    {
        base.ExposeData();
    }
}
