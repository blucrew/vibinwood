using System;
using UnityEngine;

namespace RmwHaptics
{
    /// <summary>
    /// Harmony postfix bodies — plain static methods (no attributes). They are wired
    /// to game methods one-by-one in Plugin.ApplyPatches() so a single missing/renamed
    /// target only drops that one hook instead of aborting the whole patch set.
    ///
    /// Targets (all in Assembly-CSharp, global namespace — see EVENT_MAP.md):
    ///   BattleScreenController, HotSceneController, PlayerSetItemController, BrawlCumshotAttackPanel
    /// </summary>
    public static class Hooks
    {
        // ── Combat: BattleScreenController ──
        public static void RobinAttack()   => HapticsConfig.RobinAttack.Fire();
        public static void EnemyAttack()   => HapticsConfig.EnemyAttack.Fire();

        // protected virtual void DisplayRobinsLife(int damage) — scale buzz by hit size.
        public static void RobinHurt(int damage)
        {
            float scale = damage <= 0 ? 0.6f : Math.Min(1.5f, 0.4f + damage * 0.06f);
            HapticsConfig.RobinHurt.Fire(scale);
        }

        public static void JerkOff()         => HapticsConfig.JerkOff.Fire();
        public static void ForceRobinCums()  => HapticsConfig.RobinCums.Fire();

        // protected virtual void GameOver(EndType endType)
        //   EndType: None=0, RobinCums=1, EnemyFlee=2, RobinsFlee=3, EnemyCums=4, DoubleCumshot=5
        public static void GameOver(BattleScreenController.EndType endType)
        {
            switch ((int)endType)
            {
                case 1: HapticsConfig.RobinCums.Fire();     break;
                case 4: HapticsConfig.EnemyCums.Fire();     break;
                case 5: HapticsConfig.DoubleCumshot.Fire(); break;
                default: break; // None / flee — no climax
            }
        }

        // ── Hot scenes ──
        public static void HotSceneStart()   => HapticsConfig.HotSceneStart.Fire();
        public static void HotSceneAdvance() => HapticsConfig.HotSceneAdvance.Fire();
        public static void HotSceneWin()     => HapticsConfig.HotSceneWin.Fire();
        public static void HotSceneLose()    => HapticsConfig.HotSceneLose.Fire();

        // ── Brawl ──
        public static void BrawlClimax()     => HapticsConfig.BrawlClimax.Fire();

        // ── Continuous engine (postfix on BattleScreenController.Update — runs every
        //    frame while a battle is active; replaces the crash-prone injected MonoBehaviour). ──
        public static void BattleTick()  => RuntimeState.Tick(Time.deltaTime);
        public static void BattleEnd()   => ButtplugManager.StopAllSustained();
    }
}
