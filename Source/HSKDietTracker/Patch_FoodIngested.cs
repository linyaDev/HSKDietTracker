using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKDietTracker;

[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
public static class Patch_FoodIngested
{
    public static void Postfix(Thing __instance, Pawn ingester)
    {
        if (ingester == null || !ingester.IsColonist)
            return;

        if (__instance.def?.ingestible == null)
            return;

        // Skip drugs, medicine, non-food
        if (__instance.def.IsDrug || __instance.def.ingestible.preferability <= FoodPreferability.NeverForNutrition)
            return;

        var comp = Current.Game?.GetComponent<GameComponent_DietTracker>();
        if (comp == null)
            return;

        string mealDef = __instance.def.defName;

        var compIngredients = __instance.TryGetComp<CompIngredients>();
        List<string> ingredients = compIngredients?.ingredients?
            .Select(i => i.defName)
            .ToList() ?? new List<string>();

        // Cooked meal = has ingredients AND preferability >= MealAwful (or is pemmican)
        bool isMeal = ingredients.Count > 0
                      && (__instance.def.ingestible.preferability >= FoodPreferability.MealAwful
                          || __instance.def.defName == "Pemmican");

        comp.RecordMeal(ingester, mealDef, isMeal, ingredients);
    }
}
