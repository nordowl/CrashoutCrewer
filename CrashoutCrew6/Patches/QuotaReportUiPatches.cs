using HarmonyLib;

namespace CrashoutCrew6.Patches
{
    /// <summary>
    /// Per-shift quota report. Its per-player arrays (crashout count objects/texts and the
    /// "player present" dots) are baked at 4 and indexed by the live player count, so they must
    /// grow to Max or the 5th/6th player throws IndexOutOfRange. No IL change needed.
    /// </summary>
    [HarmonyPatch(typeof(QuotaReportUI), "Show")]
    internal static class QuotaReportUI_Show_Patch
    {
        private static readonly string[] PerPlayerFields =
        {
            "allPlayerCrashoutCounts", "allPlayerCrashoutTexts", "playerImages",
        };

        [HarmonyPrefix]
        private static void Prefix(QuotaReportUI __instance)
        {
            UiRowExpander.Expand(__instance, PerPlayerFields, ModConfig.Max, false, 1.0f, "QuotaReportUI");
        }
    }
}
