using System;
using System.Collections.Concurrent;
using System.Threading;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Runs small actions on the next/safe Framework tick, with simple rate limiting,
/// so outbound chat leaves outside the chat hook call stack.
/// </summary>
public sealed class OutboundDispatcher : IDisposable
{
    private readonly IFramework framework;
    private readonly IPluginLog log;

    private readonly ConcurrentQueue<Action> work = new();
    private long lastSendMs = 0;

    // Minimum spacing between actual sends to avoid throttles.
    public int MinIntervalMs { get; set; } = 250;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public OutboundDispatcher(IFramework framework, IPluginLog log)
    {
        this.framework = framework;
        this.log = log;
        this.framework.Update += OnUpdate;
    }

    public void Enqueue(Action action)
    {
        if (action == null) return;
        work.Enqueue(action);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private void OnUpdate(IFramework _)
    {
        // Drain only a few per frame (we just need to ensure we're out of the chat detour call stack)
        int processed = 0;
        while (processed < 4 && work.TryDequeue(out var a))
        {
            // soft rate limit between individual sends
            var now = Environment.TickCount64;
            var wait = (lastSendMs + MinIntervalMs) - now;
            if (wait > 0)
                Thread.Sleep((int)wait);

            try { a(); }
            catch (Exception ex) { log.Error(ex, "OutboundDispatcher action failed"); }
            lastSendMs = Environment.TickCount64;
            processed++;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public void Dispose()
    {
        framework.Update -= OnUpdate;
        while (work.TryDequeue(out _)) { }
    }
}
