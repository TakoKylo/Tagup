using UnityEngine;

namespace Tagup {
    /// <summary>
    /// Game-flow rules layered on top of the vanilla state machine: a frozen period clock means
    /// the game can only end by reaching the win score, so this watches for a team hitting it and
    /// forces the game-over phase (after a short delay so the goal horn plays).
    /// </summary>
    internal static class TagupGame {
        private static bool _ending;          // a game-over is queued or in progress
        private static float _gameOverAt;     // Time.unscaledTime to fire game-over, 0 = none

        /// <summary>Reset when a fresh game starts (Warmup/PreGame) so the next game can end too.</summary>
        internal static void OnNewGame() {
            _ending = false;
            _gameOverAt = 0f;
        }

        /// <summary>
        /// Called when a score phase begins. If the just-scored team reached the win score, queue
        /// the game-over transition.
        /// </summary>
        internal static void OnScorePhase(TagupConfig cfg) {
            if (_ending || cfg.WinScore <= 0 || GameManager.Instance == null) return;

            int top = Mathf.Max(GameManager.Instance.BlueScore, GameManager.Instance.RedScore);
            if (top >= cfg.WinScore) {
                _ending = true;
                _gameOverAt = Time.unscaledTime + Mathf.Max(0f, cfg.GameOverDelaySeconds);
            }
        }

        /// <summary>Called every server frame; fires the queued game-over when its delay elapses.</summary>
        internal static void TickServer(TagupConfig cfg) {
            if (!_ending || _gameOverAt <= 0f || Time.unscaledTime < _gameOverAt) return;
            _gameOverAt = 0f;

            if (GameManager.Instance != null && GameManager.Instance.Phase != GamePhase.GameOver) {
                // gameResult was refreshed by the game mode's UpdateGameResult() on the goal, so the
                // game-over screen reads the correct winner. We only need to switch phases.
                GameManager.Instance.Server_SetGameState(GamePhase.GameOver, cfg.GameOverSeconds);
                Log.Info("First to " + cfg.WinScore + " reached — game over.");
            }
        }
    }
}
