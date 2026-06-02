using UnityEngine;

namespace Tagup {
    /// <summary>
    /// Thin wrapper around <see cref="Debug"/> so every line is tagged and easy to grep in
    /// the Puck log files.
    /// </summary>
    internal static class Log {
        private const string PREFIX = "[" + Constants.MOD_NAME + "] ";

        internal static void Info(string message) => Debug.Log(PREFIX + message);

        internal static void Warn(string message) => Debug.LogWarning(PREFIX + message);

        internal static void Error(string message) => Debug.LogError(PREFIX + message);
    }
}
