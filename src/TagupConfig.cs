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

        /// <summary>Continuous stick contact (ms) before a single toucher counts as possessing.</summary>
        public int MinPossessionMilliseconds = 250;

        /// <summary>Time since last stick contact (ms) after which possession is considered lost.</summary>
        public int MaxPossessionMilliseconds = 500;

        /// <summary>
        /// Max ms since the last stick touch for the puck to still count as "carried" when it
        /// crosses the line. A puck in flight longer than this is a shot/dump and tags nobody.
        /// </summary>
        public int CarryToleranceMilliseconds = 150;

        /// <summary>Yaw applied to the neutral opening faceoff formation (90 = quarter turn).</summary>
        public float FaceoffRotationDegrees = 90f;

        /// <summary>Units the defending (scoring) team spawns PAST the blue line, toward the net.</summary>
        public float FaceoffDefenseFrontset = 2f;

        /// <summary>Horizontal spacing between players in the faceoff rows.</summary>
        public float FaceoffXSpacing = 3.5f;

        /// <summary>|Z| of the goalie's spot, just in front of the kept net (~40).</summary>
        public float GoalieZ = 38f;

        /// <summary>Neutral puck Z used when a goal is waved off (back-court).</summary>
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
