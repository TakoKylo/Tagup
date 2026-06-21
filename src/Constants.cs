namespace Tagup {
    /// <summary>
    /// Fixed values that are not meant to be tuned by the server operator.
    /// Tunable gameplay values live in <see cref="TagupConfig"/>.
    /// </summary>
    internal static class Constants {
        /// <summary>Plugin / Harmony id and Plugins subfolder name.</summary>
        internal const string MOD_NAME = "Tagup";

        /// <summary>Mod version, surfaced in logs.</summary>
        internal const string MOD_VERSION = "0.2.0";

        /// <summary>Radius of the puck (from the game's Codebase.Constants.PUCK_RADIUS).</summary>
        internal const float PUCK_RADIUS = 0.14f;

        /// <summary>Chat colour for normal "ref" announcements.</summary>
        internal const string CHAT_COLOR = "#ffe97f";

        /// <summary>Chat colour for waved-off / warning announcements.</summary>
        internal const string CHAT_COLOR_WARN = "#ff7a7a";

        /// <summary>Config file name, written next to the plugin DLL.</summary>
        internal const string CONFIG_FILE = "tagup_config.json";

        /// <summary>
        /// Assembly name of the oomtm450 "Ruleset" mod (from its .csproj &lt;AssemblyName&gt;). Tagup
        /// detects this loaded assembly and yields ownership of the period clock / phase transitions
        /// to it so Ruleset's game-phase / time logging stays accurate. See <see cref="Compat"/>.
        /// </summary>
        internal const string RULESET_ASSEMBLY = "oomtm450PuckMod_Ruleset";

        /// <summary>
        /// Ruleset's Harmony instance id (its <c>Codebase.Constants.RULESET_MOD_NAME</c> =
        /// "oomtm450_ruleset"). Used in <c>[HarmonyAfter]</c> so Tagup's ScoreGoal patch runs after
        /// Ruleset's rule checks rather than in an undefined order.
        /// </summary>
        internal const string RULESET_HARMONY_ID = "oomtm450_ruleset";
    }
}
