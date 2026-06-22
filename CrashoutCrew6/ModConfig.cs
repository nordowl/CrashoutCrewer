using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace CrashoutCrew6
{
    /// <summary>
    /// Central, statically-reachable config so patches (which can't take constructor args)
    /// can read settings without plumbing references around.
    /// </summary>
    internal static class ModConfig
    {
        /// <summary>The new player cap. The game ships with 4; we extend the UI/lobby for up to 6.</summary>
        internal static ConfigEntry<int> MaxPlayers;

        /// <summary>Verbose logging for shaking out the 6-player paths.</summary>
        internal static ConfigEntry<bool> DebugLogging;

        /// <summary>Solo preview so the 6-spot lobby / 6-row report screens can be eyeballed without 6 real players.</summary>
        internal static ConfigEntry<bool> EnablePreview;
        internal static ConfigEntry<Key> PreviewReportKey;
        internal static ConfigEntry<Key> PreviewLobbyKey;

        /// <summary>Compress the report rows so 6 fit in the space the original 4 used.</summary>
        internal static ConfigEntry<bool> ReportSquish;
        /// <summary>Extra per-row scale applied after squishing (1 = unchanged); lower if rows overlap.</summary>
        internal static ConfigEntry<float> ReportRowScale;
        /// <summary>Moves the report clipboard up by this many UI units so the 6th row isn't cut off.</summary>
        internal static ConfigEntry<float> ReportYOffset;

        /// <summary>Hard ceiling the mod is built for (2 extra parking spots + 2 extra report rows).</summary>
        internal const int BuiltForMax = 6;
        internal const int VanillaMax = 4;

        internal static int Max => MaxPlayers != null ? Clamp(MaxPlayers.Value, VanillaMax, BuiltForMax) : BuiltForMax;

        internal static void Bind(ConfigFile cfg)
        {
            MaxPlayers = cfg.Bind("General", "MaxPlayers", BuiltForMax,
                new ConfigDescription("Maximum number of players. The mod is built for up to 6 (it adds 2 lobby parking spots and 2 report rows).",
                    new AcceptableValueRange<int>(VanillaMax, BuiltForMax)));

            DebugLogging = cfg.Bind("General", "DebugLogging", true,
                "Verbose log output for the 6-player code paths. Useful while testing; can be turned off later.");

            EnablePreview = cfg.Bind("Preview", "EnablePreview", true,
                "Enable the solo preview hotkeys below so the extended lobby/report UI can be checked without 6 real players.");

            PreviewReportKey = cfg.Bind("Preview", "PreviewReportKey", Key.F9,
                "Key that pops the end-of-contract report populated with 6 dummy players.");

            PreviewLobbyKey = cfg.Bind("Preview", "PreviewLobbyKey", Key.F10,
                "Key that forces all lobby parking spots (incl. the 2 new ones) to show as occupied.");

            ReportSquish = cfg.Bind("UI", "ReportSquish", true,
                "Compress the report player rows so all 6 fit in the space the original 4 occupied.");

            ReportRowScale = cfg.Bind("UI", "ReportRowScale", 1.0f,
                new ConfigDescription("Extra scale applied to each report row after squishing. Lower this (e.g. 0.85) if rows overlap.",
                    new AcceptableValueRange<float>(0.4f, 1.0f)));

            ReportYOffset = cfg.Bind("UI", "ReportYOffset", 45f,
                "Moves the end-of-contract report clipboard up by this many UI units so the 6th player's name isn't cut off.");
        }

        /// <summary>Parse a "x,y,z" config string into a position; returns false if empty/invalid.</summary>
        internal static bool TryParseVec3(string s, out UnityEngine.Vector3 v)
        {
            v = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split(',');
            if (parts.Length != 3) return false;
            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                v = new UnityEngine.Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
