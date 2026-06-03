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
    }
}
