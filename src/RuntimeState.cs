using System;

namespace RmwHaptics
{
    /// <summary>
    /// Continuous-engine state + logic. Driven from a Harmony postfix on
    /// BattleScreenController.Update (see Hooks.BattleTick) — NOT an injected MonoBehaviour,
    /// because Il2CppInterop's ClassInjector AccessViolates on this game's runtime.
    ///
    /// Reads the live battle meters each tick (throttled ~15 Hz) and drives every toy per
    /// its routing, plus XToys. Exposed RobinArousal/EnemyArousal/InBattle are for any
    /// future status readout (e.g. an external config tool).
    /// </summary>
    public static class RuntimeState
    {
        public static bool  InBattle;
        public static float RobinArousal, EnemyArousal;

        private static float _accum;
        private static bool  _wasDriving;
        private static int   _lastXToys = -1;

        public static void Tick(float dt)
        {
            _accum += dt;
            if (_accum < 0.066f) return;   // ~15 Hz
            _accum = 0f;
            TickContinuous();
        }

        private static void TickContinuous()
        {
            var mode = HapticsConfig.Mode.Value;
            bool continuous = HapticsConfig.MasterEnabled.Value &&
                              (mode == HapticMode.Continuous || mode == HapticMode.Both);

            var ctrl = BattleScreenController.instance;
            InBattle = false;
            if (ctrl != null)
            {
                try { InBattle = ctrl.battlePanel != null && ctrl.battlePanel.activeInHierarchy; }
                catch { InBattle = true; }
            }

            if (!continuous || !InBattle)
            {
                if (_wasDriving) { ButtplugManager.StopAllSustained(); _wasDriving = false; RobinArousal = EnemyArousal = 0f; }
                return;
            }
            _wasDriving = true;

            float robinLife = 1f, enemyLife = 1f;
            try { robinLife = ctrl!.robinsLifeAsPerc; } catch { }
            try { enemyLife = ctrl!.enemysLifeSliderValue; } catch { }

            RobinArousal = Arousal(robinLife);
            EnemyArousal = Arousal(enemyLife);

            int n = ButtplugManager.DeviceCount;
            for (int slot = 0; slot < n; slot++)
            {
                var src = HapticsConfig.RouteFor(ButtplugManager.GetDeviceName(slot), slot);
                ButtplugManager.SetSustained(slot, SourceValue(src, RobinArousal, EnemyArousal));
            }
            XToysContinuous(SourceValue(HapticsConfig.XToysSource.Value, RobinArousal, EnemyArousal));
        }

        private static float Arousal(float lifePerc)
        {
            if (lifePerc < 0f) lifePerc = 0f; if (lifePerc > 1f) lifePerc = 1f;
            float v = HapticsConfig.ContinuousInvert.Value ? (1f - lifePerc) : lifePerc;
            v *= HapticsConfig.ContinuousMultiplier.Value * HapticsConfig.MasterMultiplier.Value;
            if (v <= 0.001f) return 0f;
            float lo = HapticsConfig.ContinuousMin.Value, hi = HapticsConfig.ContinuousMax.Value;
            v = lo + v * (hi - lo);
            return Math.Max(0f, Math.Min(1f, v));
        }

        private static float SourceValue(ArousalSource s, float robin, float enemy) => s switch
        {
            ArousalSource.Robin => robin,
            ArousalSource.Enemy => enemy,
            ArousalSource.Both  => Math.Max(robin, enemy),
            _                   => 0f,
        };

        private static void XToysContinuous(float a)
        {
            int pct = (int)Math.Max(0, Math.Min(100, a * 100f));
            if (pct == _lastXToys) return;
            _lastXToys = pct;
            if (XToysManager.IsEnabled) _ = XToysManager.FireRawAsync(pct, 1200);
        }
    }
}
