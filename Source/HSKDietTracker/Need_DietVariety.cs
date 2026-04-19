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

        if (BiomeBonusLoader.BiomeBonuses.TryGetValue(biome.defName, out int bonus))
            return bonus;

        return 0;
    }

    public static float GetNeutralScore()
    {
        var s = HSKDietTrackerMod.Settings;
        var techLevel = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Neolithic;

        int epochMax;
        if (s != null)
        {
            switch (techLevel)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic: epochMax = s.epochNeolithic; break;
                case TechLevel.Medieval: epochMax = s.epochMedieval; break;
                case TechLevel.Industrial: epochMax = s.epochIndustrial; break;
                default: epochMax = s.epochSpacer; break;
            }
        }
        else
        {
            switch (techLevel)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic: epochMax = 30; break;
                case TechLevel.Medieval: epochMax = 40; break;
                case TechLevel.Industrial: epochMax = 50; break;
                default: epochMax = 60; break;
            }
        }

        return epochMax;
    }

    private static Texture2D cachedInfoIcon;
    private static Texture2D InfoIcon => cachedInfoIcon ??= ContentFinder<Texture2D>.Get("UI/Buttons/InfoButton", true);

    public Need_DietVariety(Pawn pawn) : base(pawn)
    {
    }

    private bool IsGuest => pawn.Faction != Faction.OfPlayer || pawn.IsQuestLodger();

    public override bool ShowOnNeedList => !IsGuest;

    public override int GUIChangeArrow => 0;

    public override void SetInitialLevel()
    {
        CurLevel = 0.5f;
    }

    public override void NeedInterval()
    {
        if (IsGuest)
            return;

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        var data = comp.GetData(pawn);
        float maxScore = GetNeutralScore() + GetBiomeBonus();
        if (maxScore < 10f) maxScore = 10f;
        float score = Mathf.Clamp(data.Score, 0f, maxScore);
        float realLevel = score / maxScore;

        // Grace period: blend from 0.5 to real value over 3 days
        int elapsed = Find.TickManager.TicksGame - data.firstSeenTick;
        if (elapsed < PawnDietData.GracePeriodTicks)
        {
            float t = (float)elapsed / PawnDietData.GracePeriodTicks;
            CurLevel = Mathf.Lerp(0.5f, realLevel, t);
        }
        else
        {
            CurLevel = realLevel;
        }
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
