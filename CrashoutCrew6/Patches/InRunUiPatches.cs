using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CrashoutCrew6.Patches
{
    /// <summary>
    /// In-run per-player UI that indexes baked 4-element arrays by the live player count and would
    /// throw IndexOutOfRange for players 5/6: the "proceed/ready" indicators and the modifier-vote
    /// dots. We grow each to Max the first time the component updates.
    /// </summary>
    internal static class InRunUiExpand
    {
        private static readonly HashSet<int> Done = new HashSet<int>();

        internal static void Once(UnityEngine.Component c, Action a)
        {
            if (c == null) return;
            if (!Done.Add(c.GetInstanceID())) return;
            try { a(); }
            catch (Exception e) { Log.Error("InRunUiExpand failed: " + e); }
        }
    }

    // Proceed area (the floor zone showing who is ready to advance).
    [HarmonyPatch(typeof(ShiftProceedArea), "OnUpdatePresentation")]
    internal static class ShiftProceedArea_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ShiftProceedArea __instance) =>
            InRunUiExpand.Once(__instance, () =>
                UiRowExpander.Expand(__instance, new[] { "playerImages" }, ModConfig.Max, false, 1f, "ShiftProceedArea"));
    }

    // Proceed UI (the per-player ready icons on the HUD).
    [HarmonyPatch(typeof(PlayerProceedUI), "OnUpdatePresentation")]
    internal static class PlayerProceedUI_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerProceedUI __instance) =>
            InRunUiExpand.Once(__instance, () =>
                UiRowExpander.Expand(__instance, new[] { "playerParents", "playerIcons" }, ModConfig.Max, false, 1f, "PlayerProceedUI"));
    }

    // Modifier vote screen: per-player dots on each choice button + the "undecided" row.
    [HarmonyPatch(typeof(ModifierChoiceManagerUI), "RefreshVotes")]
    internal static class ModifierChoiceManagerUI_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ModifierChoiceManagerUI __instance) =>
            InRunUiExpand.Once(__instance, () =>
            {
                int max = ModConfig.Max;
                UiRowExpander.Expand(__instance, new[] { "undecidedPlayers" }, max, false, 1f, "ModifierChoiceManagerUI.undecided");
                var a = AccessTools.Field(typeof(ModifierChoiceManagerUI), "choiceButtonA")?.GetValue(__instance) as ModifierChoiceButtonUI;
                var b = AccessTools.Field(typeof(ModifierChoiceManagerUI), "choiceButtonB")?.GetValue(__instance) as ModifierChoiceButtonUI;
                if (a != null) UiRowExpander.Expand(a, new[] { "players" }, max, false, 1f, "ModifierChoiceButtonUI.A");
                if (b != null) UiRowExpander.Expand(b, new[] { "players" }, max, false, 1f, "ModifierChoiceButtonUI.B");
            });
    }
}
