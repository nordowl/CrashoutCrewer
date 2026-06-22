using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CrashoutCrew6.Patches
{
    /// <summary>
    /// End-of-contract report screen. Vanilla bakes exactly 4 of every per-player UI reference and
    /// hardcodes 4 in a loop / array allocations. We clone the rows up to Max and fix the IL.
    /// </summary>
    internal static class ReportUiFields
    {
        // The 12 parallel "one per player" reference lists on ReportUI.
        internal static readonly string[] PerPlayer =
        {
            "playerIconObjects", "playerNameTexts",
            "crashoutCountObjects", "nitroCountObjects", "driftCountObjects", "upgradeCountObjects",
            "crashoutCountTexts", "nitroCountTexts", "driftCountTexts", "upgradeCountTexts",
            "playerIconImages", "playerHighlightImages",
        };

        /// <summary>Ensures the report clipboard is held shifted up (by ModConfig.ReportYOffset) so the
        /// 6th row clears the bottom edge. The animator drives the position, so a LateUpdate component
        /// re-applies the offset each frame.</summary>
        internal static void NudgeUp(ReportUI report)
        {
            if (report == null) return;
            if (report.GetComponent<ReportClipboardOffset>() == null)
            {
                report.gameObject.AddComponent<ReportClipboardOffset>();
                Log.Info($"ReportUI.NudgeUp: attached clipboard offset holder to '{report.name}'.");
            }
        }
    }

    // SetUp: grow the row references just before populating (idempotent), and bump its `for (l<4)` loop.
    [HarmonyPatch(typeof(ReportUI), "SetUp")]
    internal static class ReportUI_SetUp_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ReportUI __instance)
        {
            UiRowExpander.Expand(__instance, ReportUiFields.PerPlayer, ModConfig.Max,
                ModConfig.ReportSquish.Value, ModConfig.ReportRowScale.Value, "ReportUI");
            ReportUiFields.NudgeUp(__instance);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            ILTools.ReplaceLoadConstant(code, ModConfig.VanillaMax, ModConfig.Max, "ReportUI.SetUp");
            return code;
        }
    }

    // FullSequenceCo and AddUpCountCo allocate `new int[4]` scratch arrays indexed by player.
    // Only resize the array allocations (NOT enum-compare 4s like ContractScore.S).
    [HarmonyPatch]
    internal static class ReportUI_Coroutine_ArraySizes_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ReportUI), "FullSequenceCo"));
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ReportUI), "AddUpCountCo"));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            ILTools.ReplaceNewArrSize(code, ModConfig.VanillaMax, ModConfig.Max, __originalMethod.DeclaringType?.Name ?? "ReportUI.coroutine");
            return code;
        }
    }
}
