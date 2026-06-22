using System.Collections.Generic;
using HarmonyLib;

namespace CrashoutCrew6.Patches
{
    // Cosmetic gates that disable the "invite friends" button once the lobby hits 4 players.
    // Each method compares playerCount < 4 and has no other int-4 literal, so a blanket rewrite is safe.

    [HarmonyPatch(typeof(LobbyButtonsUI), "OnUpdatePresentation")]
    internal static class LobbyButtonsUI_InviteGate_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            ILTools.ReplaceLoadConstant(code, ModConfig.VanillaMax, ModConfig.Max, "LobbyButtonsUI.OnUpdatePresentation");
            return code;
        }
    }

    [HarmonyPatch(typeof(GameMenuUI), "OnUpdatePresentation")]
    internal static class GameMenuUI_InviteGate_Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            ILTools.ReplaceLoadConstant(code, ModConfig.VanillaMax, ModConfig.Max, "GameMenuUI.OnUpdatePresentation");
            return code;
        }
    }
}
