using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

public class Dialog_DietInfo : Window
{
    private Pawn pawn;
    private Vector2 scrollPosition;

    private static readonly Color GreenText = new Color(0.4f, 0.95f, 0.4f);
    private static readonly Color DimText = new Color(1f, 1f, 1f, 0.5f);
    private static readonly Color IconBg = new Color(0.2f, 0.2f, 0.2f, 0.6f);
    private static readonly Color IconBgHighlight = new Color(0.3f, 0.3f, 0.3f, 0.8f);
    private const float IconSize = 48f;
    private const float IconPadding = 4f;

    public override Vector2 InitialSize => new Vector2(480f, 600f);

    public Dialog_DietInfo(Pawn pawn)
    {
        this.pawn = pawn;
        doCloseButton = true;
        doCloseX = true;
        draggable = true;
        absorbInputAroundWindow = false;
    }

    public override void SetInitialSizeAndPosition()
    {
        base.SetInitialSizeAndPosition();
        var s = HSKDietTrackerMod.Settings;
        if (s != null && s.windowX >= 0f)
            windowRect.position = new Vector2(s.windowX, s.windowY);
    }

    public override void PreClose()
    {
        base.PreClose();
        var s = HSKDietTrackerMod.Settings;
        if (s != null)
        {
            s.windowX = windowRect.x;
            s.windowY = windowRect.y;
            s.Write();
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        // Update pawn if player selected a different one
        var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
        if (selectedPawn != null && selectedPawn != pawn && selectedPawn.IsColonist)
            pawn = selectedPawn;

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        var data = comp.GetData(pawn);

        // Title with pawn name
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "DT_Title".Translate() + " — " + pawn.LabelShortCap);
        Text.Font = GameFont.Small;

        float y = 40f;

        // Stats bar
        Rect statsRect = new Rect(0f, y, inRect.width, 50f);
        Widgets.DrawBoxSolid(statsRect, new Color(0.15f, 0.15f, 0.15f, 0.8f));

        float thirdW = inRect.width / 3f;
        Text.Anchor = TextAnchor.MiddleCenter;

        GUI.color = GreenText;
        Widgets.Label(new Rect(0f, y + 2f, thirdW, 22f), "DT_Meals".Translate());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y + 22f, thirdW, 26f), data.UniqueMeals.ToString());
        Text.Font = GameFont.Small;

        Widgets.Label(new Rect(thirdW, y + 2f, thirdW, 22f), "DT_Ingredients".Translate());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(thirdW, y + 22f, thirdW, 26f), data.UniqueIngredients.ToString());
        Text.Font = GameFont.Small;

        int maxScore = (int)(Need_DietVariety.GetNeutralScore() + Need_DietVariety.GetBiomeBonus());
        if (maxScore < 10) maxScore = 10;
        int neutral = maxScore / 2;
        GUI.color = data.Score >= neutral ? GreenText : new Color(1f, 0.9f, 0.3f);
        Widgets.Label(new Rect(thirdW * 2f, y + 2f, thirdW, 22f), "DT_ScoreLabel".Translate());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(thirdW * 2f, y + 22f, thirdW, 26f), data.Score + " / " + maxScore);
        Text.Font = GameFont.Small;

        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        y += 56f;

        // Grace period info
        int elapsed = Find.TickManager.TicksGame - data.firstSeenTick;
        bool inGrace = elapsed < PawnDietData.GracePeriodTicks;

        // Progress bar
        y = DrawDietProgressBar(inRect, y, data.Score, neutral, maxScore, inGrace, elapsed);

        // Collect unique meals (only cooked) and ingredients with latest tick
        var mealLatestTick = new Dictionary<string, int>();
        var ingredientLatestTick = new Dictionary<string, int>();
        foreach (var r in data.records)
        {
            if (r.isMeal)
            {
                // Cooked meal — track as meal
                if (!mealLatestTick.ContainsKey(r.mealDef) || r.tick > mealLatestTick[r.mealDef])
                    mealLatestTick[r.mealDef] = r.tick;
                // Its ingredients
                foreach (var ing in r.ingredients)
                {
                    if (!ingredientLatestTick.ContainsKey(ing) || r.tick > ingredientLatestTick[ing])
                        ingredientLatestTick[ing] = r.tick;
                }
            }
            else
            {
                // Raw food — track as ingredient
                if (!ingredientLatestTick.ContainsKey(r.mealDef) || r.tick > ingredientLatestTick[r.mealDef])
                    ingredientLatestTick[r.mealDef] = r.tick;
            }
        }

        // Calculate content height
        float iconsPerRow = Mathf.Floor((inRect.width - 16f) / (IconSize + IconPadding));
        float mealsHeight = 30f + Mathf.Ceil(mealLatestTick.Count / iconsPerRow) * (IconSize + IconPadding) + 10f;
        float ingredientsHeight = 30f + Mathf.Ceil(ingredientLatestTick.Count / iconsPerRow) * (IconSize + IconPadding) + 10f;
        float recentHeight = 30f + Mathf.Min(data.records.Count, 8) * 28f;
        float totalHeight = mealsHeight + ingredientsHeight + recentHeight + 20f;

        Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y - 50f);
        Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, totalHeight);
        Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

        float contentY = 0f;

        // === Meals section ===
        Text.Font = GameFont.Small;
        GUI.color = GreenText;
        Widgets.Label(new Rect(0f, contentY, viewRect.width, 26f), "DT_RecentMeals".Translate());
        GUI.color = Color.white;
        contentY += 28f;

        contentY = DrawIconGrid(viewRect.width, contentY, mealLatestTick);
        contentY += 10f;

        // Separator
        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        Widgets.DrawLineHorizontal(0f, contentY, viewRect.width);
        GUI.color = Color.white;
        contentY += 6f;

        // === Ingredients section ===
        GUI.color = GreenText;
        Widgets.Label(new Rect(0f, contentY, viewRect.width, 26f), "DT_RecentIngredients".Translate());
        GUI.color = Color.white;
        contentY += 28f;

        contentY = DrawIconGrid(viewRect.width, contentY, ingredientLatestTick);
        contentY += 10f;

        // Separator
        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        Widgets.DrawLineHorizontal(0f, contentY, viewRect.width);
        GUI.color = Color.white;
        contentY += 6f;

        // === Recent meals list ===
        GUI.color = DimText;
        Widgets.Label(new Rect(0f, contentY, viewRect.width, 26f), "DT_LastEaten".Translate());
        GUI.color = Color.white;
        contentY += 28f;

        int showCount = Mathf.Min(data.records.Count, 8);
        for (int i = data.records.Count - 1; i >= data.records.Count - showCount; i--)
        {
            var r = data.records[i];
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail(r.mealDef);
            string mealLabel = mealDef?.LabelCap ?? r.mealDef;

            int daysAgo = (Find.TickManager.TicksGame - r.tick) / 60000;
            string timeStr = daysAgo <= 0 ? "DT_Today".Translate().RawText : "DT_DaysAgo".Translate(daysAgo).RawText;

            // Meal icon
            if (mealDef != null)
            {
                Rect iconRect = new Rect(0f, contentY, 24f, 24f);
                Widgets.ThingIcon(iconRect, mealDef);
            }

            Widgets.Label(new Rect(28f, contentY, viewRect.width - 150f, 26f), mealLabel);

            GUI.color = DimText;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(viewRect.width - 115f, contentY, 110f, 26f), timeStr);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            contentY += 28f;
        }

        Widgets.EndScrollView();
    }


    private const int SevenDaysTicks = 420000;
    private static readonly Color TimerGreen = new Color(0.3f, 0.9f, 0.3f);
    private static readonly Color TimerYellow = new Color(0.9f, 0.9f, 0.3f);
    private static readonly Color TimerRed = new Color(0.9f, 0.3f, 0.3f);

    private float DrawIconGrid(float width, float startY, Dictionary<string, int> defTickMap)
    {
        float x = 0f;
        float y = startY;
        int now = Find.TickManager.TicksGame;

        var sorted = defTickMap.OrderBy(kvp => kvp.Value).ToList();
        foreach (var kvp in sorted)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
            if (def == null)
                continue;

            // Skip expired or nearly expired entries (< 1 hour)
            if (kvp.Value + SevenDaysTicks - now < 2500)
                continue;

            if (x + IconSize > width)
            {
                x = 0f;
                y += IconSize + IconPadding;
            }

            Rect iconRect = new Rect(x, y, IconSize, IconSize);

            // Background
            Widgets.DrawBoxSolid(iconRect, IconBg);
            if (Mouse.IsOver(iconRect))
            {
                Widgets.DrawBoxSolid(iconRect, IconBgHighlight);
                int tLeft = kvp.Value + SevenDaysTicks - now;
                int dLeft = tLeft / 60000;
                int hLeft = (tLeft % 60000) / 2500;
                string timeLeft = dLeft > 0 ? dLeft + " " + "FT_Days".Translate() : hLeft + "h";
                TooltipHandler.TipRegion(iconRect, def.LabelCap + "\n" + "DT_ExpiresIn".Translate(timeLeft));
            }

            // Icon — try uiIcon, then category icon, then text
            Rect innerRect = iconRect.ContractedBy(4f);
            bool drawn = false;

            // 1. uiIcon
            if (!drawn && def.uiIcon != null && def.uiIcon != BaseContent.BadTex)
            {
                Log.Message($"[DietTracker] {def.defName}: uiIcon found, size={def.uiIcon.width}x{def.uiIcon.height}, name={def.uiIcon.name}");
                GUI.DrawTexture(innerRect, def.uiIcon, ScaleMode.ScaleToFit);
                drawn = true;
            }

            // 2. category icon (reliable for implied defs like Meat_, Corpse_)
            if (!drawn && def.thingCategories != null)
            {
                foreach (var cat in def.thingCategories)
                {
                    Log.Message($"[DietTracker] {def.defName}: category={cat.defName}, hasIcon={cat.icon != null}, iconName={cat.icon?.name}, iconSize={cat.icon?.width}x{cat.icon?.height}, isBadTex={cat.icon == BaseContent.BadTex}");
                    if (cat.icon != null && cat.icon != BaseContent.BadTex)
                    {
                        GUI.DrawTexture(innerRect, cat.icon, ScaleMode.ScaleToFit);
                        drawn = true;
                        break;
                    }
                }
            }

            if (!drawn)
            {
                Log.Message($"[DietTracker] {def.defName}: NO ICON FOUND. uiIcon={def.uiIcon != null}, graphicData={def.graphicData != null}, categories={def.thingCategories?.Count ?? 0}");
            }

            // 3. parent race icon (for Corpse_, Meat_, Leather_)
            if (!drawn && def.ingestible?.sourceDef?.uiIcon != null && def.ingestible.sourceDef.uiIcon != BaseContent.BadTex)
            {
                GUI.DrawTexture(innerRect, def.ingestible.sourceDef.uiIcon, ScaleMode.ScaleToFit);
                drawn = true;
            }

            // 4. try by defName prefix (Corpse_X → X, Meat_X → X)
            if (!drawn)
            {
                string raceName = null;
                if (def.defName.StartsWith("Corpse_")) raceName = def.defName.Substring(7);
                else if (def.defName.StartsWith("Meat_")) raceName = def.defName.Substring(5);
                else if (def.defName.StartsWith("Leather_")) raceName = def.defName.Substring(8);

                if (raceName != null)
                {
                    var raceDef = DefDatabase<ThingDef>.GetNamedSilentFail(raceName);
                    if (raceDef?.uiIcon != null && raceDef.uiIcon != BaseContent.BadTex)
                    {
                        GUI.DrawTexture(innerRect, raceDef.uiIcon, ScaleMode.ScaleToFit);
                        drawn = true;
                    }
                }
            }

            // 5. text fallback
            if (!drawn)
            {
                GUI.color = DimText;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = true;
                Widgets.Label(iconRect.ContractedBy(2f), def.LabelCap);
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            // Timer overlay
            int ticksLeft = kvp.Value + SevenDaysTicks - now;
            int daysRemaining = ticksLeft / 60000;
            int hoursRemaining = (ticksLeft % 60000) / 2500;
            string timerStr = daysRemaining > 0 ? daysRemaining + "d" : hoursRemaining + "h";

            // Color based on urgency
            if (daysRemaining < 1)
                GUI.color = TimerRed;
            else if (daysRemaining <= 3)
                GUI.color = TimerRed;
            else if (daysRemaining <= 7)
                GUI.color = TimerYellow;
            else
                GUI.color = TimerGreen;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(x, y, IconSize - 2f, 18f), timerStr);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            x += IconSize + IconPadding;
        }

        return y + IconSize + IconPadding;
    }

    private static readonly int[] DietMoods = { -16, -12, -8, -4, 0, 2, 4, 6 };
    private static readonly float[] DietThresholds = { 0.10f, 0.20f, 0.30f, 0.42f, 0.57f, 0.71f, 0.85f, 1.0f };
    private static readonly Color[] DietColors =
    {
        new Color(0.7f, 0.1f, 0.1f),
        new Color(0.85f, 0.2f, 0.2f),
        new Color(0.95f, 0.35f, 0.2f),
        new Color(0.95f, 0.6f, 0.2f),
        new Color(0.7f, 0.7f, 0.7f),
        new Color(0.4f, 0.75f, 0.3f),
        new Color(0.3f, 0.85f, 0.3f),
        new Color(0.15f, 0.95f, 0.4f),
    };
    private static readonly string[] DietStageKeys =
    {
        "DT_Stage0", "DT_Stage1", "DT_Stage2", "DT_Stage3",
        "DT_Stage4", "DT_Stage5", "DT_Stage6", "DT_Stage7"
    };

    private float DrawDietProgressBar(Rect inRect, float y, int score, int neutral, int maxScore, bool inGrace = false, int graceElapsed = 0)
    {
        float barHeight = 18f;
        float barX = 20f;
        float barWidth = inRect.width - 40f;

        // Background
        Widgets.DrawBoxSolid(new Rect(barX, y, barWidth, barHeight), new Color(0.1f, 0.1f, 0.1f, 0.8f));

        // Colored segments + tooltips
        float prevX = 0f;
        for (int i = 0; i < DietThresholds.Length; i++)
        {
            float segEnd = DietThresholds[i] * barWidth;
            Rect segRect = new Rect(barX + prevX, y, segEnd - prevX, barHeight);
            Widgets.DrawBoxSolid(segRect, DietColors[i]);

            GUI.color = new Color(0f, 0f, 0f, 0.3f);
            Widgets.DrawBox(segRect, 1);
            GUI.color = Color.white;

            if (Mouse.IsOver(segRect))
            {
                string moodVal = DietMoods[i] >= 0 ? "+" + DietMoods[i] : DietMoods[i].ToString();
                TooltipHandler.TipRegion(segRect, DietStageKeys[i].Translate() + " (" + moodVal + ")");
            }
            prevX = segEnd;
        }

        // Marker
        float normalized = maxScore > 0 ? Mathf.Clamp01((float)score / maxScore) : 0f;
        float markerX = barX + normalized * barWidth;
        Widgets.DrawBoxSolid(new Rect(markerX - 2f, y - 2f, 4f, barHeight + 4f), Color.white);

        y += barHeight + 4f;

        // Current stage text
        int currentStage = 0;
        for (int i = 0; i < DietThresholds.Length; i++)
        {
            if (normalized <= DietThresholds[i]) { currentStage = i; break; }
            if (i == DietThresholds.Length - 1) currentStage = i;
        }

        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = DietColors[currentStage];
        string moodStr = DietMoods[currentStage] >= 0 ? "+" + DietMoods[currentStage] : DietMoods[currentStage].ToString();
        string stageText = DietStageKeys[currentStage].Translate() + " (" + moodStr + ")";

        // Points to next stage
        if (currentStage < DietThresholds.Length - 1)
        {
            int nextScoreNeeded = (int)(DietThresholds[currentStage] * maxScore) + 1;
            int pointsToNext = nextScoreNeeded - score;
            if (pointsToNext > 0)
                stageText += "  →  " + "DT_NextLevel".Translate(pointsToNext);
        }

        Widgets.Label(new Rect(0f, y, inRect.width, 20f), stageText);
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        y += 24f;

        // Grace period label
        if (inGrace)
        {
            int daysLeft = (PawnDietData.GracePeriodTicks - graceElapsed) / 60000;
            if (daysLeft < 1) daysLeft = 1;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.5f, 0.8f, 1f);
            Rect graceRect = new Rect(0f, y, inRect.width, 20f);
            Widgets.Label(graceRect, "DT_GracePeriod".Translate(daysLeft));
            if (Mouse.IsOver(graceRect))
                TooltipHandler.TipRegion(graceRect, "DT_GracePeriodTooltip".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f;
        }

        return y;
    }
}
