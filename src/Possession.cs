using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Tagup {
    /// <summary>
    /// Server-side puck-possession resolver. Fed from the puck's stick collisions and resolved
    /// each frame. Mirrors the game-mod community's GetPlayerSteamIdInPossession approach: a
    /// player "possesses" the puck once they have kept continuous stick contact for at least
    /// MinPossession ms, and loses it MaxPossession ms after their last contact. When two
    /// players both qualify, possession is treated as contested (nobody).
    ///
    /// All calls happen on the Unity main thread (physics callbacks + PhysicsManager.Update),
    /// so plain dictionaries are fine.
    /// </summary>
    internal static class Possession {
        /// <summary>Stopwatch since the player's current continuous touch began (steamId keyed).</summary>
        private static readonly Dictionary<string, Stopwatch> _touch = new Dictionary<string, Stopwatch>();

        /// <summary>Stopwatch since the player's last stick contact (steamId keyed).</summary>
        private static readonly Dictionary<string, Stopwatch> _lastContact = new Dictionary<string, Stopwatch>();

        /// <summary>Last known team for each steamId.</summary>
        private static readonly Dictionary<string, PlayerTeam> _team = new Dictionary<string, PlayerTeam>();

        /// <summary>Time since ANY stick last touched the puck (distinguishes a carry from a shot in flight).</summary>
        private static readonly Stopwatch _lastTouch = new Stopwatch();

        /// <summary>Set from config on enable; used to detect a re-acquired puck.</summary>
        internal static int MaxPossessionMs = 500;

        /// <summary>Milliseconds since any stick last touched the puck (MaxValue if never).</summary>
        internal static long MsSinceLastTouch => _lastTouch.IsRunning ? _lastTouch.ElapsedMilliseconds : long.MaxValue;

        internal static void OnStickContact(Player player) {
            if (player == null) return;
            string id = player.SteamId.Value.ToString();
            if (string.IsNullOrEmpty(id)) return;

            if (!_lastContact.TryGetValue(id, out Stopwatch contact)) {
                contact = Stopwatch.StartNew();
                _lastContact[id] = contact;
            }

            if (!_touch.TryGetValue(id, out Stopwatch touch)) {
                _touch[id] = Stopwatch.StartNew();
            }
            else if (contact.ElapsedMilliseconds > MaxPossessionMs) {
                // The puck came back after being away long enough to count as lost — fresh touch.
                touch.Restart();
            }

            contact.Restart();
            _lastTouch.Restart();
            _team[id] = player.Team;
        }

        internal static void OnStickExit(Player player) {
            if (player == null) return;
            string id = player.SteamId.Value.ToString();
            if (_lastContact.TryGetValue(id, out Stopwatch contact)) contact.Restart();
        }

        /// <summary>Resolved steam id in possession, or "" if nobody / contested.</summary>
        internal static string GetPossessorSteamId(int minMs, int maxMs) {
            Dictionary<string, Stopwatch> live = _touch
                .Where(kvp => !_lastContact.TryGetValue(kvp.Key, out Stopwatch c) || c.ElapsedMilliseconds <= maxMs)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            if (live.Count == 1) return live.First().Key;

            List<KeyValuePair<string, Stopwatch>> settled =
                live.Where(kvp => kvp.Value.ElapsedMilliseconds > minMs).ToList();

            if (settled.Count == 1) return settled[0].Key;
            return ""; // nobody, or possession is contested
        }

        internal static PlayerTeam GetPossessorTeam(int minMs, int maxMs) {
            string id = GetPossessorSteamId(minMs, maxMs);
            if (!string.IsNullOrEmpty(id) && _team.TryGetValue(id, out PlayerTeam team)) return team;
            return PlayerTeam.None;
        }

        internal static void Clear() {
            _touch.Clear();
            _lastContact.Clear();
            _team.Clear();
            _lastTouch.Reset();
        }
    }
}
