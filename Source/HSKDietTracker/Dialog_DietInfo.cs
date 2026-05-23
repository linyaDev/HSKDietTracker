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

    public override Vector2 InitialSize => new Vector2(480f, 680f);

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
        Widgets.Label(new Rect(0f, 0f, inRect.width - 130f, 35f), "DT_Title".Translate() + " — " + pawn.LabelShortCap);
        Text.Font = GameFont.Small;

        // History button (top right, aligned with title)
        float btnWidth = 120f;
        float btnHeight = 28f;
        Rect btnRect = new Rect(inRect.width - btnWidth - 4f, 3f, btnWidth, btnHeight);
        if (Widgets.ButtonText(btnRect, "DT_HistoryBtn".Translate()))
        {
            var existing = Find.WindowStack.WindowOfType<Dialog_DietHistory>();
            if (existing != null)
                existing.Close();
            else
                Find.WindowStack.Add(new Dialog_DietHistory(pawn, windowRect));
        }

        float y = 40f;

        // Stats bar
        Rect statsRect = new Rect(0f, y, inRect.width, 50f);
        Widgets.DrawBoxSolid(statsRect, new Color(0.15f, 0.15f, 0.15f, 0.8f));

        bool luxurySettingOn = HSKDietTrackerMod.Settings?.luxuryEnabled ?? true;
        var techLevel = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Neolithic;
        bool luxuryLocked = techLevel <= TechLevel.Neolithic;
        bool luxuryOn = luxurySettingOn && !luxuryLocked;
        bool luxuryVisible = luxurySettingOn; // show column even if locked
        float colW = inRect.width / (luxuryVisible ? 4f : 3f);
        Text.Anchor = TextAnchor.MiddleCenter;

        GUI.color = GreenText;
        Widgets.Label(new Rect(0f, y + 2f, colW, 22f), "DT_Meals".Translate());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y + 22f, colW, 26f), data.UniqueMeals.ToString());
        Text.Font = GameFont.Small;

        Widgets.Label(new Rect(colW, y + 2f, colW, 22f), "DT_Ingredients".Translate());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(colW, y + 22f, colW, 26f), data.UniqueIngredients.ToString());
        Text.Font = GameFont.Small;

        int scoreColIdx = 2;
        if (luxuryVisible)
        {
            GUI.color = luxuryLocked ? DimText : (data.TotalFilledSlots > 0 ? GreenText : DimText);
            Widgets.Label(new Rect(colW * 2f, y + 2f, colW, 22f), "DT_Luxury".Translate());
            Text.Font = GameFont.Medium;
            if (luxuryLocked)
                Widgets.Label(new Rect(colW * 2f, y + 22f, colW, 26f), "—");
            else
                Widgets.Label(new Rect(colW * 2f, y + 22f, colW, 26f), data.TotalFilledSlots + " / " + LuxurySlotLoader.TotalSlots);
            Text.Font = GameFont.Small;
            scoreColIdx = 3;
        }

        int maxScore = (int)(Need_DietVariety.GetNeutralScore() + Need_DietVariety.GetBiomeBonus());
        if (maxScore < 10) maxScore = 10;
        int neutral = maxScore / 2;
        int totalScore = data.Score + (luxuryOn ? data.LuxuryScore : 0);
        GUI.color = totalScore >= neutral ? GreenText : new Color(1f, 0.9f, 0.3f);
        Widgets.Label(new Rect(colW * scoreColIdx, y + 2f, colW, 22f), "DT_ScoreLabel".Translate());
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(colW * scoreColIdx, y + 22f, colW, 26f), totalScore + " / " + maxScore);
        Text.Font = GameFont.Small;

        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        y += 56f;

        // Small colony info
        // Grace period / small colony info
        int elapsed = Find.TickManager.TicksGame - data.firstSeenTick;
        bool inGrace = elapsed < PawnDietData.GracePeriodTicks;
        bool smallColony = PawnsFinder.AllMaps_FreeColonistsSpawned.Count < 3;

        // Progress bar
        y = DrawDietProgressBar(inRect, y, totalScore, neutral, maxScore, inGrace, elapsed, smallColony);

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
        float luxuryHeight = luxuryVisible ? 30f + LuxCellSize + LuxCellPadding + 16f + (luxuryLocked ? 24f : 0f) : 0f;
        float totalHeight = mealsHeight + ingredientsHeight + luxuryHeight + 20f;

        Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y - 50f);
        Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, totalHeight);
        Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

        float contentY = 0f;

        // === Meals section ===
        Text.Font = GameFont.Small;
        GUI.color = GreenText;
        Widgets.Label(new Rect(0f, contentY, viewRect.width, 26f), "DT_RecentMeals".Translate(mealLatestTick.Count));
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
        Widgets.Label(new Rect(0f, contentY, viewRect.width, 26f), "DT_RecentIngredients".Translate(ingredientLatestTick.Count));
        GUI.color = Color.white;
        contentY += 28f;

        contentY = DrawIconGrid(viewRect.width, contentY, ingredientLatestTick);
        contentY += 10f;

        if (luxuryVisible)
        {
            // Separator
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            Widgets.DrawLineHorizontal(0f, contentY, viewRect.width);
            GUI.color = Color.white;
            contentY += 6f;

            // === Luxury slots section ===
            GUI.color = luxuryLocked ? DimText : GreenText;
            Widgets.Label(new Rect(0f, contentY, viewRect.width, 26f), "DT_LuxurySlots".Translate());
            GUI.color = Color.white;
            contentY += 28f;

            if (luxuryLocked)
            {
                // Locked message
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.5f, 0.8f, 1f);
                Widgets.Label(new Rect(0f, contentY, viewRect.width, 20f), "DT_LuxuryLocked".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                contentY += 24f;
            }

            contentY = DrawLuxurySlots(viewRect.width, contentY, data);
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
                GUI.DrawTexture(innerRect, def.uiIcon, ScaleMode.ScaleToFit);
                drawn = true;
            }

            // 2. category icon (reliable for implied defs like Meat_, Corpse_)
            if (!drawn && def.thingCategories != null)
            {
                foreach (var cat in def.thingCategories)
                {
                    if (cat.icon != null && cat.icon != BaseContent.BadTex)
                    {
                        GUI.DrawTexture(innerRect, cat.icon, ScaleMode.ScaleToFit);
                        drawn = true;
                        break;
                    }
                }
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

    private static readonly Color LuxuryFilledBg = new Color(0.15f, 0.4f, 0.15f, 0.7f);
    private static readonly Color LuxuryEmptyBg = new Color(0.15f, 0.15f, 0.15f, 0.6f);
    private const float LuxCellSize = 72f;
    private const float LuxCellPadding = 6f;
    private const float LuxIconArea = 40f;

    private static readonly Color LuxuryTimerColor = new Color(0.3f, 0.85f, 0.3f, 0.35f);

    private float DrawLuxurySlots(float width, float startY, PawnDietData data)
    {
        float x = 0f;
        float y = startY;
        int now = Find.TickManager.TicksGame;

        foreach (var cat in LuxurySlotLoader.Categories)
        {
            var filledItemTicks = data.FilledLuxuryItemsWithTicks(cat.name);
            var filledItems = filledItemTicks.Keys.ToList();
            string catLabel = cat.labelKey.Translate();

            for (int slot = 0; slot < cat.slots; slot++)
            {
                if (x + LuxCellSize > width)
                {
                    x = 0f;
                    y += LuxCellSize + LuxCellPadding;
                }

                Rect cellRect = new Rect(x, y, LuxCellSize, LuxCellSize);
                bool isFilled = slot < filledItems.Count;

                // Background
                Widgets.DrawBoxSolid(cellRect, LuxuryEmptyBg);

                // Timer progress bar (green, top to bottom) for filled slots
                if (isFilled)
                {
                    int itemTick = filledItemTicks[filledItems[slot]];
                    int ticksLeft = itemTick + SevenDaysTicks - now;
                    float fillPct = Mathf.Clamp01((float)ticksLeft / SevenDaysTicks);
                    float fillHeight = (LuxCellSize - 2f) * fillPct;
                    Widgets.DrawBoxSolid(new Rect(x + 1f, y + 1f, LuxCellSize - 2f, fillHeight), LuxuryTimerColor);
                }

                // Border
                GUI.color = isFilled ? GreenText : new Color(1f, 1f, 1f, 0.15f);
                Widgets.DrawBox(cellRect, 1);
                GUI.color = Color.white;

                // Icon area (upper center)
                Rect iconArea = new Rect(x + (LuxCellSize - LuxIconArea) / 2f, y + 4f, LuxIconArea, LuxIconArea);

                if (isFilled)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(filledItems[slot]);
                    if (def?.uiIcon != null && def.uiIcon != BaseContent.BadTex)
                        GUI.DrawTexture(iconArea, def.uiIcon, ScaleMode.ScaleToFit);
                }

                // Label (bottom, centered)
                GUI.color = isFilled ? GreenText : DimText;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = true;
                Widgets.Label(new Rect(x + 2f, y + LuxCellSize - 24f, LuxCellSize - 4f, 20f), catLabel);
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                // Points overlay (top-right)
                GUI.color = isFilled ? GreenText : DimText;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(x, y + 1f, LuxCellSize - 3f, 16f), "+" + cat.pointsPerSlot);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                // Tooltip
                if (Mouse.IsOver(cellRect))
                {
                    string status = data.FilledSlots(cat.name) + "/" + cat.slots;
                    var itemLines = cat.items.Select(i =>
                    {
                        var d = DefDatabase<ThingDef>.GetNamedSilentFail(i);
                        string label = d?.LabelCap.RawText ?? i;
                        bool consumed = filledItems.Contains(i);
                        return (consumed ? "\u2713 " : "   ") + label;
                    });

                    string tip = catLabel + " (" + status + ")";
                    if (isFilled)
                    {
                        int itemTick = filledItemTicks[filledItems[slot]];
                        int tLeft = itemTick + SevenDaysTicks - now;
                        int dLeft = tLeft / 60000;
                        int hLeft = (tLeft % 60000) / 2500;
                        string timeLeft = dLeft > 0 ? dLeft + "d " + hLeft + "h" : hLeft + "h";
                        tip += "\n" + "DT_ExpiresIn".Translate(timeLeft);
                    }
                    tip += "\n\n" + "DT_LuxuryItems".Translate() + "\n" + string.Join("\n", itemLines);
                    TooltipHandler.TipRegion(cellRect, tip);
                }

                x += LuxCellSize + LuxCellPadding;
            }

            // Gap between categories
            x += 4f;
        }

        return y + LuxCellSize + LuxCellPadding;
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

    private float DrawDietProgressBar(Rect inRect, float y, int score, int neutral, int maxScore, bool inGrace = false, int graceElapsed = 0, bool smallColony = false)
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

        // Small colony / grace period label
        if (smallColony)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.5f, 0.8f, 1f);
            Rect smallRect = new Rect(0f, y, inRect.width, 20f);
            Widgets.Label(smallRect, "DT_SmallColony".Translate());
            if (Mouse.IsOver(smallRect))
                TooltipHandler.TipRegion(smallRect, "DT_SmallColonyTooltip".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f;
        }
        else if (inGrace)
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
