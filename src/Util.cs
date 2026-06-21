using System;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tagup {
    /// <summary>Server-role helpers.</summary>
    internal static class Srv {
        /// <summary>
        /// True when this instance owns authoritative game state (dedicated server or listen host).
        /// All gameplay-altering patches gate on this so pure clients stay passive.
        /// </summary>
        internal static bool IsServer =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>True on a headless dedicated server (no display to render a HUD to).</summary>
        internal static bool IsDedicated => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    /// <summary>
    /// Coexistence with other server-side mods. Right now this only concerns the oomtm450 "Ruleset"
    /// mod: Ruleset reads and LOGS the engine period clock (it pauses/resumes ticking and
    /// saves/restores the time remaining across stoppages, and logs the time on every phase change).
    /// So Tagup must not freeze that shared clock while Ruleset is present, or those logs become
    /// meaningless. Detection is by loaded-assembly name. Note this stays true once Ruleset has
    /// merely been loaded (the DLL is not unloaded when the mod is disabled) — which is the right
    /// signal for "this server runs Ruleset".
    /// </summary>
    internal static class Compat {
        private static bool _rulesetFound;   // latches true on first sighting (the DLL never unloads)
        private static float _nextScan;      // throttles re-scans while it has not been found yet

        /// <summary>
        /// True once the Ruleset mod's assembly is loaded in this process. Latches on first
        /// detection; while absent it re-scans at most every few seconds so a Ruleset enabled AFTER
        /// Tagup (either load order) is still picked up, without walking the AppDomain every frame on
        /// the common no-Ruleset server.
        /// </summary>
        internal static bool RulesetPresent {
            get {
                if (_rulesetFound) return true;
                if (Time.unscaledTime < _nextScan) return false;
                _nextScan = Time.unscaledTime + 5f;
                _rulesetFound = IsAssemblyLoaded(Constants.RULESET_ASSEMBLY);
                return _rulesetFound;
            }
        }

        /// <summary>
        /// True when Tagup should keep the period CLOCK running for the Ruleset mod instead of
        /// freezing it (it loops the clock so the game never ends on time — see the Server_Tick
        /// patch) so Ruleset's time logging, which reads the same engine clock, stays accurate. Tagup
        /// keeps its own scoring and its first-to-N win (that game-over is log-safe: one honest
        /// GameOver line). An operator can force Tagup's clock freeze back on by setting
        /// <see cref="TagupConfig.YieldToRuleset"/> to false.
        /// </summary>
        internal static bool YieldClock(TagupConfig cfg) =>
            cfg != null && cfg.YieldToRuleset && RulesetPresent;

        private static bool IsAssemblyLoaded(string name) {
            try {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                    if (string.Equals(asm.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { /* enumerating the AppDomain is best-effort; never let it break a patch */ }
            return false;
        }
    }

    /// <summary>Small team helpers (the game has its own but it is internal to Puck.dll).</summary>
    internal static class TeamFunc {
        internal static PlayerTeam Other(PlayerTeam team) {
            if (team == PlayerTeam.Blue) return PlayerTeam.Red;
            if (team == PlayerTeam.Red) return PlayerTeam.Blue;
            return PlayerTeam.None;
        }

        internal static string Name(PlayerTeam team) => team.ToString().ToUpperInvariant();
    }
}
