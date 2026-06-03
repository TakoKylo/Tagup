using System;
using System.IO;
using Newtonsoft.Json;

namespace Tagup {
    /// <summary>
    /// Tunable gameplay values, persisted as JSON next to the plugin DLL so a server operator
    /// can adjust thresholds without recompiling. Loaded once on enable.
    /// </summary>
    internal class TagupConfig {
        /// <summary>Which net stays in play: "Blue" (+Z net) or "Red" (-Z net).</summary>
        public string ActiveNet = "Blue";

        /// <summary>|Z| of the tag-up / clear blue line (game blue lines sit at ~±13.25).</summary>
        public float TagLineZ = 13.25f;

        /// <summary>Hysteresis band (units) around the line when deciding which half the puck is in.</summary>
        public float LineMargin = 0.3f;

        /// <summary>Time since last stick contact (ms) after which possession is considered lost.</summary>
        public int MaxPossessionMilliseconds = 500;

        /// <summary>
        /// A stick contact shorter than this (ms), with the puck on the ice, is treated as a TIP /
        /// deflection and ignored for possession — only longer contact counts as control. This is
        /// what keeps a defender's glance off the puck from registering as a possession / turnover.
        /// </summary>
        public int MaxTippedMilliseconds = 80;

        /// <summary>Puck centre above this height is airborne, so any contact with it is a tip (not control).</summary>
        public float PuckOnIceHeight = 0.25f;

        /// <summary>
        /// The possessor keeps the puck only while it stays within this horizontal distance (units) of
        /// them. Once the puck gets farther — a shot, dump, or long pass — possession is loose
        /// (contested) until a player corrals it again. ~stick reach, so stickhandling still counts.
        /// </summary>
        public float PossessionRadius = 2.5f;

        /// <summary>
        /// Puck moving faster than this (units/s) is a shot / loose puck in flight, not under control,
        /// so it belongs to nobody. Keep above skating speed so a fast carry still counts; below a
        /// typical dump-in. (Puck max speed is 30.)
        /// </summary>
        public float ControlSpeed = 14f;

        /// <summary>
        /// A slow puck within this horizontal distance (units) of a lone skater's blade counts as
        /// under their control even when their stick is not touching it this instant — so a slow
        /// stickhandle or fake move across the blue line still registers as a controlled entry. If an
        /// opponent is also within reach it is a contested battle (nobody's). ~one stick blade's reach.
        /// </summary>
        public float ControlRadius = 1.5f;

        /// <summary>
        /// Seconds a new team must keep control before it counts as a turnover. Stops a tip / deflection
        /// / ghost touch on the way to the net from briefly registering as the other team and stealing
        /// the goal from the shooter. Keep small (a real steal lasts far longer than a deflection).
        /// </summary>
        public float TurnoverControlSeconds = 0.15f;

        /// <summary>
        /// Seconds a carry survives a brief stick-contact gap before the puck is considered loose, so
        /// the controlled-entry check is not fooled by a one-frame gap mid-dangle. A puck in flight
        /// (a shot/dump) ends the carry immediately regardless of this.
        /// </summary>
        public float EntryGraceSeconds = 0.4f;

        /// <summary>Yaw applied to the neutral opening faceoff formation (90 = quarter turn).</summary>
        public float FaceoffRotationDegrees = 90f;

        /// <summary>Units the defending (scoring) team spawns PAST the blue line, toward the net.</summary>
        public float FaceoffDefenseFrontset = 2f;

        /// <summary>Horizontal spacing between players in the faceoff rows.</summary>
        public float FaceoffXSpacing = 3.5f;

        /// <summary>|Z| of the goalie's spot, just in front of the kept net (~40).</summary>
        public float GoalieZ = 38f;

        /// <summary>Z of the centre dot the neutral opening faceoff formation is rotated around.</summary>
        public float FaceoffZ = 0f;

        /// <summary>Broadcast chat messages for tag-up / turnover / waved-off events.</summary>
        public bool Announce = true;

        /// <summary>Block players from claiming the goalie position. Off by default so the kept net can have a goalie.</summary>
        public bool DisableGoalie = false;

        /// <summary>Freeze the period clock so the game only ends on score (first to N).</summary>
        public bool FreezeTimer = true;

        /// <summary>First team to this many goals wins. 0 disables the first-to-N ending.</summary>
        public int WinScore = 3;

        /// <summary>Seconds the game-over screen is shown before the next game starts.</summary>
        public int GameOverSeconds = 15;

        /// <summary>Delay after the winning goal before the game-over screen (lets the horn play).</summary>
        public float GameOverDelaySeconds = 2f;

        /// <summary>Show the custom Tagup HUD overlay (client-side).</summary>
        public bool Hud = true;

        /// <summary>True when the kept net is the +Z ("Blue") net.</summary>
        [JsonIgnore]
        public bool ActiveNetIsBlue => !string.Equals(ActiveNet, "Red", StringComparison.OrdinalIgnoreCase);

        /// <summary>+1 if the scoring half is +Z, -1 if it is -Z. Lets the same maths serve both nets.</summary>
        [JsonIgnore]
        public float ScoringSign => ActiveNetIsBlue ? 1f : -1f;

        internal static TagupConfig Load(string path) {
            try {
                if (File.Exists(path)) {
                    TagupConfig cfg = JsonConvert.DeserializeObject<TagupConfig>(File.ReadAllText(path));
                    if (cfg != null) {
                        Log.Info("Loaded config from " + path);
                        return cfg;
                    }
                }
            }
            catch (Exception ex) {
                Log.Error("Failed to read config, using defaults: " + ex.Message);
            }

            TagupConfig fresh = new TagupConfig();
            fresh.Save(path);
            return fresh;
        }

        internal void Save(string path) {
            try {
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) {
                Log.Error("Failed to write config: " + ex.Message);
            }
        }
    }
}
