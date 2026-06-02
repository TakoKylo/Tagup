using System;

namespace Tagup {
    /// <summary>Which half of the rink the puck is in, relative to the tag-up line.</summary>
    internal enum HalfSide {
        /// <summary>Inside the line margin — ambiguous, ignore for state transitions.</summary>
        Unknown,
        /// <summary>Behind the tag-up line (the clearing / build-up half).</summary>
        BackCourt,
        /// <summary>Past the tag-up line, toward the live net.</summary>
        ScoringHalf,
    }

    /// <summary>
    /// The make-it-take-it state machine (server-side). A team is "tagged up" (eligible to score)
    /// only after it has controlled the puck in the back court and then CARRIED it across the
    /// tag-up line — the puck must be on a stick as it crosses. A puck shot or dumped in tags
    /// nobody; both teams have to take it in. A turnover clears the new team's eligibility, and the
    /// puck leaving the scoring half un-tags everyone.
    /// </summary>
    internal static class TagupState {
        // Tagged up (allowed to score).
        internal static bool BlueEligible;
        internal static bool RedEligible;

        // Has had clear possession in the back court since the last turnover (allowed to tag up).
        internal static bool BlueCleared;
        internal static bool RedCleared;

        /// <summary>Last team to hold clear possession — the presumed shooter on a goal.</summary>
        internal static PlayerTeam LastPossessionTeam = PlayerTeam.None;

        /// <summary>Steam id of the last clear possessor — used to credit the goal.</summary>
        internal static string LastPossessorSteamId = "";

        // Last definite half the puck was in (ignores the Unknown line band), for crossing detection.
        private static HalfSide _lastDefiniteSide = HalfSide.BackCourt;

        // True when the puck's most recent entry into the scoring half was under stick control
        // (a carry) rather than a shot/dump. Only a controlled entry can produce a tag-up.
        private static bool _zoneEntryControlled;

        internal static void Reset() {
            BlueEligible = RedEligible = false;
            BlueCleared = RedCleared = false;
            LastPossessionTeam = PlayerTeam.None;
            LastPossessorSteamId = "";
            _lastDefiniteSide = HalfSide.BackCourt;
            _zoneEntryControlled = false;
        }

        internal static bool IsEligible(PlayerTeam team) =>
            team == PlayerTeam.Blue ? BlueEligible : team == PlayerTeam.Red && RedEligible;

        private static void SetEligible(PlayerTeam team, bool value) {
            if (team == PlayerTeam.Blue) BlueEligible = value;
            else if (team == PlayerTeam.Red) RedEligible = value;
        }

        private static bool IsCleared(PlayerTeam team) =>
            team == PlayerTeam.Blue ? BlueCleared : team == PlayerTeam.Red && RedCleared;

        private static void SetCleared(PlayerTeam team, bool value) {
            if (team == PlayerTeam.Blue) BlueCleared = value;
            else if (team == PlayerTeam.Red) RedCleared = value;
        }

        /// <summary>
        /// Advance the state machine for one server frame in Play.
        /// </summary>
        /// <param name="possessor">Team with settled possession this frame, or None.</param>
        /// <param name="possessorSteamId">Steam id of that possessor (for goal credit).</param>
        /// <param name="side">Which half the puck is currently in.</param>
        /// <param name="puckCarried">True if the puck is on a stick right now (or was a moment ago) — a carry, not a shot.</param>
        /// <param name="announce">Sink for "ref" chat messages (message, isWarning).</param>
        internal static void OnFrame(PlayerTeam possessor, string possessorSteamId, HalfSide side,
            bool puckCarried, Action<string, bool> announce) {

            // Possession bookkeeping (turnover resets the new team; track last clear possessor).
            if (possessor != PlayerTeam.None) {
                if (LastPossessionTeam != PlayerTeam.None && possessor != LastPossessionTeam) {
                    SetEligible(possessor, false);
                    SetCleared(possessor, false);
                }
                LastPossessionTeam = possessor;
                if (!string.IsNullOrEmpty(possessorSteamId)) LastPossessorSteamId = possessorSteamId;
            }

            if (side == HalfSide.BackCourt) {
                // Out of the scoring half: nobody is tagged up, and the entry is no longer "live".
                BlueEligible = false;
                RedEligible = false;
                _zoneEntryControlled = false;
                if (possessor != PlayerTeam.None && !IsCleared(possessor)) SetCleared(possessor, true);
            }
            else if (side == HalfSide.ScoringHalf) {
                // Record whether the puck crossed INTO the scoring half under control (a carry).
                if (_lastDefiniteSide == HalfSide.BackCourt)
                    _zoneEntryControlled = puckCarried;

                // Tag up only if the puck was carried in and the possessing team had cleared it.
                if (_zoneEntryControlled && possessor != PlayerTeam.None &&
                    IsCleared(possessor) && !IsEligible(possessor)) {
                    SetEligible(possessor, true);
                    announce($"{TeamFunc.Name(possessor)} is TAGGED UP — live to score!", false);
                }
            }

            if (side != HalfSide.Unknown) _lastDefiniteSide = side;
        }
    }
}
