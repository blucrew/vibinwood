# RMW (The Whellcum's Secret) — Haptic Event Map

Game: Robin Morningwood Adventure — The Whellcum's Secret (TWS)
Engine: Unity 2022.3.20f1, IL2CPP. Assembly-CSharp. Most game classes are in the **global namespace** (no namespace).
Source of truth: Il2CppDumper `dump.cs` (RMW_Haptics/dump/dump.cs).

> Content is illustration/turn-based — NO continuous arousal meter. Haptics model = **discrete events** (start / advance / climax / hit).

## Combat — `BattleScreenController : MonoBehaviour` (TypeDefIndex 4116)
| Method | Meaning | Haptic |
|---|---|---|
| `void Attack(AttackData attackData)` | Robin lands an attack | short pulse |
| `void ExecuteEnemyAttack()` | Enemy attacks Robin | short pulse |
| `void DisplayRobinsLife(int damage)` | Robin takes `damage` | intensity scaled by `damage` |
| `void JerkOff()` | Robin's jerk-off battle move | medium buzz |
| `void EnemyStartsCumming()` | Enemy climax begins | rising buzz |
| `void GameOver(EndType endType)` | Battle end | by EndType (below) |
| `void ForceGameOverRobinCums()` | Forced Robin climax | strong climax |

`enum BattleScreenController.EndType`: None=0, RobinCums=1, EnemyFlee=2, RobinsFlee=3, EnemyCums=4, DoubleCumshot=5.
→ Climax intensity: DoubleCumshot > RobinCums/EnemyCums > flee(none).

## Hot scenes (gallery / illustrations)
| Method | Meaning | Haptic |
|---|---|---|
| `HotSceneController.StartDisplay()` | A hot scene opens | onset buzz |
| `HotSceneController.DisplayNextScene()` | Advance to next illustration frame | pulse per frame |
| `PlayerSetItemController.WinHotScene(HotsceneData data)` | Won a hot scene | climax |
| `PlayerSetItemController.LoseHotScene(HotsceneData data)` | Lost a hot scene | softer |

## Brawl minigame (adult card battler)
| Method | Meaning | Haptic |
|---|---|---|
| `BrawlCumshotAttackPanel.Play(AttackData data)` | Brawl climax attack plays | climax |

## Notes
- All hooks are `public`/`protected virtual` instance methods → HarmonyX patchable by `typeof(X), "Method"`.
- `DisplayRobinsLife(int damage)` exposes the damage value → use for intensity scaling.
- AudioClip `playerCumshotSound` exists on BattleScreenController + BrawlCumshotAttackPanel (confirms climax SFX moments).
