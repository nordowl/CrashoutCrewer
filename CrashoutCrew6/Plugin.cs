using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrashoutCrew6
{
    [BepInPlugin(Guid, "Crashout Crew 6 Players", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.flux.crashoutcrew6";

        internal static Plugin Instance { get; private set; }
        private Harmony _harmony;
        private static GameObject _hotkeyGo;

        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            ModConfig.Bind(Config);

            Log.Info($"Crashout Crew 6 Players loading (MaxPlayers = {ModConfig.Max}).");

            try
            {
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                int patched = 0;
                foreach (var m in _harmony.GetPatchedMethods())
                {
                    patched++;
                    Log.Info($"  patched: {m.DeclaringType?.FullName}.{m.Name}");
                }
                Log.Info($"Harmony patching complete: {patched} method(s) patched.");

                // Register the extra lobby parking-spot prefab as soon as a lobby scene loads.
                LobbyExpander.InstallSceneHook();

                // The preview hotkeys run on a dedicated object that survives the game's boot-time
                // DontDestroyOnLoad purge (which can stop this plugin component from ticking).
                EnsureHotkeyHandler();
                SceneManager.sceneLoaded += (s, m) => EnsureHotkeyHandler();
            }
            catch (Exception e)
            {
                Log.Error("Setup failed: " + e);
            }
        }

        private static void EnsureHotkeyHandler()
        {
            if (_hotkeyGo != null) return; // Unity '==' returns true if destroyed -> recreate
            _hotkeyGo = new GameObject("CC6_Hotkeys");
            _hotkeyGo.AddComponent<HotkeyHandler>();
            DontDestroyOnLoad(_hotkeyGo);
        }
    }
}
