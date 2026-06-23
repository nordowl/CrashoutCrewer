using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrashoutCrew6
{
    /// <summary>
    /// The lobby bakes exactly 4 LobbyPlayer "parking spot" scene objects. Each connection is
    /// assigned one as its player object, so 6 players need 6 of them. We register the LobbyPlayer
    /// as a Mirror spawnable prefab (cloned from a scene instance, sceneId cleared) so all peers can
    /// replicate extra copies, then the host spawns the additional spots and appends them to the
    /// lobby's available-players list.
    /// </summary>
    internal static class LobbyExpander
    {
        // Fixed, distinctive assetId for our runtime LobbyPlayer prefab (must be stable across peers).
        internal const uint AssetId = 0xC0FFEE06;

        // Hand-tuned lobby layout (confirmed in-game). The two new spots' base positions plus a
        // per-row shift that nudges each existing column for visibility.
        private static readonly Vector3 Spot5Base = new Vector3(-17.05f, 0f, 18.9f);  // left column (row B)
        private static readonly Vector3 Spot6Base = new Vector3(-10.83f, 0f, 21.2f);  // right column (row A)
        private static readonly Vector3 RowOffsetHighX = new Vector3(0f, 0f, -3.2f);  // column near x=-10.8 (row A)
        private static readonly Vector3 RowOffsetLowX = new Vector3(2f, 0f, -1f);     // column near x=-17 (row B)

        // The 4 vanilla parking-spot positions (by lobbyPlayerIndex). Fixed reference so every peer
        // computes the same layout regardless of the (networked) live kart positions.
        private static readonly Vector3[] VanillaSpots =
        {
            new Vector3(-10.83f, 0f, 14.94f), // 0 — row A front
            new Vector3(-10.78f, 0f, 17.96f), // 1 — row A back
            new Vector3(-17.05f, 0f, 12.64f), // 2 — row B front
            new Vector3(-16.73f, 0f, 15.91f), // 3 — row B back
        };
        private const float ColAX = -10.83f;
        private const float ColBX = -17.05f;

        private static Vector3 RowOffset(float x) =>
            Mathf.Abs(x - ColAX) <= Mathf.Abs(x - ColBX) ? RowOffsetHighX : RowOffsetLowX;

        private static GameObject _template;
        private static bool _sceneHookInstalled;

        // the camera zoom targets are per-spot children, so they follow the spots automatically;
        // we only need to wire the 2 new array slots once.
        internal static bool CameraTargetsBuilt;

        private static readonly System.Reflection.FieldInfo AssetIdField =
            AccessTools.Field(typeof(NetworkIdentity), "_assetId");
        private static readonly System.Reflection.FieldInfo ServerAvailableField =
            AccessTools.Field(typeof(LobbyManager), "_serverAvailablePlayers");

        /// <summary>Called once from plugin startup: make sure clients register the prefab as soon as a lobby scene loads.</summary>
        internal static void InstallSceneHook()
        {
            if (_sceneHookInstalled) return;
            _sceneHookInstalled = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != null && scene.name.ToLowerInvariant().Contains("lobby"))
            {
                if (TryFindSceneSource(out var src))
                    EnsurePrefab(src);
                else
                    Log.Debug("LobbyExpander: lobby scene loaded but no LobbyPlayer found yet (will retry on manager start).");
            }
        }

        /// <summary>Build a disabled clone of a scene LobbyPlayer and register it as a spawnable prefab. Idempotent.</summary>
        internal static void EnsurePrefab(LobbyPlayer source)
        {
            if (_template != null) return;
            if (source == null) { Log.Warn("LobbyExpander.EnsurePrefab: source is null."); return; }

            try
            {
                var holder = new GameObject("CC6_LobbyPrefabHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);

                var template = Object.Instantiate(source.gameObject, holder.transform);
                template.name = "CC6_LobbyPlayerPrefab";
                template.SetActive(false);

                var id = template.GetComponent<NetworkIdentity>();
                if (id == null) { Log.Error("LobbyExpander: source LobbyPlayer has no NetworkIdentity."); Object.Destroy(holder); return; }
                id.sceneId = 0;

                NetworkClient.RegisterPrefab(template, AssetId);
                _template = template;
                Log.Info($"LobbyExpander: registered LobbyPlayer prefab under assetId 0x{AssetId:X8}.");
            }
            catch (System.Exception e)
            {
                Log.Error("LobbyExpander.EnsurePrefab failed: " + e);
            }
        }

        private static bool TryFindSceneSource(out LobbyPlayer source)
        {
            source = null;
            var all = Resources.FindObjectsOfTypeAll<LobbyPlayer>();
            foreach (var lp in all)
            {
                if (lp == null) continue;
                var go = lp.gameObject;
                // a real scene instance, not our template / an asset
                if (go.scene.IsValid() && go.hideFlags == HideFlags.None && go != _template)
                {
                    source = lp;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Runs on every peer once the lobby starts. Clones the painted parking bays (local scene
        /// art) for the new spots, and on the server spawns the extra networked LobbyPlayer karts.
        /// </summary>
        internal static void ApplyLobby(LobbyManager mgr)
        {
            try
            {
                var originals = GetOriginalSpots();
                int have = originals.Count;
                int target = ModConfig.Max;
                if (have == 0) { Log.Warn("LobbyExpander: no original lobby spots found."); return; }

                var scene = originals[0].gameObject.scene;
                var source = originals[have - 1];

                GameObject decalsRoot = null;
                foreach (var r in scene.GetRootGameObjects())
                    if (r.name == "decals") { decalsRoot = r; break; }

                if (ModConfig.DebugLogging.Value)
                    DumpLobbyScene(scene, originals);

                CameraTargetsBuilt = false; // rebuild camera targets for this fresh lobby

                // Final positions are deterministic (fixed bases + fixed per-row offsets), so every
                // peer agrees without reading the live, networked kart positions.
                var bases = new[] { Spot5Base, Spot6Base };
                var newPositions = new List<Vector3>();
                for (int k = 0; have + k < target; k++)
                {
                    Vector3 b = k < bases.Length ? bases[k] : VanillaSpots[k % VanillaSpots.Length];
                    newPositions.Add(b + RowOffset(b.x));
                }

                // make sure the kart prefab is registered (server spawns it, clients receive it)
                EnsurePrefab(source);

                // Bays are LOCAL scene art — every peer draws its own (deterministic). The originals'
                // bay decals are shifted, and a full bay is added for each new spot.
                bool alreadyApplied = decalsRoot != null && decalsRoot.transform.Find("CC6_bays") != null;
                if (!alreadyApplied)
                {
                    var templates = CaptureBayTemplates(decalsRoot); // from vanilla decals + fixed front refs
                    ShiftDecals(decalsRoot);
                    CloneNewBays(decalsRoot, newPositions, templates);
                }

                // Kart positions are NETWORKED (server-authoritative). Only the server moves them; the
                // clients receive them over the wire. Doing it on clients too would double-shift.
                if (NetworkServer.active && mgr != null)
                {
                    ServerPositionOriginals(originals);

                    var spots = ServerAvailableField.GetValue(mgr) as List<LobbyPlayer>;
                    if (spots == null) { Log.Error("LobbyExpander: _serverAvailablePlayers not found."); return; }
                    if (spots.Count >= target) { Log.Debug("LobbyExpander: karts already added."); return; }
                    if (_template == null) { Log.Error("LobbyExpander: prefab template unavailable; cannot add spots."); return; }

                    for (int k = 0; k < newPositions.Count; k++)
                    {
                        int index = have + k;
                        var lp = SpawnSpot(source, index, newPositions[k], source.transform.rotation);
                        if (lp != null)
                        {
                            spots.Add(lp);
                            Log.Info($"LobbyExpander: added kart spot[{index}] at {Fmt(newPositions[k])}.");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error("LobbyExpander.ApplyLobby failed: " + e);
            }
        }

        private class BayTemplate
        {
            public float columnX;
            public List<(Transform decal, Vector3 rel)> parts = new List<(Transform, Vector3)>();
        }

        /// <summary>Captures each column's complete-bay decals relative to that column's fixed vanilla
        /// front-spot reference. Reads the decals while they're still at their vanilla scene positions.</summary>
        private static List<BayTemplate> CaptureBayTemplates(GameObject decalsRoot)
        {
            var templates = new List<BayTemplate>();
            if (decalsRoot == null) return templates;

            var stripes = new List<Transform>();
            foreach (var t in decalsRoot.GetComponentsInChildren<Transform>(true))
                if (t != decalsRoot.transform && t.name.Contains("stripe") && !t.name.StartsWith("CC6_"))
                    stripes.Add(t);

            // front spot of each column = the lower-Z vanilla spot (index 0 = row A, index 2 = row B)
            foreach (var front in new[] { VanillaSpots[0], VanillaSpots[2] })
            {
                var tmpl = new BayTemplate { columnX = front.x };
                foreach (var d in stripes)
                {
                    var dp = d.position;
                    if (Mathf.Abs(dp.x - front.x) < 2.6f && Mathf.Abs(dp.z - front.z) < 2.6f)
                        tmpl.parts.Add((d, dp - front));
                }
                templates.Add(tmpl);
                Log.Debug($"LobbyExpander: bay template for column x={tmpl.columnX:0.00} has {tmpl.parts.Count} decal(s).");
            }
            return templates;
        }

        /// <summary>Shifts the existing painted bay decals by their row offset. Local on every peer.</summary>
        private static void ShiftDecals(GameObject decalsRoot)
        {
            if (decalsRoot == null) return;
            foreach (var t in decalsRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == decalsRoot.transform || !t.name.Contains("stripe")) continue;
                if (t.name.StartsWith("CC6_")) continue;   // our own clones
                var p = t.position;
                if (p.z < 8f || p.z > 24f) continue;       // parking-area decals only
                t.position += RowOffset(p.x);
            }
            Log.Info("LobbyExpander: shifted existing bay decals.");
        }

        /// <summary>Server-only: positions the existing lobby karts to their final (shifted) spots.
        /// These are networked, so clients receive them — clients must NOT do this themselves.</summary>
        private static void ServerPositionOriginals(List<LobbyPlayer> originals)
        {
            foreach (var lp in originals)
            {
                if (lp == null) continue;
                int idx = lp.lobbyPlayerIndex;
                if (idx < 0 || idx >= VanillaSpots.Length) continue;
                Vector3 pos = VanillaSpots[idx] + RowOffset(VanillaSpots[idx].x);
                lp.transform.position = pos;
                var rb = lp.GetComponent<Rigidbody>();
                if (rb != null) { rb.position = pos; rb.velocity = Vector3.zero; }
            }
            Log.Info("LobbyExpander: positioned original lobby karts (server).");
        }

        /// <summary>Clones a complete bay outline centered on each new spot, using the nearest column template.</summary>
        private static void CloneNewBays(GameObject decalsRoot, List<Vector3> newPositions, List<BayTemplate> templates)
        {
            if (decalsRoot == null) { Log.Warn("LobbyExpander: 'decals' root not found; no bays added."); return; }
            if (decalsRoot.transform.Find("CC6_bays") != null) { Log.Debug("LobbyExpander: bays already present."); return; }
            var container = new GameObject("CC6_bays");
            container.transform.SetParent(decalsRoot.transform, false);

            int cloned = 0;
            foreach (var pos in newPositions)
            {
                var tmpl = NearestTemplate(templates, pos.x);
                if (tmpl == null) continue;
                foreach (var part in tmpl.parts)
                {
                    var clone = Object.Instantiate(part.decal.gameObject, container.transform, true);
                    clone.name = "CC6_" + part.decal.name;
                    clone.transform.position = pos + part.rel;
                    clone.transform.rotation = part.decal.rotation;
                    cloned++;
                }
            }
            Log.Info($"LobbyExpander: cloned {cloned} bay decal(s) for {newPositions.Count} new spot(s).");
        }

        private static BayTemplate NearestTemplate(List<BayTemplate> templates, float x)
        {
            BayTemplate best = null;
            float bestD = float.MaxValue;
            foreach (var t in templates)
            {
                float d = Mathf.Abs(t.columnX - x);
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        private static List<LobbyPlayer> GetOriginalSpots()
        {
            var list = new List<LobbyPlayer>();
            foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
                if (lp != null && !lp.name.StartsWith("CC6_")) list.Add(lp);
            list.Sort((a, b) => a.lobbyPlayerIndex.CompareTo(b.lobbyPlayerIndex));
            return list;
        }

        /// <summary>
        /// Extends LobbyCamera.playerTargetTransforms to Max so players 5/6 don't IndexOutOfRange when
        /// they open customization. The original targets are per-spot children (they already follow the
        /// shifted spots), so we leave them untouched and only wire the 2 new slots to the new karts'
        /// own camera-target child. Built once per lobby; safe to call every frame.
        /// </summary>
        internal static void EnsureCameraTargets(LobbyCamera cam)
        {
            if (CameraTargetsBuilt || cam == null) return;
            var targets = cam.playerTargetTransforms;
            if (targets == null || targets.Length == 0) return;
            int target = ModConfig.Max;
            if (targets.Length >= target) { CameraTargetsBuilt = true; return; }

            // map our spawned spots by index
            var newLps = new Dictionary<int, LobbyPlayer>();
            foreach (var lp in Object.FindObjectsOfType<LobbyPlayer>())
                if (lp != null && lp.name.StartsWith("CC6_")) newLps[lp.lobbyPlayerIndex] = lp;
            // wait until every new spot exists (so we can use its real camera-target child)
            for (int i = targets.Length; i < target; i++)
                if (!newLps.ContainsKey(i)) return;

            try
            {
                // the per-spot camera-target child name, taken from an existing entry
                string camTargetName = null;
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] != null) { camTargetName = targets[i].name; break; }

                var arr = new Transform[target];
                for (int i = 0; i < target; i++)
                {
                    if (i < targets.Length && targets[i] != null)
                    {
                        arr[i] = targets[i]; // original target follows its spot already
                    }
                    else if (newLps.TryGetValue(i, out var lp))
                    {
                        arr[i] = camTargetName != null ? FindChildRecursive(lp.transform, camTargetName) : null;
                        if (arr[i] == null) arr[i] = lp.transform; // fallback: frame the kart root
                    }
                }
                cam.playerTargetTransforms = arr;
                CameraTargetsBuilt = true;
                Log.Info($"LobbyExpander: extended lobby-camera targets to {target} (target child '{camTargetName}').");
            }
            catch (System.Exception e)
            {
                Log.Error("EnsureCameraTargets failed: " + e);
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static LobbyPlayer SpawnSpot(LobbyPlayer source, int index, Vector3 pos, Quaternion rot)
        {
            try
            {
                var go = Object.Instantiate(_template); // inactive (template is disabled)
                go.name = "CC6_LobbyPlayer_" + (index + 1);

                var id = go.GetComponent<NetworkIdentity>();
                id.sceneId = 0;
                AssetIdField.SetValue(id, AssetId); // so the game's auto NetworkServer.Spawn replicates it as our prefab

                var lp = go.GetComponent<LobbyPlayer>();
                lp.lobbyPlayerIndex = index;

                // place it in the same scene/hierarchy context as the real spots
                if (source.transform.parent != null)
                    go.transform.SetParent(source.transform.parent, false);
                else
                    SceneManager.MoveGameObjectToScene(go, source.gameObject.scene);

                go.transform.position = pos;
                go.transform.rotation = rot;

                go.SetActive(true); // EntityBehaviour.OnEnable -> creates entity + NetworkServer.Spawn(go)

                Log.Debug($"LobbyExpander.SpawnSpot[{index}]: active={go.activeInHierarchy} " +
                          $"worldPos={Fmt(go.transform.position)} netId={id.netId} scene={go.scene.name}");
                return lp;
            }
            catch (System.Exception e)
            {
                Log.Error($"LobbyExpander.SpawnSpot[{index}] failed: " + e);
                return null;
            }
        }

        private static int CountRenderers(Transform t)
        {
            return t.GetComponentsInChildren<Renderer>(true).Length;
        }

        private static bool _dumpedScene;

        /// <summary>One-time diagnostic: lists scene roots and the floor art near each existing spot (to locate the bay lines).</summary>
        private static void DumpLobbyScene(Scene scene, List<LobbyPlayer> spots)
        {
            if (_dumpedScene) return;
            _dumpedScene = true;
            try
            {
                var roots = scene.GetRootGameObjects();
                Log.Info($"LobbyExpander: === scene-lobby roots ({roots.Length}) ===");
                foreach (var r in roots)
                {
                    int rc = r.GetComponentsInChildren<Renderer>(true).Length;
                    Log.Info($"  root '{r.name}' pos={Fmt(r.transform.position)} children={r.transform.childCount} renderers={rc}");
                }

                // dump the 'decals' subtree (floor markings such as the parking bays likely live here)
                foreach (var r in roots)
                {
                    if (r.name == "decals")
                    {
                        Log.Info($"LobbyExpander: 'decals' root scale={Fmt(r.transform.localScale)} lossy={Fmt(r.transform.lossyScale)} rot={Fmt(r.transform.eulerAngles)}");
                        Log.Info("LobbyExpander: === parking-area decals (x -20..-7, z 8..24) ===");
                        foreach (var t in r.GetComponentsInChildren<Transform>(true))
                        {
                            if (t == r.transform) continue;
                            var p = t.position;
                            if (p.x < -20f || p.x > -7f || p.z < 8f || p.z > 24f) continue;
                            Log.Info($"    '{t.name}' pos={Fmt(p)} euler={Fmt(t.eulerAngles)} lScale={Fmt(t.localScale)} lossy={Fmt(t.lossyScale)}");
                        }
                    }
                }

                // every transform whose X/Z lines up with a spot (any Y, any component type)
                var allT = new List<Transform>();
                foreach (var r in roots) allT.AddRange(r.GetComponentsInChildren<Transform>(true));
                for (int i = 0; i < spots.Count; i++)
                {
                    var sp = spots[i].transform.position;
                    Log.Info($"LobbyExpander: --- objects aligned with spot[{i}] {Fmt(sp)} (any Y, excluding the kart) ---");
                    int shown = 0;
                    foreach (var t in allT)
                    {
                        if (t == null || IsUnderLobbyPlayer(t)) continue;
                        var rp = t.position;
                        if (Mathf.Abs(rp.x - sp.x) < 2.0f && Mathf.Abs(rp.z - sp.z) < 2.0f)
                        {
                            Log.Info($"    '{PathOf(t)}' pos={Fmt(rp)} comps=[{ComponentTypes(t)}]");
                            if (++shown >= 10) break;
                        }
                    }
                    if (shown == 0) Log.Info("    (none aligned)");
                }
            }
            catch (System.Exception e) { Log.Error("DumpLobbyScene failed: " + e); }
        }

        private static bool IsUnderLobbyPlayer(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.GetComponent<LobbyPlayer>() != null) return true;
            return false;
        }

        private static string PathOf(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static string ComponentTypes(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(c.GetType().Name);
            }
            return sb.ToString();
        }

        private static void DumpHierarchy(Transform root, string label)
        {
            Log.Info($"LobbyExpander: --- hierarchy of {label} ({root.name}) ---");
            DumpHierarchyRec(root, 0);
        }

        private static void DumpHierarchyRec(Transform t, int depth)
        {
            if (depth > 4) return;
            string indent = new string(' ', depth * 2);
            var rend = t.GetComponent<Renderer>();
            Log.Info($"  {indent}{t.name} active={t.gameObject.activeSelf} renderer={(rend != null ? rend.GetType().Name + (rend.enabled ? "(on)" : "(off)") : "-")}");
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchyRec(t.GetChild(i), depth + 1);
        }

        private static string Fmt(Vector3 v) =>
            $"{v.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{v.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{v.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
