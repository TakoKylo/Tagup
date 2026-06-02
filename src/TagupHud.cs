using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tagup {
    /// <summary>Builds the two HUD strings (phase + time) from the current tag-up state.</summary>
    internal static class TagupStatus {
        internal static (string phase, string time) Build(TagupConfig cfg) {
            string phase;
            if (TagupState.BlueEligible && !TagupState.RedEligible) phase = "BLUE — TAGGED UP";
            else if (TagupState.RedEligible && !TagupState.BlueEligible) phase = "RED — TAGGED UP";
            else if (TagupState.BlueEligible || TagupState.RedEligible) phase = "TAGGED UP";
            else phase = "TAG UP TO SCORE";

            string time = cfg.WinScore > 0 ? "FIRST TO " + cfg.WinScore : "HALF COURT";
            return (phase, time);
        }
    }

    /// <summary>
    /// Client-side HUD overlay, modelled on the OpenWorld mod's ActivityHud: it reuses the vanilla
    /// scoreboard's PhaseLabel + TimeLabel (resolved on <see cref="UIGameState"/> via reflection)
    /// and, during the Play phase, overwrites them with the tag-up status and the "FIRST TO N"
    /// target — so the frozen period clock is replaced with something meaningful. The team score
    /// boxes are left untouched (they show the race to N). Created on clients and the listen host,
    /// never on a headless dedicated server.
    /// </summary>
    public class TagupHud : MonoBehaviour {
        public static TagupHud Instance { get; private set; }

        private static FieldInfo _fiPhase;
        private static FieldInfo _fiTime;

        private UIGameState _ui;
        private Label _phaseLabel;
        private Label _timeLabel;

        public static void EnsureCreated() {
            if (Instance != null) return;
            if (Srv.IsDedicated) return; // nothing to render on a headless server
            GameObject go = new GameObject("Tagup_Hud");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TagupHud>();
        }

        public static void Shutdown() {
            TagupNet.ClientUnregister();
            if (Instance == null) return;
            Destroy(Instance.gameObject);
            Instance = null;
        }

        private void LateUpdate() {
            try {
                // Clients learn the status over the network; register lazily once Netcode is up.
                // Registered unconditionally so a listen host also handles its own loopback broadcast.
                TagupNet.ClientRegister();

                // Re-resolve if the UI was destroyed by a scene reload (the cached managed Label
                // references survive a destroy, so check the owning UIGameState's Unity-null too).
                if (_ui == null || _phaseLabel == null || _timeLabel == null) {
                    if (!TryResolveLabels()) return;
                }

                // Only take over during live play; let vanilla own the faceoff / score countdowns.
                if (GameManager.Instance == null || GameManager.Instance.Phase != GamePhase.Play) return;

                string phase, time;
                if (Srv.IsServer) {
                    (phase, time) = TagupStatus.Build(TagupPlugin.Cfg); // host reads state directly
                }
                else {
                    phase = TagupNet.ClientPhase;
                    time = TagupNet.ClientTime;
                }

                _phaseLabel.style.display = DisplayStyle.Flex;
                _timeLabel.style.display = DisplayStyle.Flex;
                _phaseLabel.text = phase;
                _timeLabel.text = time;
            }
            catch {
                // Never let HUD rendering tear down the frame.
            }
        }

        private bool TryResolveLabels() {
            if (_ui == null) {
                _ui = Object.FindFirstObjectByType<UIGameState>(FindObjectsInactive.Include);
                if (_ui == null) return false;
            }

            if (_fiPhase == null)
                _fiPhase = typeof(UIGameState).GetField("phaseLabel", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_fiTime == null)
                _fiTime = typeof(UIGameState).GetField("timeLabel", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_fiPhase == null || _fiTime == null) return false;

            _phaseLabel = _fiPhase.GetValue(_ui) as Label;
            _timeLabel = _fiTime.GetValue(_ui) as Label;
            return _phaseLabel != null && _timeLabel != null;
        }
    }
}
