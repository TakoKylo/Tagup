using UnityEngine;

namespace Tagup {
    /// <summary>
    /// Faceoff placement. After a goal the team that scored DEFENDS (lines up just inside the blue
    /// line with the net behind them), while the team that was scored on ATTACKS from the default
    /// (centre) faceoff spots facing the net — the puck stays on the default centre dot so their
    /// centre wins it. The opening faceoff (no previous scorer) is a neutral 90° centre draw.
    /// Goalies always go to the kept net's crease.
    /// </summary>
    internal static class Faceoff {
        /// <summary>Team that scored the most recent goal; drives the next restart. None = game start.</summary>
        internal static PlayerTeam LastScoringTeam = PlayerTeam.None;

        internal static void Reset() => LastScoringTeam = PlayerTeam.None;

        /// <summary>True after a goal: the conceding team is awarded the puck. False at game start.</summary>
        internal static bool IsPossessionRestart => LastScoringTeam != PlayerTeam.None;

        /// <summary>Attacking team = whoever was scored on (gets the puck).</summary>
        internal static PlayerTeam OffenseTeam => TeamFunc.Other(LastScoringTeam);

        /// <summary>Defending team = whoever scored.</summary>
        internal static PlayerTeam DefenseTeam => LastScoringTeam;

        /// <summary>Team that owns (defends) the kept net in vanilla terms.</summary>
        private static PlayerTeam NetOwner(TagupConfig cfg) => cfg.ActiveNetIsBlue ? PlayerTeam.Blue : PlayerTeam.Red;

        // Defenders line up just past the blue line, toward the net.
        private static float DefenseSignedZ(TagupConfig cfg) => cfg.TagLineZ + cfg.FaceoffDefenseFrontset;

        /// <summary>Place one player for the faceoff (goalies always go to the crease).</summary>
        internal static void PlaceSkater(Player player, TagupConfig cfg) {
            if (!player || !player.PlayerBody) return;
            if (player.Team != PlayerTeam.Blue && player.Team != PlayerTeam.Red) return;

            if (player.Role == PlayerRole.Goalie) {
                PlaceGoalie(player, cfg);
                return;
            }

            if (!IsPossessionRestart) {
                PlaceNeutral(player, cfg); // opening faceoff: neutral centre draw, nobody awarded the puck
                return;
            }

            if (player.Team == OffenseTeam)
                PlaceAttacker(player, cfg); // scored-on team: default spot facing the net
            else
                PlaceDefender(player, cfg); // scorer: blue line with the net behind them
        }

        /// <summary>
        /// Scored-on (attacking) team: keep the vanilla faceoff spot when it already faces the kept
        /// net (the "default red" layout); mirror the net-owner team across centre when it doesn't.
        /// The puck stays on the default centre dot, so this team's centre wins it.
        /// </summary>
        private static void PlaceAttacker(Player player, TagupConfig cfg) {
            if (player.Team == TeamFunc.Other(NetOwner(cfg)))
                return; // already facing the kept net — leave it at its default spot

            Transform body = player.PlayerBody.transform;
            Vector3 p = body.position;
            player.PlayerBody.Server_Teleport(
                new Vector3(p.x, p.y, -p.z),
                Quaternion.Euler(0f, 180f, 0f) * body.rotation);
        }

        /// <summary>Scoring (defending) team: line up just inside the blue line, net behind them.</summary>
        private static void PlaceDefender(Player player, TagupConfig cfg) {
            float z = cfg.ScoringSign * DefenseSignedZ(cfg);
            float x = SpreadX(player, cfg);
            float y = player.PlayerBody.transform.position.y;
            Quaternion rot = Quaternion.LookRotation(new Vector3(0f, 0f, -cfg.ScoringSign), Vector3.up);
            player.PlayerBody.Server_Teleport(new Vector3(x, y, z), rot);
        }

        /// <summary>
        /// Neutral opening faceoff: rotate the player's vanilla faceoff spot (and facing) about the
        /// centre dot by <see cref="TagupConfig.FaceoffRotationDegrees"/>, leaving a fair contest.
        /// </summary>
        private static void PlaceNeutral(Player player, TagupConfig cfg) {
            Vector3 center = new Vector3(0f, 0f, cfg.FaceoffZ);
            Quaternion yaw = Quaternion.Euler(0f, cfg.FaceoffRotationDegrees, 0f);

            Transform body = player.PlayerBody.transform;
            Vector3 flat = new Vector3(body.position.x, 0f, body.position.z);
            Vector3 rotated = center + yaw * (flat - center);

            player.PlayerBody.Server_Teleport(
                new Vector3(rotated.x, body.position.y, rotated.z),
                yaw * body.rotation);
        }

        /// <summary>Send any goalie to the kept net's crease, facing the play.</summary>
        private static void PlaceGoalie(Player player, TagupConfig cfg) {
            float y = player.PlayerBody.transform.position.y;
            float z = cfg.ScoringSign * cfg.GoalieZ;
            Quaternion rot = Quaternion.LookRotation(new Vector3(0f, 0f, -cfg.ScoringSign), Vector3.up);
            player.PlayerBody.Server_Teleport(new Vector3(0f, y, z), rot);
        }

        // Spread skaters across X by their position name; centre on the puck.
        private static float SpreadX(Player player, TagupConfig cfg) {
            string name = (player.PlayerPosition != null) ? player.PlayerPosition.Name : "";
            float s = cfg.FaceoffXSpacing;
            switch (name) {
                case "LW": return -2f * s;
                case "LD": return -1f * s;
                case "C": return 0f;
                case "RD": return 1f * s;
                case "RW": return 2f * s;
                default: return 0f;
            }
        }
    }
}
