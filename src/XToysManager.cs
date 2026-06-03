using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RmwHaptics
{
    /// <summary>
    /// XToys (xtoys.app) cloud Private Webhook output — fires in parallel with Buttplug.
    /// Protocol (verified live June 2026):
    ///   POST https://webhook.xtoys.app/&lt;webhookId&gt;
    ///   Body: {"action":"setIntensity","intensity":&lt;0-100&gt;}
    /// action keyword is camelCase "setIntensity" (hyphenated "set-intensity" is silently ignored).
    /// Requires a running XToys script with a Global Trigger consuming the webhook
    /// (any script with a setIntensity Global Trigger works; the published xtoys.app/scripts/7dtvibe is one).
    /// </summary>
    public static class XToysManager
    {
        private static readonly HttpClient _http;
        private const string BaseUrl = "https://webhook.xtoys.app";

        private static string _webhookId         = "";
        private static int    _lastSentIntensity  = -1;
        private static CancellationTokenSource? _decayCts;
        private static readonly object           _decayLock = new object();

        static XToysManager()
        {
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public static bool   IsEnabled  => HapticsConfig.XToysEnabled?.Value == true
                                         && !string.IsNullOrWhiteSpace(_webhookId);
        public static string WebhookId  => _webhookId;

        public static void Configure(string webhookId)
        {
            _webhookId = webhookId?.Trim() ?? "";
            if (IsEnabled)
                HapticsLogger.Info(LogCat.XToys, "Configured — webhook ID ready.");
            else
                HapticsLogger.Info(LogCat.XToys, "Webhook ID empty or XToys disabled — output off.");
        }

        public static void Fire(float intensity, int durationMs)
        {
            if (!IsEnabled) return;

            double multiplier     = HapticsConfig.XToysMultiplier?.Value ?? 1.0f;
            int    scaled         = (int)Math.Max(0, Math.Min(100, intensity * multiplier * 100.0));
            int    minDur         = HapticsConfig.XToysMinDurationMs?.Value ?? 300;
            int    effectiveDurMs = Math.Max(durationMs, minDur);

            _ = FireAsync(scaled, effectiveDurMs);
        }

        public static Task FireRawAsync(int intensity, int durationMs)
        {
            if (string.IsNullOrWhiteSpace(_webhookId))
            {
                HapticsLogger.Warning(LogCat.XToys, "Test fired but webhook ID is not set.");
                return Task.CompletedTask;
            }
            HapticsLogger.Info(LogCat.XToys, $"Test: {intensity}% for {durationMs}ms…");
            return FireAsync(intensity, durationMs);
        }

        public static async Task StopAsync()
        {
            lock (_decayLock) { _decayCts?.Cancel(); }
            await SendIntensityAsync(0);
        }

        private static async Task FireAsync(int intensity, int durationMs)
        {
            CancellationTokenSource cts;
            lock (_decayLock)
            {
                _decayCts?.Cancel();
                _decayCts = new CancellationTokenSource();
                cts = _decayCts;
            }

            await SendIntensityAsync(intensity);

            try
            {
                await Task.Delay(durationMs, cts.Token);
                await SendIntensityAsync(0);
            }
            catch (OperationCanceledException) { }
        }

        private static async Task SendIntensityAsync(int intensity)
        {
            intensity = Math.Max(0, Math.Min(100, intensity));
            if (intensity == _lastSentIntensity) return;

            string url  = $"{BaseUrl}/{Uri.EscapeDataString(_webhookId)}";
            string json = $"{{\"action\":\"setIntensity\",\"intensity\":{intensity}}}";
            try
            {
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(url, content).ConfigureAwait(false);
                _lastSentIntensity = intensity;
                HapticsLogger.Verbose(LogCat.XToys, $"→ {intensity}%  HTTP {(int)resp.StatusCode}");
            }
            catch (TaskCanceledException)
            {
                HapticsLogger.Warning(LogCat.XToys, "Request timed out (10s) — xtoys.app unreachable?");
            }
            catch (Exception ex)
            {
                HapticsLogger.Warning(LogCat.XToys, $"Send failed: {ex.Message}");
            }
        }
    }
}
