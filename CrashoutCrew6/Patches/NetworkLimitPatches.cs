using Aggro.Core;
using HarmonyLib;
using UnityEngine;

namespace CrashoutCrew6.Patches
{
    // Aggro.Core.Platform.CreateLobbyAsync(bool allowFriends, int playerCount) is the dispatcher all
    // platforms route through; GameManager calls it with a literal 4. Raise it to the new cap.
    [HarmonyPatch(typeof(Platform), nameof(Platform.CreateLobbyAsync))]
    internal static class Platform_CreateLobbyAsync_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ref int playerCount)
        {
            int target = ModConfig.Max;
            if (playerCount < target)
            {
                Log.Debug($"Platform.CreateLobbyAsync: bumping lobby cap {playerCount} -> {target}");
                playerCount = target;
            }
        }
    }

    // Belt-and-suspenders in case anything calls the Steam implementation directly.
    [HarmonyPatch(typeof(SteamPlatform), nameof(SteamPlatform.CreateLobbyAsync))]
    internal static class SteamPlatform_CreateLobbyAsync_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ref int playerCount)
        {
            int target = ModConfig.Max;
            if (playerCount < target) playerCount = target;
        }
    }

    // AggroNetworkManager : Mirror.NetworkManager. Make sure maxConnections can hold host + (Max-1)
    // clients; never lower an already-higher value.
    [HarmonyPatch(typeof(AggroNetworkManager), "Awake")]
    internal static class AggroNetworkManager_Awake_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(AggroNetworkManager __instance)
        {
            int target = ModConfig.Max;
            if (__instance.maxConnections < target)
            {
                Log.Debug($"AggroNetworkManager.Awake: raising maxConnections {__instance.maxConnections} -> {target}");
                __instance.maxConnections = Mathf.Max(__instance.maxConnections, target);
            }
        }
    }
}
