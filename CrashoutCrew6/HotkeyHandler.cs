using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CrashoutCrew6
{
    /// <summary>
    /// Runs the solo-preview hotkeys from its own persistent GameObject. We don't rely on the
    /// BepInEx plugin's own Update() because this game clears DontDestroyOnLoad objects on boot,
    /// which can stop the plugin component from ticking. This object is (re)created on scene load.
    /// </summary>
    internal class HotkeyHandler : MonoBehaviour
    {
        private bool _logged;

        private void Update()
        {
            if (!_logged) { _logged = true; Log.Info("HotkeyHandler active — preview hotkeys live."); }
            if (ModConfig.EnablePreview == null || !ModConfig.EnablePreview.Value) return;

            try
            {
                if (Pressed(ModConfig.PreviewReportKey.Value))
                {
                    Log.Info("Preview report key pressed.");
                    SixPlayerPreview.PreviewReport();
                }
                if (Pressed(ModConfig.PreviewLobbyKey.Value))
                {
                    Log.Info("Preview lobby key pressed.");
                    SixPlayerPreview.PreviewLobby();
                }
            }
            catch (Exception e)
            {
                Log.Error("Preview hotkey failed: " + e);
            }
        }

        /// <summary>True if the key went down this frame, trying the new Input System then legacy Input.</summary>
        private static bool Pressed(Key key)
        {
            if (key == Key.None) return false;

            // New Input System
            try
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    var c = kb[key];
                    if (c != null && c.wasPressedThisFrame) return true;
                }
            }
            catch { /* input system may be unavailable in this config */ }

            // Legacy Input fallback (throws if the project is "new input only" — swallowed)
            try
            {
                if (TryLegacy(key, out var kc) && Input.GetKeyDown(kc)) return true;
            }
            catch { }

            return false;
        }

        private static bool TryLegacy(Key key, out KeyCode kc)
        {
            kc = KeyCode.None;
            try { kc = (KeyCode)Enum.Parse(typeof(KeyCode), key.ToString()); return true; }
            catch { return false; }
        }
    }
}
