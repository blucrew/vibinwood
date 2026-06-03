using System;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace RmwHaptics
{
    /// <summary>
    /// 7 Days to Vibe — Robin Morningwood Adventure (The Whellcum's Secret) edition.
    /// BepInEx 6 IL2CPP plugin. Drives Intiface/Buttplug + XToys haptics from in-game events.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BasePlugin
    {
        public const string GUID    = "com.7daystovibe.rmw";
        public const string NAME    = "RMW Haptics (7 Days to Vibe)";
        public const string VERSION = "0.1.0";

        private Harmony _harmony = null!;

        public override void Load()
        {
            // Buttplug's WebsocketConnector hard-references System.Threading.Channels 7.0.0.0,
            // but BepInEx's bundled .NET 6 runtime pins it at 6.0.0.0 in the TPA list, so CoreCLR
            // refuses to load the 7.0 identity (FileLoadException 0x80131621). The 6.0 API surface
            // is compatible, so redirect any same-named assembly request to the already-loaded /
            // runtime version. Must be registered before Buttplug touches those types.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveToLoadedVersion;

            HapticsLogger.Init(Log, Config);
            HapticsConfig.Init(Config);

            // XToys: configure now and re-apply if the webhook ID changes at runtime.
            XToysManager.Configure(HapticsConfig.XToysWebhookId.Value);
            HapticsConfig.XToysWebhookId.SettingChanged += (_, _) =>
                XToysManager.Configure(HapticsConfig.XToysWebhookId.Value);

            // Intiface connects on a background thread so a missing server never blocks load.
            Task.Run(async () =>
            {
                try { await ButtplugManager.InitAsync(); }
                catch (Exception e) { HapticsLogger.Error(LogCat.Device, $"Intiface init failed: {e.Message}"); }
            });

            _harmony = new Harmony(GUID);
            ApplyPatches();

            HapticsLogger.Info(LogCat.System, $"{NAME} v{VERSION} loaded.");
        }

        /// <summary>
        /// Resolve a failed assembly load to a same-named assembly that is already loaded
        /// (or loadable by simple name from the runtime), ignoring the requested version.
        /// Fixes the Channels 7.0.0.0 → 6.0.0.0 identity mismatch under BepInEx's net6 CoreCLR.
        /// </summary>
        private static Assembly? ResolveToLoadedVersion(object? sender, ResolveEventArgs args)
        {
            string requested = new AssemblyName(args.Name).Name ?? "";
            if (requested.Length == 0) return null;

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase))
                    return a;

            try { return Assembly.Load(new AssemblyName(requested)); }
            catch { return null; }
        }

        /// <summary>
        /// Patch each game method individually with try/catch so one renamed/missing
        /// target (e.g. across a game update) only drops that single hook.
        /// </summary>
        private void ApplyPatches()
        {
            int ok = 0, fail = 0;

            void P(Type t, string method, string hook)
            {
                try
                {
                    var orig = AccessTools.Method(t, method);
                    if (orig == null)
                    {
                        HapticsLogger.Warning(LogCat.Patch, $"✗ {t?.Name}.{method} — method not found");
                        fail++;
                        return;
                    }
                    var post = new HarmonyMethod(AccessTools.Method(typeof(Hooks), hook));
                    _harmony.Patch(orig, postfix: post);
                    HapticsLogger.Info(LogCat.Patch, $"✓ {t.Name}.{method}");
                    ok++;
                }
                catch (Exception e)
                {
                    HapticsLogger.Warning(LogCat.Patch, $"✗ {t?.Name}.{method} — {e.Message}");
                    fail++;
                }
            }

            // ── Combat (BattleScreenController) ──
            P(typeof(BattleScreenController), "Attack",                 nameof(Hooks.RobinAttack));
            P(typeof(BattleScreenController), "ExecuteEnemyAttack",     nameof(Hooks.EnemyAttack));
            P(typeof(BattleScreenController), "DisplayRobinsLife",      nameof(Hooks.RobinHurt));
            P(typeof(BattleScreenController), "JerkOff",                nameof(Hooks.JerkOff));
            // NOTE: BattleScreenController.EnemyStartsCumming() is called every frame from the
            // title-screen attract loop (1200+ calls/min) — useless as a discrete hook and would
            // machine-gun the toy on the menu. The enemy-climax moment is already covered by
            // GameOver(EnemyCums) and BrawlCumshotAttackPanel.Play, so it is intentionally not patched.
            P(typeof(BattleScreenController), "ForceGameOverRobinCums", nameof(Hooks.ForceRobinCums));
            P(typeof(BattleScreenController), "GameOver",               nameof(Hooks.GameOver));

            // ── Hot scenes ──
            P(typeof(HotSceneController),     "StartDisplay",     nameof(Hooks.HotSceneStart));
            P(typeof(HotSceneController),     "DisplayNextScene", nameof(Hooks.HotSceneAdvance));
            P(typeof(PlayerSetItemController),"WinHotScene",      nameof(Hooks.HotSceneWin));
            P(typeof(PlayerSetItemController),"LoseHotScene",     nameof(Hooks.HotSceneLose));

            // ── Brawl ──
            P(typeof(BrawlCumshotAttackPanel),"Play",             nameof(Hooks.BrawlClimax));

            HapticsLogger.Info(LogCat.System, $"Patching complete — {ok} ✓ / {fail} ✗.");
        }
    }
}
