using System.Collections.Generic;
using HarmonyLib;
using Mirror;

namespace CrashoutCrew6.Patches
{
    internal static class LobbyPatchShared
    {
        internal static readonly System.Reflection.FieldInfo ServerAvailableField =
            AccessTools.Field(typeof(LobbyManager), "_serverAvailablePlayers");
    }

    // Runs on every peer. On the server it spawns the 2 extra spots; on clients it makes sure the
    // spawnable prefab is registered (belt-and-suspenders alongside the scene-load hook).
    [HarmonyPatch(typeof(LobbyManager), "OnEntityStart")]
    internal static class LobbyManager_OnEntityStart_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(LobbyManager __instance)
        {
            try
            {
                LobbyExpander.ApplyLobby(__instance);
            }
            catch (System.Exception e)
            {
                Log.Error("LobbyManager.OnEntityStart postfix failed: " + e);
            }
        }
    }

    // The lobby camera zooms to playerTargetTransforms[lobbyPlayerIndex] when a player opens
    // customization. That array is baked at 4, so players 5/6 would IndexOutOfRange. Extend it
    // (and shift the existing targets to follow the moved spots) before the camera uses it.
    [HarmonyPatch(typeof(LobbyCamera), "OnUpdatePresentation")]
    internal static class LobbyCamera_OnUpdatePresentation_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(LobbyCamera __instance)
        {
            if (!LobbyExpander.CameraTargetsBuilt)
                LobbyExpander.EnsureCameraTargets(__instance);
        }
    }

    // Safety net: if the assignment index is ever out of range (e.g. spawning the extra spots
    // failed), skip rather than throw IndexOutOfRange on the server.
    [HarmonyPatch(typeof(LobbyManager), "ServerAddPlayer")]
    internal static class LobbyManager_ServerAddPlayer_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(LobbyManager __instance, int playerIndex)
        {
            var spots = LobbyPatchShared.ServerAvailableField.GetValue(__instance) as List<LobbyPlayer>;
            if (spots == null || playerIndex < 0 || playerIndex >= spots.Count)
            {
                Log.Error($"ServerAddPlayer: playerIndex {playerIndex} out of range (available={spots?.Count ?? -1}); skipping to avoid a crash. The extra lobby spots may not have spawned.");
                return false; // skip original, don't crash
            }
            return true;
        }
    }
}
