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
