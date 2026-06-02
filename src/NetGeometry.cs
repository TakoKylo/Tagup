using System.Collections.Generic;
using UnityEngine;

namespace Tagup {
    /// <summary>
    /// Owns the "half court" geometry: disabling the net on the non-scoring side and answering
    /// which half of the rink a given Z is in. The net is disabled on every instance that runs
    /// the mod (client and server) so visuals and colliders stay consistent for the open half.
    /// </summary>
    internal static class NetGeometry {
        private static readonly List<GameObject> _disabled = new List<GameObject>();
        private static bool _applied;

        /// <summary>Which half of the rink contains <paramref name="puckZ"/>.</summary>
        internal static HalfSide GetSide(float puckZ, TagupConfig cfg) {
            float signed = cfg.ScoringSign * puckZ;
            if (signed > cfg.TagLineZ + cfg.LineMargin) return HalfSide.ScoringHalf;
            if (signed < cfg.TagLineZ - cfg.LineMargin) return HalfSide.BackCourt;
            return HalfSide.Unknown;
        }

        /// <summary>True if <paramref name="z"/> is on the removed (non-scoring) side.</summary>
        internal static bool IsRemovedSide(float z, TagupConfig cfg) => z * -cfg.ScoringSign > 0f;

        /// <summary>
        /// Disable the net on the non-scoring side. Idempotent: no-op once applied, and
        /// automatically re-applies if the scene reloaded (our objects were destroyed).
        /// </summary>
        internal static void EnsureApplied(TagupConfig cfg) {
            if (_applied) {
                bool stillValid = false;
                foreach (GameObject go in _disabled) {
                    if (go != null) { stillValid = true; break; }
                }
                if (stillValid) return;
                _applied = false;
                _disabled.Clear();
            }

            Goal[] goals = Object.FindObjectsByType<Goal>(FindObjectsSortMode.None);
            if (goals == null || goals.Length == 0) return; // scene not ready yet

            foreach (Goal goal in goals) {
                if (!goal) continue;
                float z = goal.transform.position.z;
                if (IsRemovedSide(z, cfg)) {
                    goal.gameObject.SetActive(false);
                    _disabled.Add(goal.gameObject);
                    Log.Info($"Disabled net at z={z:0.0} (non-scoring side).");
                }
            }
            _applied = true;
        }

        /// <summary>Re-enable any nets we disabled (called when the mod is turned off).</summary>
        internal static void Restore() {
            foreach (GameObject go in _disabled) {
                if (go != null) go.SetActive(true);
            }
            _disabled.Clear();
            _applied = false;
        }
    }
}
