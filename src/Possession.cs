using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Tagup {
    /// <summary>
    /// Server-side puck-possession resolver, modelled on the Ruleset mod
    /// (<c>PlayerFunc.GetPlayerSteamIdInPossession</c> / <c>PuckFunc.PuckIsTipped</c>). The idea that
    /// makes it robust: a brief glance off a stick is a TIP and is ignored — only a touch that lasts
    /// at least <see cref="MaxTippedMs"/> with the puck on the ice (not airborne) counts as real
    /// possession. The last player to take real possession is then remembered (<see cref="LastTeam"/>
    /// / <see cref="LastSteamId"/>) so it survives the puck being in flight or a goal-mouth scramble,
    /// instead of flickering to "nobody" every time the puck leaves the blade. A different player
    /// taking real control is a clean turnover: the previous owner's touch timer is zeroed.
    ///
    /// All calls happen on the Unity main thread (physics callbacks + PhysicsManager.Update), so plain
    /// dictionaries are fine.
    /// </summary>
    internal static class Possession {
        /// <summary>Stopwatch since the player's current continuous touch began (steamId keyed).</summary>
        private static readonly Dictionary<string, Stopwatch> _touch = new Dictionary<string, Stopwatch>();

        /// <summary>Stopwatch since the player's last stick contact — restarted on every stay/exit.</summary>
        private static readonly Dictionary<string, Stopwatch> _lastContact = new Dictionary<string, Stopwatch>();

        /// <summary>Time since the puck was last under real (non-tipped) stick control.</summary>
        private static readonly Stopwatch _lastTouch = new Stopwatch();

        /// <summary>Team of the last player to take real possession (sticky; survives flight/scrambles).</summary>
        internal static PlayerTeam LastTeam = PlayerTeam.None;

        /// <summary>Steam id of the last player to take real possession (sticky) — used to credit goals.</summary>
        internal static string LastSteamId = "";

        // ---- Tunables (set from config on enable) -----------------------------------------------
        /// <summary>Puck away from a player longer than this (ms) counts as a lost puck (fresh touch).</summary>
        internal static int MaxPossessionMs = 500;

        /// <summary>A stick contact shorter than this (ms), on the ice, is a tip — ignored for possession.</summary>
        internal static int MaxTippedMs = 80;

        /// <summary>Puck centre above this height is airborne, so any contact with it is a tip/deflection.</summary>
        internal static float PuckOnIceHeight = 0.25f;

        /// <summary>The possessor keeps the puck only while it stays within this horizontal distance of them.</summary>
        internal static float PossessionRadius = 2.5f;

        /// <summary>A slow puck within this horizontal distance of a lone skater's blade is under their
        /// control even with no stick contact this instant (slow stickhandle / fake).</summary>
        internal static float ControlRadius = 1.5f;

        /// <summary>Puck faster than this (units/s) is a shot/loose puck in flight, not under control.</summary>
        internal static float ControlSpeed = 14f;

        /// <summary>Milliseconds since the puck was last under real stick control (MaxValue if never).</summary>
        internal static long MsSinceLastTouch => _lastTouch.IsRunning ? _lastTouch.ElapsedMilliseconds : long.MaxValue;

        private static string Id(Player player) {
            if (player == null || player.SteamId == null) return null;
            string id = player.SteamId.Value.ToString();
            return string.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>A stick first touches the puck this frame (OnCollisionEnter): start/refresh its timers.</summary>
        internal static void OnContactBegin(Player player) {
            string id = Id(player);
            if (id == null) return;

            if (!_touch.TryGetValue(id, out Stopwatch touch)) {
                touch = Stopwatch.StartNew();
                _touch[id] = touch;
            }

            if (!_lastContact.TryGetValue(id, out Stopwatch contact)) {
                _lastContact[id] = Stopwatch.StartNew();
            }
            else if (contact.ElapsedMilliseconds > MaxPossessionMs ||
                     (!string.IsNullOrEmpty(LastSteamId) && LastSteamId != id)) {
                // Fresh acquisition: the puck was away long enough, or a different player had it last.
                // Restart this player's continuous-touch timer and zero the previous owner's, so the
                // tip threshold is measured from this acquisition and possession hands off cleanly.
                touch.Restart();
                if (!string.IsNullOrEmpty(LastSteamId) && LastSteamId != id &&
                    _touch.TryGetValue(LastSteamId, out Stopwatch prev))
                    prev.Reset();
            }
        }

        /// <summary>
        /// Ongoing or ending stick contact (OnCollisionStay / OnCollisionExit). Refreshes the
        /// last-contact timer and, when the contact is real possession (not a fleeting tip / airborne
        /// deflection) or a continuation of the current owner's, records the sticky possessor.
        /// </summary>
        internal static void OnContact(Player player, float puckY) {
            string id = Id(player);
            if (id == null) return;

            if (!_touch.TryGetValue(id, out Stopwatch touch)) {
                touch = Stopwatch.StartNew();
                _touch[id] = touch;
            }
            if (!_lastContact.TryGetValue(id, out Stopwatch contact)) {
                contact = new Stopwatch();
                _lastContact[id] = contact;
            }
            contact.Restart();

            if (id == LastSteamId || !IsTip(touch, contact, puckY)) {
                LastTeam = player.Team;
                LastSteamId = id;
                _lastTouch.Restart();
            }
        }

        // A contact is a tip if the puck is airborne, or the continuous contact has not yet lasted
        // MaxTippedMs. _lastContact was just restarted, so (touch - contact) is the contact's duration.
        private static bool IsTip(Stopwatch touch, Stopwatch contact, float puckY) {
            if (puckY > PuckOnIceHeight) return true;
            return touch.ElapsedMilliseconds - contact.ElapsedMilliseconds < MaxTippedMs;
        }

        /// <summary>
        /// The team actually controlling the puck this frame, or None when it is loose / contested.
        /// Two ways to control it, both gated by speed (a shot/dump/hard pass in flight is nobody's):
        ///   1. The sticky last-real-toucher, while they touched it recently AND it is still within
        ///      <see cref="PossessionRadius"/> of them.
        ///   2. Proximity fallback: a slow puck hugging one skater's blade (within
        ///      <see cref="ControlRadius"/>, with no opponent equally close) is under their control
        ///      even with no stick contact this instant — a slow stickhandle / fake move still counts.
        /// The fallback keys off whoever is NEAREST the puck, so it never revives a far-away dumper:
        /// a dump-in is still loose at the line, and a goal-mouth scramble is still contested (None).
        /// </summary>
        internal static PlayerTeam Resolve(Vector3 puckPos, float puckSpeed, out string steamId) {
            steamId = "";
            if (puckSpeed > ControlSpeed) return PlayerTeam.None;             // shot / loose puck in flight

            // 1. Sticky last real-toucher, still recent and still near them.
            if (!string.IsNullOrEmpty(LastSteamId) && MsSinceLastTouch <= MaxPossessionMs) {
                Player p = PlayerManager.Instance != null ? PlayerManager.Instance.GetPlayerBySteamId(LastSteamId) : null;
                if (p && p.PlayerBody && p.PlayerBody.Rigidbody) {
                    Vector3 body = p.PlayerBody.Rigidbody.position;
                    float dx = body.x - puckPos.x, dz = body.z - puckPos.z;
                    if (dx * dx + dz * dz <= PossessionRadius * PossessionRadius) {
                        steamId = LastSteamId;
                        return LastTeam;
                    }
                }
            }

            // 2. Proximity fallback (handles slow stickhandle / fake with no qualifying stick contact).
            return ResolveByProximity(puckPos, out steamId);
        }

        // The nearest non-goalie skater whose blade is within ControlRadius of the puck owns it — unless
        // an opponent is also within reach, in which case it is a contested battle and belongs to nobody.
        private static PlayerTeam ResolveByProximity(Vector3 puckPos, out string steamId) {
            steamId = "";
            // Proximity = a carry on the ice. An airborne puck (flip-in / bounce) near a skater is not
            // under control, so it stays loose — keeps a flip-in over the line from tagging like a carry.
            if (puckPos.y > PuckOnIceHeight) return PlayerTeam.None;
            PlayerManager pm = PlayerManager.Instance;
            if (pm == null) return PlayerTeam.None;

            float r2 = ControlRadius * ControlRadius;
            List<Player> players = pm.GetSpawnedPlayers();
            if (players == null) return PlayerTeam.None;

            Player nearest = null;
            float nearestD2 = float.MaxValue;
            foreach (Player p in players) {
                if (!p || p.Role == PlayerRole.Goalie) continue;
                if (!TryControlPos(p, out Vector3 sp)) continue;
                float dx = sp.x - puckPos.x, dz = sp.z - puckPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < nearestD2) { nearestD2 = d2; nearest = p; }
            }
            if (nearest == null || nearestD2 > r2) return PlayerTeam.None;

            PlayerTeam team = nearest.Team;
            foreach (Player p in players) {
                if (!p || p == nearest || p.Role == PlayerRole.Goalie || p.Team == team) continue;
                if (!TryControlPos(p, out Vector3 sp)) continue;
                float dx = sp.x - puckPos.x, dz = sp.z - puckPos.z;
                if (dx * dx + dz * dz <= r2) return PlayerTeam.None;   // an opponent is contesting it
            }

            string id = Id(nearest);
            if (id == null) return PlayerTeam.None;
            steamId = id;
            return team;
        }

        // The point we measure puck distance from: the stick blade (so stickhandling counts), falling
        // back to the body when the stick is not available.
        private static bool TryControlPos(Player player, out Vector3 pos) {
            pos = default;
            if (player.Stick) { pos = player.Stick.BladeHandlePosition; return true; }
            if (player.PlayerBody && player.PlayerBody.Rigidbody) { pos = player.PlayerBody.Rigidbody.position; return true; }
            return false;
        }

        internal static void Clear() {
            _touch.Clear();
            _lastContact.Clear();
            _lastTouch.Reset();
            LastTeam = PlayerTeam.None;
            LastSteamId = "";
        }
    }
}
