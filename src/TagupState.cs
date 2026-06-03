using System;
using UnityEngine;

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
    /// only when it CROSSES THE BLUE LINE INTO THE SCORING HALF WHILE IN POSSESSION — having first
    /// controlled the puck in the back court (cleared). A puck that crosses the line loose (a dump or
    /// shot) tags nobody, and simply recovering a loose puck inside the zone does NOT tag up: the team
    /// has to take it back out past the line and bring it in under control. A turnover clears the new
    /// team's eligibility, and the puck leaving the scoring half un-tags everyone.
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

        /// <summary>HUD-facing possessor: the controlling team, held briefly after the puck goes loose
        /// then None ("contested"). Debounced so a quick pass does not flash CONTESTED.</summary>
        internal static PlayerTeam HudPossessor = PlayerTeam.None;

        /// <summary>HUD-facing side: which half the HUD possessor had the puck in.</summary>
        internal static HalfSide HudSide = HalfSide.BackCourt;

        /// <summary>Which half the puck is in (last definite side; ignores the line band).</summary>
        internal static HalfSide CurrentSide = HalfSide.BackCourt;

        // Last definite side, for detecting the back-court -> scoring-half crossing.
        private static HalfSide _lastSide = HalfSide.BackCourt;

        // Team that controlled the puck as it last entered the scoring half (None = a loose entry /
        // dump). Only this team can tag up on this entry; reset when the puck goes back to the back court.
        private static PlayerTeam _entryControlledBy = PlayerTeam.None;

        // Time.unscaledTime the puck went loose (-1 while controlled), for the CONTESTED debounce.
        private static float _looseSince = -1f;
        private const float ContestedGraceSeconds = 0.35f;

        internal static void Reset() {
            BlueEligible = RedEligible = false;
            BlueCleared = RedCleared = false;
            LastPossessionTeam = PlayerTeam.None;
            LastPossessorSteamId = "";
            HudPossessor = PlayerTeam.None;
            HudSide = HalfSide.BackCourt;
            CurrentSide = HalfSide.BackCourt;
            _lastSide = HalfSide.BackCourt;
            _entryControlledBy = PlayerTeam.None;
            _looseSince = -1f;
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
        /// <param name="possessor">Team actually controlling the puck this frame (near it + under control), or None when it is loose / contested / in flight.</param>
        /// <param name="possessorSteamId">Steam id of that possessor (for goal credit).</param>
        /// <param name="side">Which half the puck is currently in.</param>
        /// <param name="announce">Sink for "ref" chat messages (message, isWarning).</param>
        internal static void OnFrame(PlayerTeam possessor, string possessorSteamId, HalfSide side,
            Action<string, bool> announce) {

            if (side != HalfSide.Unknown) CurrentSide = side;

            // HUD: debounced possessor/side so a quick pass does not flash CONTESTED.
            if (possessor != PlayerTeam.None) {
                HudPossessor = possessor;
                HudSide = CurrentSide;
                _looseSince = -1f;
            }
            else {
                if (_looseSince < 0f) _looseSince = Time.unscaledTime;
                if (Time.unscaledTime - _looseSince >= ContestedGraceSeconds) HudPossessor = PlayerTeam.None;
            }

            // Possession bookkeeping (turnover resets the new team; track last controlling possessor).
            // A loose puck is None, so it never updates this — possession only changes hands when a
            // team actually corrals the puck, which is exactly the turnover signal we want.
            if (possessor != PlayerTeam.None) {
                if (LastPossessionTeam != PlayerTeam.None && possessor != LastPossessionTeam) {
                    SetEligible(possessor, false);
                    SetCleared(possessor, false);
                }
                LastPossessionTeam = possessor;
                if (!string.IsNullOrEmpty(possessorSteamId)) LastPossessorSteamId = possessorSteamId;
            }

            if (side == HalfSide.BackCourt) {
                // Out of the scoring half: nobody is tagged up, and this entry is over — to tag up a
                // team must cross the line again in possession. Controlling the puck here clears the team.
                BlueEligible = false;
                RedEligible = false;
                _entryControlledBy = PlayerTeam.None;
                if (possessor != PlayerTeam.None && !IsCleared(possessor))
                    SetCleared(possessor, true);
            }
            else if (side == HalfSide.ScoringHalf) {
                // Record who controlled the puck as it crossed INTO the scoring half. None = it crossed
                // loose (a dump/shot), which tags nobody and cannot be salvaged by recovering it inside.
                if (_lastSide == HalfSide.BackCourt)
                    _entryControlledBy = possessor;

                // Tag up only the team that brought the puck across the line under control (and had
                // cleared it first). Re-checked each frame so it still fires if they enter a hair before
                // the "cleared" flag settles, but a loose entry (_entryControlledBy == None) never tags.
                if (_entryControlledBy != PlayerTeam.None && possessor == _entryControlledBy &&
                    IsCleared(possessor) && !IsEligible(possessor)) {
                    SetEligible(possessor, true);
                    announce($"{TeamFunc.Name(possessor)} is TAGGED UP, live to score!", false);
                }
            }

            if (side != HalfSide.Unknown) _lastSide = side;
        }
    }
}
