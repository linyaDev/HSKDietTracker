using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKDietTracker;

public class Dialog_DietHistory : Window
{
    private Pawn pawn;
    private Vector2 scrollPosition;
    private Rect parentRect;

    private static readonly Color DimText = new Color(1f, 1f, 1f, 0.5f);
    private static readonly Color GreenText = new Color(0.4f, 0.95f, 0.4f);

    private const float WinWidth = 320f;
    private const float WinHeight = 500f;

    public override Vector2 InitialSize => new Vector2(WinWidth, WinHeight);

    public Dialog_DietHistory(Pawn pawn, Rect parentWindowRect)
    {
        this.pawn = pawn;
        this.parentRect = parentWindowRect;
        doCloseButton = false;
        doCloseX = true;
        draggable = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false; // keep WASD/edge camera movement while open
        focusWhenOpened = false;
    }

    public override void SetInitialSizeAndPosition()
    {
        base.SetInitialSizeAndPosition();
        // Position to the right of the parent window
        float x = parentRect.xMax + 4f;
        float y = parentRect.y;
        // Clamp to screen
        if (x + WinWidth > UI.screenWidth)
            x = parentRect.x - WinWidth - 4f;
        if (y + WinHeight > UI.screenHeight)
            y = UI.screenHeight - WinHeight;
        windowRect.position = new Vector2(x, y);
    }

    public override void DoWindowContents(Rect inRect)
    {
        // Follow pawn selection
        var selectedPawn = Find.Selector.SingleSelectedThing as Pawn;
        if (selectedPawn != null && selectedPawn != pawn && selectedPawn.IsColonist)
            pawn = selectedPawn;

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        var data = comp.GetData(pawn);

        // Title
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "DT_LastEaten".Translate());
        Text.Font = GameFont.Small;

        // Pawn name
        GUI.color = DimText;
        Widgets.Label(new Rect(0f, 32f, inRect.width, 22f), pawn.LabelShortCap);
        GUI.color = Color.white;

        float y = 58f;
        int recordCount = data.records.Count;

        Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y);
        float rowHeight = 28f;
        Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, recordCount * rowHeight);
        Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

        float contentY = 0f;
        for (int i = recordCount - 1; i >= 0; i--)
        {
            var r = data.records[i];
            ThingDef mealDef = DefDatabase<ThingDef>.GetNamedSilentFail(r.mealDef);
            string mealLabel = mealDef?.LabelCap ?? r.mealDef;

            int daysAgo = (Find.TickManager.TicksGame - r.tick) / 60000;
            string timeStr = daysAgo <= 0 ? "DT_Today".Translate().RawText : "DT_DaysAgo".Translate(daysAgo).RawText;

            // Icon
            if (mealDef != null)
            {
                Rect iconRect = new Rect(0f, contentY, 24f, 24f);
                Widgets.ThingIcon(iconRect, mealDef);
            }

            // Label
            Widgets.Label(new Rect(28f, contentY, viewRect.width - 140f, 26f), mealLabel);

            // Time
            GUI.color = DimText;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(viewRect.width - 80f, contentY, 76f, 26f), timeStr);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Ingredient tooltip
            Rect rowRect = new Rect(0f, contentY, viewRect.width, 26f);
            if (r.ingredients != null && r.ingredients.Count > 0 && Mouse.IsOver(rowRect))
            {
                var ingLabels = r.ingredients.Select(ing =>
                {
                    var ingDef = DefDatabase<ThingDef>.GetNamedSilentFail(ing);
                    return ingDef?.LabelCap.RawText ?? ing;
                });
                TooltipHandler.TipRegion(rowRect, "DT_IngredientsTip".Translate() + "\n" + string.Join(", ", ingLabels));
            }

            contentY += rowHeight;
        }

        Widgets.EndScrollView();
    }
}
