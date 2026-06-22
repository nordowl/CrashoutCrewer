using BepInEx.Logging;

namespace CrashoutCrew6
{
    /// <summary>Tiny logging facade so call sites stay short and debug spam is gated by config.</summary>
    internal static class Log
    {
        internal static ManualLogSource Source;

        internal static void Init(ManualLogSource source) => Source = source;

        internal static void Info(string msg) => Source?.LogInfo(msg);
        internal static void Warn(string msg) => Source?.LogWarning(msg);
        internal static void Error(string msg) => Source?.LogError(msg);

        /// <summary>Verbose, only emitted when the user turns DebugLogging on.</summary>
        internal static void Debug(string msg)
        {
            if (ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value)
                Source?.LogInfo("[dbg] " + msg);
        }
    }
}
