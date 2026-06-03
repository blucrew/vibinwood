using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Configuration;

namespace RmwHaptics
{
    public enum HapticPattern      { Vibrate, Pulse }
    public enum HapticActuatorType { All, Vibrate, Linear, Rotate }

    /// <summary>
    /// One configurable haptic event. Owns a CancellationTokenSource so rapid
    /// re-fires cancel the previous still-running motor task. Fires both outputs
    /// (Intiface + XToys) in parallel.
    /// </summary>
    public class HapticEvent
    {
        public string Key { get; }
        public ConfigEntry<bool>          Enabled;
        public ConfigEntry<float>         Intensity;
        public ConfigEntry<int>           DurationMs;
        public ConfigEntry<HapticPattern> Pattern;

        private CancellationTokenSource? _cts;
        private long _lastFireTick;   // Environment.TickCount64 of last fire (debounce)

        public HapticEvent(ConfigFile cfg, string key, string description,
                           bool enabled, float intensity, int durationMs, HapticPattern pattern)
        {
            Key        = key;
            Enabled    = cfg.Bind(key, "Enabled",    enabled,    description);
            Intensity  = cfg.Bind(key, "Intensity",  intensity,  "Peak intensity 0.0–1.0.");
            DurationMs = cfg.Bind(key, "DurationMs", durationMs, "Event duration in milliseconds.");
            Pattern    = cfg.Bind(key, "Pattern",    pattern,    "Vibrate (steady) or Pulse (ramp up/down).");
        }

        /// <summary>Fire this event. scale multiplies the configured intensity (e.g. damage scaling).</summary>
        public void Fire(float scale = 1f)
        {
            if (!HapticsConfig.MasterEnabled.Value || !Enabled.Value) return;

            // Debounce: collapse rapid/duplicate fires (e.g. a method called from Update,
            // or attacks landing in quick succession) so the toy doesn't machine-gun.
            long now    = Environment.TickCount64;
            int  minGap = Math.Max(150, DurationMs.Value);
            if (now - _lastFireTick < minGap) return;
            _lastFireTick = now;

            float i = Intensity.Value * scale * HapticsConfig.MasterMultiplier.Value;
            if (i < 0f) i = 0f; if (i > 1f) i = 1f;
            int dur = DurationMs.Value;

            HapticsLogger.Info(LogCat.Event, $"{Key} → intensity={i:F2} dur={dur}ms pattern={Pattern.Value}");

            ButtplugManager.Fire(i, dur, Pattern.Value, -1, HapticActuatorType.All, -1, ref _cts);
            XToysManager.Fire(i, dur);
        }
    }

    /// <summary>
    /// Central config: master toggles, XToys settings, and the table of RMW haptic events.
    /// Event keys mirror the in-game moments mapped in EVENT_MAP.md.
    /// </summary>
    public static class HapticsConfig
    {
        // ── Master ──
        public static ConfigEntry<bool>  MasterEnabled    = null!;
        public static ConfigEntry<float> MasterMultiplier = null!;

        // ── XToys ──
        public static ConfigEntry<bool>   XToysEnabled       = null!;
        public static ConfigEntry<string> XToysWebhookId     = null!;
        public static ConfigEntry<float>  XToysMultiplier    = null!;
        public static ConfigEntry<int>    XToysMinDurationMs = null!;

        // ── Event table ──
        public static readonly Dictionary<string, HapticEvent> Events = new Dictionary<string, HapticEvent>();

        // Combat
        public static HapticEvent RobinAttack    = null!;
        public static HapticEvent EnemyAttack    = null!;
        public static HapticEvent RobinHurt      = null!;
        public static HapticEvent JerkOff        = null!;
        public static HapticEvent RobinCums      = null!;
        public static HapticEvent EnemyCums      = null!;
        public static HapticEvent DoubleCumshot  = null!;
        // Hot scenes
        public static HapticEvent HotSceneStart  = null!;
        public static HapticEvent HotSceneAdvance= null!;
        public static HapticEvent HotSceneWin    = null!;
        public static HapticEvent HotSceneLose   = null!;
        // Brawl
        public static HapticEvent BrawlClimax    = null!;

        private static HapticEvent Add(ConfigFile cfg, string key, string desc,
                                       bool en, float intensity, int dur, HapticPattern pat)
        {
            var ev = new HapticEvent(cfg, key, desc, en, intensity, dur, pat);
            Events[key] = ev;
            return ev;
        }

        public static void Init(ConfigFile cfg)
        {
            MasterEnabled    = cfg.Bind("General", "Enabled", true,
                "Master switch for all haptic output.");
            MasterMultiplier = cfg.Bind("General", "MasterMultiplier", 1.0f,
                "Global intensity multiplier applied to every event (0.0–1.0+).");

            XToysEnabled       = cfg.Bind("XToys", "Enabled", false,
                "Enable the XToys cloud webhook output (fires alongside Intiface).");
            XToysWebhookId     = cfg.Bind("XToys", "WebhookId", "",
                "Your XToys Private Webhook ID. Load the '7 Days to Vibe' script (xtoys.app/scripts/7dtvibe), " +
                "run it, connect your toy, then paste the webhook ID here. Treat it like a password.");
            XToysMultiplier    = cfg.Bind("XToys", "Multiplier", 1.0f,
                "Intensity multiplier for XToys output only.");
            XToysMinDurationMs = cfg.Bind("XToys", "MinDurationMs", 400,
                "Pad short events to at least this duration so cloud latency doesn't swallow them.");

            // ── Combat (BattleScreenController) ──
            RobinAttack   = Add(cfg, "Combat.RobinAttack",  "Robin lands an attack in battle.",        true, 0.45f, 250, HapticPattern.Pulse);
            EnemyAttack   = Add(cfg, "Combat.EnemyAttack",  "Enemy attacks Robin.",                    true, 0.35f, 250, HapticPattern.Pulse);
            RobinHurt     = Add(cfg, "Combat.RobinHurt",    "Robin takes damage (scales with hit).",   true, 0.50f, 350, HapticPattern.Vibrate);
            JerkOff       = Add(cfg, "Combat.JerkOff",      "Robin uses the jerk-off battle move.",    true, 0.60f, 900, HapticPattern.Pulse);
            RobinCums     = Add(cfg, "Combat.RobinCums",    "Battle ends with Robin's climax.",        true, 0.90f, 2500,HapticPattern.Vibrate);
            EnemyCums     = Add(cfg, "Combat.EnemyCums",    "Battle ends with the enemy's climax.",    true, 0.80f, 2200,HapticPattern.Vibrate);
            DoubleCumshot = Add(cfg, "Combat.DoubleCumshot","Battle ends in a double climax (max!).",  true, 1.00f, 3000,HapticPattern.Vibrate);

            // ── Hot scenes ──
            HotSceneStart   = Add(cfg, "HotScene.Start",   "A hot scene opens.",                   true, 0.40f, 600, HapticPattern.Pulse);
            HotSceneAdvance = Add(cfg, "HotScene.Advance", "Advance to the next illustration.",    true, 0.55f, 450, HapticPattern.Pulse);
            HotSceneWin     = Add(cfg, "HotScene.Win",     "Won a hot scene.",                     true, 0.95f, 2500,HapticPattern.Vibrate);
            HotSceneLose    = Add(cfg, "HotScene.Lose",    "Lost a hot scene.",                    true, 0.50f, 1200,HapticPattern.Pulse);

            // ── Brawl ──
            BrawlClimax     = Add(cfg, "Brawl.Climax",     "Brawl minigame climax (cumshot panel).",true, 0.90f, 2200,HapticPattern.Vibrate);

            HapticsLogger.Info(LogCat.System, $"Config loaded — {Events.Count} events bound.");
        }
    }
}
