using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Configuration;

namespace RmwHaptics
{
    public enum HapticPattern      { Vibrate, Pulse }
    public enum HapticActuatorType { All, Vibrate, Linear, Rotate }

    /// <summary>Master haptic mode. Discrete = event pulses; Continuous = battle-meter buzz.</summary>
    public enum HapticMode    { Off, Discrete, Continuous, Both }

    /// <summary>Which battle meter drives a given toy in continuous mode.</summary>
    public enum ArousalSource { Off, Robin, Enemy, Both }

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

            // Mode gate: discrete events only fire in Discrete/Both modes.
            var mode = HapticsConfig.Mode.Value;
            if (mode == HapticMode.Off || mode == HapticMode.Continuous) return;

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
        public static ConfigEntry<HapticMode> Mode        = null!;

        // ── Continuous (battle arousal) ──
        public static ConfigEntry<bool>  ContinuousInvert     = null!;  // arousal = 1-life (swell toward climax)
        public static ConfigEntry<float> ContinuousMultiplier = null!;
        public static ConfigEntry<float> ContinuousMin        = null!;  // floor when arousal > 0
        public static ConfigEntry<float> ContinuousMax        = null!;  // ceiling
        public static ConfigEntry<ArousalSource> XToysSource  = null!;  // which meter XToys follows
        public static ConfigEntry<string> ToyRouting          = null!;  // "deviceName=Source;…"

        // In-memory per-device routing (mirrors ToyRouting string), editable by the GUI.
        private static readonly Dictionary<string, ArousalSource> _route = new Dictionary<string, ArousalSource>();

        /// <summary>Source assigned to a toy; defaults: slot 0 → Robin, others → Enemy.</summary>
        public static ArousalSource RouteFor(string deviceName, int slot)
        {
            if (_route.TryGetValue(deviceName, out var s)) return s;
            return slot == 0 ? ArousalSource.Robin : ArousalSource.Enemy;
        }

        public static void SetRoute(string deviceName, ArousalSource src)
        {
            _route[deviceName] = src;
            var parts = new List<string>();
            foreach (var kv in _route) parts.Add($"{kv.Key}={kv.Value}");
            ToyRouting.Value = string.Join(";", parts);
        }

        private static void LoadRouting()
        {
            _route.Clear();
            foreach (var pair in (ToyRouting.Value ?? "").Split(';'))
            {
                int eq = pair.LastIndexOf('=');
                if (eq <= 0) continue;
                string name = pair.Substring(0, eq).Trim();
                if (Enum.TryParse(pair.Substring(eq + 1).Trim(), out ArousalSource s) && name.Length > 0)
                    _route[name] = s;
            }
        }

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
            Mode             = cfg.Bind("General", "Mode", HapticMode.Both,
                "Off / Discrete (event pulses) / Continuous (battle-meter buzz) / Both.");

            ContinuousInvert     = cfg.Bind("Continuous", "Invert", true,
                "true: vibration swells as the character's meter drains toward climax (arousal = 1 − life). " +
                "false: vibration tracks remaining life directly.");
            ContinuousMultiplier = cfg.Bind("Continuous", "Multiplier", 1.0f,
                "Overall intensity scale for the continuous battle buzz.");
            ContinuousMin        = cfg.Bind("Continuous", "MinIntensity", 0.08f,
                "Floor intensity once arousal is above zero (so a faint buzz is always felt).");
            ContinuousMax        = cfg.Bind("Continuous", "MaxIntensity", 1.0f,
                "Ceiling intensity at full arousal.");
            XToysSource          = cfg.Bind("Continuous", "XToysSource", ArousalSource.Robin,
                "Which meter the XToys output follows in continuous mode (Off to disable).");
            ToyRouting           = cfg.Bind("Continuous", "ToyRouting", "",
                "Per-toy meter routing, 'DeviceName=Robin;OtherToy=Enemy'. Edited via the in-game panel. " +
                "Unlisted toys default to slot 0 → Robin, others → Enemy.");
            LoadRouting();

            XToysEnabled       = cfg.Bind("XToys", "Enabled", false,
                "Enable the XToys cloud webhook output (fires alongside Intiface).");
            XToysWebhookId     = cfg.Bind("XToys", "WebhookId", "",
                "Your XToys Private Webhook ID. Run an XToys script with a Private Webhook + Generic Output " +
                "and a setIntensity Global Trigger (the published xtoys.app/scripts/7dtvibe works as-is), " +
                "connect your toy, then paste the webhook ID here. Treat it like a password.");
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
