// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Plugin.cs

#nullable enable
using System;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Minimal, safe plugin bootstrap that relies on correct Dalamud service injection.
    /// Avoids custom interfaces (like AiCompanionPlugin.IPluginLog) that IoC can't resolve.
    /// </summary>
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "AI Companion";

        // ---- Dalamud services (CORRECT NAMESPACES!) ----
        [PluginService] internal IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal IPluginLog Log { get; private set; } = null!;
        [PluginService] internal IFramework Framework { get; private set; } = null!;
        [PluginService] internal IChatGui ChatGui { get; private set; } = null!;

        // ---- Plugin state ----
        private Configuration config = null!;

        // NOTE: Dalamud supports a constructor that receives DalamudPluginInterface.
        // Property injection happens after construction, so don't rely on [PluginService] inside this ctor.
        public Plugin(IDalamudPluginInterface pi)
        {
            // Load or create configuration using the provided interface.
            var cfg = pi.GetPluginConfig() as Configuration ?? new Configuration();
            cfg.Initialize(pi);
            config = cfg;

            // Light log to prove we reached here (Logger is not injected yet—use pi for now if you want).
            // Full logger available after property injection; see OnInjected().
        }

        // Dalamud will perform property injection immediately after constructing the plugin.
        // We can use an explicit method to run any code that depends on injected services safely.

        private void OnInjected()
        {
            try
            {
                // Sanity pings to confirm services are alive
                Log.Info("AI Companion loaded. Config v{Version}", config.Version);

                // If you later wire systems that depend on IFramework / IChatGui, do it here.
                // e.g., start your dispatcher, register commands, open windows, etc.
                // Keep this minimal to avoid new load-time failures.
            }
            catch (Exception ex)
            {
                // Make failures visible in Dalamud logs without crashing load.
                try { Log.Error(ex, "Initialization after injection failed."); } catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            // Unhook anything you add later (events, commands, windows, dispatchers).
            try
            {
                Log.Info("AI Companion disposed.");
            }
            catch { /* logger may be unavailable during shutdown */ }
        }
    }
}
