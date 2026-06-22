using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CrashoutCrew6.Patches
{
    /// <summary>
    /// Players spawn at PlayerPosition markers chosen by `_playerSpawned++ % _positions.Count`. If a
    /// room has fewer markers than players, the 5th/6th wrap onto an earlier player's spot. We log the
    /// marker count and nudge any wrapped spawn aside so karts don't spawn exactly inside each other.
    /// </summary>
    [HarmonyPatch(typeof(AggroNetworkManager), "ServerGetSpawnedPlayer")]
    internal static class SpawnSpread_Patch
    {
        private static readonly FieldInfo PosField = AccessTools.Field(typeof(AggroNetworkManager), "_positions");
        private static readonly FieldInfo SpawnedField = AccessTools.Field(typeof(AggroNetworkManager), "_playerSpawned");

        [HarmonyPostfix]
        private static void Postfix(GameObject __result)
        {
            try
            {
                int count = (PosField?.GetValue(null) as ICollection)?.Count ?? 0;
                int spawned = SpawnedField != null ? (int)SpawnedField.GetValue(null) : 0;
                int indexUsed = spawned - 1; // _playerSpawned was already incremented inside the method
                Log.Debug($"Spawn: markers={count}, indexUsed={indexUsed}");

                if (count <= 0 || __result == null) return;
                int wrap = indexUsed / count;
                if (wrap <= 0) return; // got a distinct marker; nothing to do

                // offset so the extra player doesn't materialise exactly on top of an earlier one
                var t = __result.transform;
                Vector3 offset = t.right * (1.6f * wrap);
                t.position += offset;
                var rb = __result.GetComponent<Rigidbody>();
                if (rb != null) rb.position += offset;
                Log.Info($"Spawn spread: wrapped spawn (index {indexUsed}, {count} markers) nudged by {offset}.");
            }
            catch (Exception e)
            {
                Log.Error("SpawnSpread postfix failed: " + e);
            }
        }
    }
}
