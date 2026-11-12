using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace BlockStoryMod
{
    // logs under the mods name in LogOutput.log
    internal static class L
    {
        private static readonly ManualLogSource _s = Logger.CreateLogSource(typeof(L).Assembly.GetName().Name);
        private static readonly HashSet<string> _seen = new HashSet<string>();

        public static void Info(string m) => _s.LogInfo(m);
        public static void Warn(string m) => _s.LogWarning(m);

        // Update can fail 60x a sec so only log once
        public static void Err(string where, Exception e)
        {
            if (_seen.Add(where + e.GetType().Name + e.Message)) _s.LogError(where + ": " + e);
        }
    }
}
