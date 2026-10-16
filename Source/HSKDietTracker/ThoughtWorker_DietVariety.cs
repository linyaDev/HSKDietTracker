using RimWorld;
using Verse;

namespace HSKDietTracker;

public class ThoughtWorker_DietVariety : ThoughtWorker
{
    public override ThoughtState CurrentStateInternal(Pawn p)
    {
        var need = p.needs?.TryGetNeed<Need_DietVariety>();
        if (need == null || !need.ShowOnNeedList)
            return ThoughtState.Inactive;

        float level = need.CurLevel;

        if (level <= 0.10f) return ThoughtState.ActiveAtStage(0);  // -16
        if (level <= 0.20f) return ThoughtState.ActiveAtStage(1);  // -12
        if (level <= 0.30f) return ThoughtState.ActiveAtStage(2);  // -8
        if (level <= 0.42f) return ThoughtState.ActiveAtStage(3);  // -4
        if (level <= 0.57f) return ThoughtState.ActiveAtStage(4);  // 0
        if (level <= 0.71f) return ThoughtState.ActiveAtStage(5);  // +2
        if (level <= 0.85f) return ThoughtState.ActiveAtStage(6);  // +4
        return ThoughtState.ActiveAtStage(7);                      // +6
    }
}
