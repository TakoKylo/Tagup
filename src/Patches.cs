using System;
using HarmonyLib;
using UnityEngine;

namespace Tagup {
    /// <summary>
    /// All Harmony patches for the mod. Possession tracking and net removal aside, every patch
    /// is server-authoritative and gates on <see cref="Srv.IsServer"/>.
    /// </summary>
    internal static class Patches {
        private static TagupConfig Cfg => TagupPlugin.Cfg;

        /// <summary>Broadcast a "ref" message to all players (server only).</summary>
        private static void Announce(string message, bool warn) {
            if (Cfg == null || !Cfg.Announce) return;
            try {
                ChatManager chat = ChatManager.Instance;
                if (chat != null)
                    chat.Server_BroadcastChatMessage(message, warn ? Constants.CHAT_COLOR_WARN : Constants.CHAT_COLOR);
            }
            catch { /* chat is best-effort */ }
        }

        private static Stick GetStick(Collision collision) =>
            collision != null && collision.gameObject ? collision.gameObject.GetComponent<Stick>() : null;

        // ----------------------------------------------------------------- Possession tracking
        // Resolve the non-goalie stick player on a puck collision (goalies never "possess" for tag-up
        // purposes). Returns null for anything else. Guarded by the callers so an exception can never
        // escape into the physics callback.
        private static Player SkaterOnPuck(Collision collision) {
            Stick stick = GetStick(collision);
            return (stick && stick.Player && stick.Player.Role != PlayerRole.Goalie) ? stick.Player : null;
        }

        [HarmonyPatch(typeof(Puck), "OnCollisionEnter")]
        internal static class Puck_OnCollisionEnter {
            private static void Postfix(Collision collision) {
                if (!Srv.IsServer) return;
                try {
                    Player skater = SkaterOnPuck(collision);
                    if (skater) Possession.OnContactBegin(skater);
                }
                catch (Exception ex) { Log.Error("Puck enter patch: " + ex); }
            }
        }

        [HarmonyPatch(typeof(Puck), "OnCollisionStay")]
        internal static class Puck_OnCollisionStay {
            private static void Postfix(Puck __instance, Collision collision) {
                if (!Srv.IsServer || !__instance) return;
                try {
                    Player skater = SkaterOnPuck(collision);
                    if (skater) Possession.OnContact(skater, __instance.Rigidbody.position.y);
                }
                catch (Exception ex) { Log.Error("Puck stay patch: " + ex); }
            }
        }

        [HarmonyPatch(typeof(Puck), "OnCollisionExit")]
        internal static class Puck_OnCollisionExit {
            private static void Postfix(Puck __instance, Collision collision) {
                if (!Srv.IsServer || !__instance) return;
                try {
                    Player skater = SkaterOnPuck(collision);
                    if (skater) Possession.OnContact(skater, __instance.Rigidbody.position.y);
                }
                catch (Exception ex) { Log.Error("Puck exit patch: " + ex); }
            }
        }

        // ----------------------------- Per-frame: net removal (everyone) + tag-up logic (server)
        [HarmonyPatch(typeof(PhysicsManager), "Update")]
        internal static class PhysicsManager_Update {
            private static void Postfix() {
                try {
                    // Runs on client and server so the open half + HUD are consistent for everyone.
                    NetGeometry.EnsureApplied(Cfg);
                    if (Cfg.Hud) TagupHud.EnsureCreated();

                    if (!Srv.IsServer) return;

                    // First-to-N: fire any queued game-over once its delay elapses.
                    TagupGame.TickServer(Cfg);

                    // Push HUD status to remote clients (throttled to changes + a 2 s heartbeat).
                    if (Cfg.Hud) TagupNet.ServerMaybeBroadcast(Cfg);

                    if (GameManager.Instance == null || GameManager.Instance.Phase != GamePhase.Play) return;
                    if (PuckManager.Instance == null) return;

                    Puck puck = PuckManager.Instance.GetPuck();
                    if (!puck) return;

                    Vector3 puckPos = puck.Rigidbody.position;
                    HalfSide side = NetGeometry.GetSide(puckPos.z, Cfg);
                    // Who actually controls the puck right now (near it + slow enough), or None if it is
                    // loose / in flight — a dump or shot is contested until someone corrals it.
                    PlayerTeam possessor = Possession.Resolve(puckPos, puck.Rigidbody.linearVelocity.magnitude, out string possessorId);
                    TagupState.OnFrame(possessor, possessorId, side, Announce);
                }
                catch (Exception ex) {
                    Log.Error("PhysicsManager.Update patch: " + ex);
                }
            }
        }

        // ------------------------------------ Scoring: enforce tag-up + fix attribution (one net)
        [HarmonyPatch(typeof(BaseGameMode<BaseGameModeConfig>), "ScoreGoal")]
        internal static class BaseGameMode_ScoreGoal {
            // byTeam / goalPlayer are taken by ref so we can correct the engine's attribution:
            // both teams shoot the same net, so the engine would always credit the wrong side.
            private static bool Prefix(ref PlayerTeam byTeam, ref Player goalPlayer,
                ref Player assistPlayer, ref Player secondAssistPlayer, Puck puck) {
                if (!Srv.IsServer) return true;
                try {
                    PlayerTeam scorer = TagupState.LastPossessionTeam;

                    // A goal only counts if the team that carried the puck over the line is tagged up.
                    // A shot/dump in (or a goal with no clear possessor) tags nobody — wave it off and
                    // reset to neutral so BOTH teams have to take it in.
                    if (scorer == PlayerTeam.None || !TagupState.IsEligible(scorer)) {
                        Announce("NO GOAL: the puck wasn't carried over the line. Take it in to score.", true);
                        TagupState.Reset();
                        Possession.Clear();   // neutral: nobody owns the puck until a fresh touch
                        ResetPuckToCenter(puck);
                        return false; // cancel the goal; play continues
                    }

                    // Legal goal: credit the correct team and scorer. OnGoalScored keys the score
                    // increment + score phase off byTeam, so this is all that is needed.
                    byTeam = scorer;
                    Faceoff.LastScoringTeam = scorer; // drives the next restart (conceding team attacks)
                    Player realScorer = ResolveScorer(scorer);
                    if (realScorer) goalPlayer = realScorer;
                    assistPlayer = null;        // engine assists come from the wrong-side collisions
                    secondAssistPlayer = null;
                    return true;
                }
                catch (Exception ex) {
                    Log.Error("ScoreGoal patch: " + ex);
                    return true;
                }
            }

            private static Player ResolveScorer(PlayerTeam team) {
                string id = TagupState.LastPossessorSteamId;
                if (string.IsNullOrEmpty(id) || PlayerManager.Instance == null) return null;
                Player p = PlayerManager.Instance.GetPlayerBySteamId(id);
                return (p && p.Team == team) ? p : null;
            }

            private static void ResetPuckToCenter(Puck puck) {
                if (!puck) return;
                puck.Rigidbody.position = new Vector3(0f, puck.Rigidbody.position.y, Cfg.FaceoffZ);
                puck.Rigidbody.linearVelocity = Vector3.zero;
                puck.Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        // -------------------------------- Belt & suspenders: ignore the removed net's trigger
        [HarmonyPatch(typeof(Goal), nameof(Goal.Server_OnPuckEnterGoal))]
        internal static class Goal_Server_OnPuckEnterGoal {
            private static bool Prefix(Goal __instance) {
                if (!Srv.IsServer) return true;
                return !NetGeometry.IsRemovedSide(__instance.transform.position.z, Cfg);
            }
        }

        // ---------------------------------- Phase transitions: reset state, rotate faceoff puck
        [HarmonyPatch(typeof(BaseGameMode<BaseGameModeConfig>), "OnGameStateChanged")]
        internal static class BaseGameMode_OnGameStateChanged {
            private static void Postfix(GameState oldGameState, GameState newGameState) {
                try {
                    if (oldGameState.Phase == newGameState.Phase) return;
                    if (!Srv.IsServer) return;

                    switch (newGameState.Phase) {
                        case GamePhase.BlueScore:
                        case GamePhase.RedScore:
                            TagupGame.OnScorePhase(Cfg); // queue game-over if a team hit the win score
                            TagupState.Reset();
                            Possession.Clear();
                            break;
                        case GamePhase.Warmup:
                        case GamePhase.PreGame:
                            TagupGame.OnNewGame();        // a fresh game can end again
                            Faceoff.Reset();              // no "scored on" team at game start
                            TagupState.Reset();
                            Possession.Clear();
                            break;
                        case GamePhase.FaceOff:
                        case GamePhase.Intermission:
                        case GamePhase.GameOver:
                            TagupState.Reset();
                            Possession.Clear();
                            break;
                    }
                }
                catch (Exception ex) {
                    Log.Error("OnGameStateChanged patch: " + ex);
                }
            }
        }

        // -------------------------- Place each skater for the post-goal restart as they spawn
        [HarmonyPatch(typeof(Player), nameof(Player.Server_SpawnCharacter))]
        internal static class Player_Server_SpawnCharacter {
            private static void Postfix(Player __instance) {
                try {
                    if (!Srv.IsServer) return;
                    if (GameManager.Instance == null || GameManager.Instance.Phase != GamePhase.FaceOff) return;

                    Faceoff.PlaceSkater(__instance, Cfg);
                }
                catch (Exception ex) {
                    Log.Error("Server_SpawnCharacter patch: " + ex);
                }
            }
        }

        // ----------------------------------------------- Open net: block claiming the goalie
        [HarmonyPatch(typeof(PlayerPosition), nameof(PlayerPosition.Server_Claim))]
        internal static class PlayerPosition_Server_Claim {
            private static bool Prefix(PlayerPosition __instance) {
                if (!Srv.IsServer) return true;
                if (Cfg.DisableGoalie && __instance.Role == PlayerRole.Goalie) return false;
                return true;
            }
        }

        // ------------------------------ Freeze the period clock (game ends on score, not on time)
        [HarmonyPatch(typeof(GameManager), "Server_Tick")]
        internal static class GameManager_Server_Tick {
            private static bool Prefix() {
                if (!Srv.IsServer || !Cfg.FreezeTimer) return true;
                // Freeze only the Play-phase period clock; faceoff / score / intermission countdowns
                // still run so the game keeps flowing between whistles.
                return GameManager.Instance == null || GameManager.Instance.Phase != GamePhase.Play;
            }
        }
    }
}
