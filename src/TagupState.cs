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

        // --- Turnover guard: a new team must control the puck for this long before it counts as a
        // turnover (and updates the goal-credit possessor), so a tip / deflection / ghost touch on the
        // way to the net cannot briefly read as the other team and steal the goal from the shooter. ---
        internal static float TurnoverControlSeconds = 0.15f;
        private static PlayerTeam _pendingTeam = PlayerTeam.None;
        private static float _pendingSince = 0f;

        // --- Carry tracking: the team in continuous control, held across brief stick-contact gaps (a
        // slow stickhandle / fake still counts), but dropped the instant the puck is in flight (a
        // shot/dump) or control lapses past the grace. Drives clearing + the controlled-entry check. ---
        internal static float EntryGraceSeconds = 0.4f;
        private static PlayerTeam _carryTeam = PlayerTeam.None;
        private static float _carryExpiry = -1f;

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
            _pendingTeam = PlayerTeam.None;
            _pendingSince = 0f;
            _carryTeam = PlayerTeam.None;
            _carryExpiry = -1f;
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
        /// <param name="puckSpeed">Puck speed (units/s); above <see cref="Possession.ControlSpeed"/> the puck is in flight (a shot/dump), which ends a carry at once.</param>
        /// <param name="announce">Sink for "ref" chat messages (message, isWarning).</param>
        internal static void OnFrame(PlayerTeam possessor, string possessorSteamId, HalfSide side,
            float puckSpeed, Action<string, bool> announce) {

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

            // Continuous-carry team: held through brief stick-contact gaps (a slow stickhandle / fake
            // still counts as carrying), but dropped the instant the puck is in flight (a shot/dump) or
            // control lapses past the grace window. This is what drives clearing + the entry check.
            bool puckInFlight = puckSpeed > Possession.ControlSpeed;
            if (possessor != PlayerTeam.None) {
                _carryTeam = possessor;
                _carryExpiry = Time.unscaledTime + EntryGraceSeconds;
            }
            else if (puckInFlight || Time.unscaledTime > _carryExpiry) {
                _carryTeam = PlayerTeam.None;
            }

            // Possession bookkeeping for goal credit. A new team only takes over after holding control
            // for TurnoverControlSeconds, so a tip / deflection / ghost touch that reads as the other
            // team for an instant cannot flip the credit away from the shooter. Same-team frames refresh
            // the scorer id immediately; a loose puck cancels any half-formed turnover.
            if (possessor != PlayerTeam.None) {
                if (possessor == LastPossessionTeam || LastPossessionTeam == PlayerTeam.None) {
                    CommitPossession(possessor, possessorSteamId);
                }
                else {
                    if (possessor != _pendingTeam) { _pendingTeam = possessor; _pendingSince = Time.unscaledTime; }
                    if (Time.unscaledTime - _pendingSince >= TurnoverControlSeconds) {
                        SetEligible(possessor, false);   // a real turnover: the new team must re-tag
                        SetCleared(possessor, false);
                        CommitPossession(possessor, possessorSteamId);
                    }
                }
            }
            else {
                _pendingTeam = PlayerTeam.None;
            }

            if (side == HalfSide.BackCourt) {
                // Out of the scoring half: nobody is tagged up, and this entry is over — to tag up a
                // team must cross the line again in possession. Carrying the puck here clears the team.
                BlueEligible = false;
                RedEligible = false;
                _entryControlledBy = PlayerTeam.None;
                if (_carryTeam != PlayerTeam.None && !IsCleared(_carryTeam))
                    SetCleared(_carryTeam, true);
            }
            else if (side == HalfSide.ScoringHalf) {
                // Record who CARRIED the puck across the line. None = it crossed loose (a dump/shot),
                // which tags nobody and cannot be salvaged by recovering it inside the zone.
                if (_lastSide == HalfSide.BackCourt)
                    _entryControlledBy = _carryTeam;

                // Tag up only the team that brought the puck across under control (and had cleared it
                // first). Keyed on _carryTeam so a brief dangle gap just inside the line still tags;
                // a loose entry (_entryControlledBy == None) never does.
                if (_entryControlledBy != PlayerTeam.None && _carryTeam == _entryControlledBy &&
                    IsCleared(_carryTeam) && !IsEligible(_carryTeam)) {
                    SetEligible(_carryTeam, true);
                    announce($"{TeamFunc.Name(_carryTeam)} is TAGGED UP, live to score!", false);
                }
            }

            if (side != HalfSide.Unknown) _lastSide = side;
        }

        // Commit the controlling team as the last possessor for goal credit, and clear any pending turnover.
        private static void CommitPossession(PlayerTeam team, string steamId) {
            LastPossessionTeam = team;
            if (!string.IsNullOrEmpty(steamId)) LastPossessorSteamId = steamId;
            _pendingTeam = PlayerTeam.None;
        }
    }
}
