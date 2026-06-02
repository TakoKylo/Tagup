# Tagup — Half-Court "Tag-Up" mod for Puck

A server-side game-mode mod for **Puck** (build 897) that turns the rink into a
**half-court / make-it-take-it** sheet:

- **One live net.** The net on the non-scoring side is removed; both teams attack the same net.
- **Tag up to score.** A goal only counts if the scoring team **carried the puck across the blue
  line on a stick** — a puck shot or dumped in tags nobody, and both teams have to take it in. The
  puck **leaving the scoring half un-tags** the team; carry it back in to re-tag.
- **Clear after a turnover.** When the puck changes hands, the new team must **carry it back out
  past the blue line themselves**, then bring it back in, before they can score.
- **First to N (default 3).** The period clock is **frozen**; the game ends when a team reaches the
  win score.
- **Custom HUD.** During play the frozen clock is replaced with the tag-up status + "FIRST TO N";
  the team score boxes show the race. (Overlays the vanilla scoreboard, like the OpenWorld mod.)
- **Possession restart.** After a goal the team that **scored defends** — they spawn just inside
  the blue line with the net behind them. The team that was **scored on attacks from the default
  (centre) faceoff spots** with the puck on the default centre dot, so their centre wins it. The
  opening faceoff is a neutral 90° centre draw.
- **Optional goalie.** Players may take the goalie role; any goalie is placed in the kept net's
  crease.

Illegal goals (scoring without being tagged up) are **waved off** and the puck is turned over to
the other team; play continues.

## How it works (implementation)

| Concern | Where |
| --- | --- |
| Plugin entry point (`IPuckPlugin`) | [src/TagupPlugin.cs](src/TagupPlugin.cs) |
| Harmony patches | [src/Patches.cs](src/Patches.cs) |
| Tag-up / clear state machine | [src/TagupState.cs](src/TagupState.cs) |
| Puck-possession resolver | [src/Possession.cs](src/Possession.cs) |
| Net removal + half detection | [src/NetGeometry.cs](src/NetGeometry.cs) |
| 90° faceoff rotation | [src/Faceoff.cs](src/Faceoff.cs) |
| Tunable config | [src/TagupConfig.cs](src/TagupConfig.cs) |

Because both teams shoot the same net, the engine's built-in goal attribution is always wrong, so
the `ScoreGoal` patch rewrites `byTeam`/`goalPlayer` to credit whichever team is actually tagged up
and in possession (or cancels the goal entirely).

All gameplay logic is server-authoritative; net removal also runs on clients so the open half looks
consistent for everyone running the mod.

## Building

Requires the .NET SDK (a .NET Framework 4.8 targeting pack or the
`Microsoft.NETFramework.ReferenceAssemblies` package) and a local Puck install.

```powershell
dotnet build Tagup.csproj -c Release
```

The build outputs straight into `…\Puck\Plugins\Tagup\Tagup.dll`. If your game lives elsewhere,
override the lib path and output path:

```powershell
dotnet build Tagup.csproj -c Release -p:PuckLibsPath="D:\Steam\steamapps\common\Puck\Puck_Data\Managed" -p:OutputPath="D:\Steam\steamapps\common\Puck\Plugins\Tagup"
```

Then launch Puck, open the **Mods** menu, and enable **Tagup**.

## Configuration

On first enable a `tagup_config.json` is written next to the DLL. Defaults:

| Key | Default | Meaning |
| --- | --- | --- |
| `ActiveNet` | `"Blue"` | Net that stays in play: `"Blue"` (+Z) or `"Red"` (−Z). |
| `TagLineZ` | `13.25` | \|Z\| of the tag-up / clear blue line. |
| `LineMargin` | `0.3` | Hysteresis band around the line. |
| `MinPossessionMilliseconds` | `250` | Continuous stick contact before you "possess". |
| `MaxPossessionMilliseconds` | `500` | Time after last contact before possession is lost. |
| `CarryToleranceMilliseconds` | `150` | Max time-in-flight for a line crossing to still count as a carry (vs a shot). |
| `FaceoffRotationDegrees` | `90` | Yaw of the neutral opening faceoff formation. |
| `FaceoffDefenseFrontset` | `2` | Units the defending (scoring) team spawns past the blue line. |
| `FaceoffXSpacing` | `3.5` | Horizontal spacing between defenders in the faceoff row. |
| `GoalieZ` | `38` | \|Z\| of the goalie spot, just in front of the kept net. |
| `FaceoffZ` | `0` | Neutral puck Z used when a goal is waved off (back court). |
| `Announce` | `true` | Broadcast tag-up / waved-off chat messages. |
| `DisableGoalie` | `false` | Block claiming the goalie position. Off so the kept net can have a goalie. |
| `FreezeTimer` | `true` | Freeze the period clock; game ends on score, not time. |
| `WinScore` | `3` | First team to this many goals wins (0 = no first-to-N ending). |
| `GameOverSeconds` | `15` | Length of the game-over screen before the next game. |
| `GameOverDelaySeconds` | `2` | Pause after the winning goal before game-over (lets the horn play). |
| `Hud` | `true` | Show the custom HUD overlay (client-side). |

## Status / tuning

This is a first playable cut. The make-it-take-it thresholds, the post-goal spawn distances, and the
waved-off puck-reset behaviour are heuristics that will want a round of in-game tuning — see the
config table above.
