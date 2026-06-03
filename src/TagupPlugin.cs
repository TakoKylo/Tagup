using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace Tagup {
    /// <summary>
    /// Entry point. Puck loads the first DLL in each Plugins subfolder, finds the
    /// <c>IPuckPlugin</c> implementation, and calls <see cref="OnEnable"/> / <see cref="OnDisable"/>
    /// when the mod is toggled in the in-game Mods menu.
    ///
    /// This is the "half-court / tag-up" mode: one live net, carry the puck over the blue line
    /// in possession to be allowed to score, and clear it back out yourself after a turnover.
    /// </summary>
    public class TagupPlugin : IPuckPlugin {
        private static readonly Harmony _harmony = new Harmony(Constants.MOD_NAME);
        private static bool _patched;

        /// <summary>Shared, server-authoritative config (read by the patches).</summary>
        internal static TagupConfig Cfg { get; private set; } = new TagupConfig();

        public bool OnEnable() {
            try {
                if (_patched) return true;

                Log.Info($"Enabling {Constants.MOD_NAME} v{Constants.MOD_VERSION}...");

                Cfg = TagupConfig.Load(ConfigPath());
                Possession.MaxPossessionMs = Cfg.MaxPossessionMilliseconds;
                Possession.MaxTippedMs = Cfg.MaxTippedMilliseconds;
                Possession.PuckOnIceHeight = Cfg.PuckOnIceHeight;
                Possession.PossessionRadius = Cfg.PossessionRadius;
                Possession.ControlSpeed = Cfg.ControlSpeed;

                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                _patched = true;

                Log.Info($"Enabled. Active net: {Cfg.ActiveNet} (tag line |Z|={Cfg.TagLineZ}).");
                return true;
            }
            catch (Exception ex) {
                Log.Error("Failed to enable: " + ex);
                return false;
            }
        }

        public bool OnDisable() {
            try {
                Log.Info("Disabling...");

                if (_patched) {
                    _harmony.UnpatchSelf();
                    _patched = false;
                }

                TagupHud.Shutdown();
                NetGeometry.Restore();
                TagupState.Reset();
                Possession.Clear();
                TagupGame.Reset();   // drop any queued first-to-N game-over so re-enable starts clean

                Log.Info("Disabled.");
                return true;
            }
            catch (Exception ex) {
                Log.Error("Failed to disable: " + ex);
                return false;
            }
        }

        /// <summary>Config lives next to the plugin DLL (Puck/Plugins/Tagup/).</summary>
        private static string ConfigPath() {
            string dir;
            try {
                dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            catch {
                dir = ".";
            }
            return Path.Combine(dir ?? ".", Constants.CONFIG_FILE);
        }
    }
}
