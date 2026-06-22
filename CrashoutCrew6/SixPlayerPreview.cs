using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Aggro.Core;
using HarmonyLib;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CrashoutCrew6
{
    /// <summary>
    /// Solo preview helpers so the extended lobby / report UI can be eyeballed without 6 real players.
    /// These are deliberately lightweight (no full game state needed) and fully guarded.
    /// </summary>
    internal static class SixPlayerPreview
    {
        /// <summary>Pop the end-of-contract report filled with Max dummy player rows.</summary>
        internal static void PreviewReport()
        {
            var report = AggroManagerBase<ReportUI>.instance;
            if (report == null) report = UnityEngine.Object.FindObjectOfType<ReportUI>(true);
            if (report == null)
            {
                Log.Warn("PreviewReport: ReportUI not found. Start (or be inside) a run so it exists, then press the key again.");
                return;
            }
            Log.Info("PreviewReport: populating 6 dummy rows on " + report.name);

            int n = ModConfig.Max;

            // make sure the rows exist (idempotent; clones happen once)
            UiRowExpander.Expand(report, ReportFields, n, ModConfig.ReportSquish.Value, ModConfig.ReportRowScale.Value, "ReportUI(preview)");
            Patches.ReportUiFields.NudgeUp(report);

            ActivateAll(report, "playerIconObjects", n);
            ActivateAll(report, "crashoutCountObjects", n);
            ActivateAll(report, "nitroCountObjects", n);
            ActivateAll(report, "driftCountObjects", n);
            ActivateAll(report, "upgradeCountObjects", n);

            var names = GetList<TextMeshProUGUI>(report, "playerNameTexts");
            var icons = GetList<Image>(report, "playerIconImages");
            var highlights = GetList<Image>(report, "playerHighlightImages");
            var crashout = GetList<TextMeshProUGUI>(report, "crashoutCountTexts");
            var nitro = GetList<TextMeshProUGUI>(report, "nitroCountTexts");
            var drift = GetList<TextMeshProUGUI>(report, "driftCountTexts");
            var upgrade = GetList<TextMeshProUGUI>(report, "upgradeCountTexts");

            for (int i = 0; i < n; i++)
            {
                Color c = Color.HSVToRGB(i / (float)n, 0.7f, 1f);
                SetText(names, i, "Player " + (i + 1), c * 0.5f);
                SetColor(icons, i, c);
                if (highlights != null && i < highlights.Count && highlights[i] != null)
                    highlights[i].color = new Color(c.r * 0.9f, c.g * 0.9f, c.b * 0.9f, 0.5f);
                SetText(crashout, i, (3 + i).ToString(), null);
                SetText(nitro, i, (10 + i).ToString(), null);
                SetText(drift, i, (100 + 10 * i) + "m", null);
                SetText(upgrade, i, (1 + i).ToString(), null);
            }

            TrySetAnimatorBool(report, "show", true);
            Log.Info($"PreviewReport: populated {n} dummy player rows. Inspect the layout; tune with UI.ReportRowScale / ReportSquish.");
        }

        /// <summary>Force every lobby parking spot (including the 2 new ones) to show as occupied.</summary>
        internal static void PreviewLobby()
        {
            var spots = Object.FindObjectsOfType<LobbyPlayer>();
            if (spots == null || spots.Length == 0)
            {
                Log.Warn("PreviewLobby: no LobbyPlayer spots found. Enter a lobby first.");
                return;
            }

            int assigned = 0;
            foreach (var lp in spots)
            {
                try
                {
                    if (NetworkServer.active)
                    {
                        lp.ServerPlayerAssigned();
                    }
                    else
                    {
                        // client-side cosmetic: drive the assigned sync field directly
                        var f = AccessTools.Field(typeof(LobbyPlayer), "_syncHasPlayerAssigned");
                        f?.SetValue(lp, true);
                    }
                    assigned++;
                }
                catch (System.Exception e) { Log.Debug("PreviewLobby spot failed: " + e.Message); }
            }
            Log.Info($"PreviewLobby: marked {assigned} parking spot(s) occupied (found {spots.Length}).");
        }

        // ---- reflection helpers ----------------------------------------------

        private static readonly string[] ReportFields =
        {
            "playerIconObjects", "playerNameTexts",
            "crashoutCountObjects", "nitroCountObjects", "driftCountObjects", "upgradeCountObjects",
            "crashoutCountTexts", "nitroCountTexts", "driftCountTexts", "upgradeCountTexts",
            "playerIconImages", "playerHighlightImages",
        };

        private static List<T> GetList<T>(object host, string field) where T : class
        {
            var fi = AccessTools.Field(host.GetType(), field);
            return fi?.GetValue(host) as List<T>;
        }

        private static void ActivateAll(object host, string field, int n)
        {
            var list = GetList<GameObject>(host, field);
            if (list == null) return;
            for (int i = 0; i < n && i < list.Count; i++)
                if (list[i] != null) list[i].SetActive(true);
        }

        private static void SetText(List<TextMeshProUGUI> list, int i, string text, Color? color)
        {
            if (list == null || i >= list.Count || list[i] == null) return;
            list[i].text = text;
            if (color.HasValue) list[i].color = color.Value;
        }

        private static void SetColor(List<Image> list, int i, Color color)
        {
            if (list == null || i >= list.Count || list[i] == null) return;
            list[i].color = color;
        }

        private static void TrySetAnimatorBool(Component host, string param, bool value)
        {
            try
            {
                var fi = AccessTools.Field(host.GetType(), "animator");
                var anim = fi?.GetValue(host) as Animator;
                if (anim != null) anim.SetBool(param, value);
            }
            catch { /* non-fatal */ }
        }
    }
}
