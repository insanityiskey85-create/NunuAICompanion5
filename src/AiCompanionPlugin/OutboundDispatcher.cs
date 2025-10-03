using System;
using System.Collections.Concurrent;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Queues outbound chat actions and executes them on Framework.Update
/// with a minimum interval to avoid flood/hitches.
/// </summary>
internal sealed class OutboundDispatcher : IDisposable
{
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly ConcurrentQueue<Action> _queue = new();
    private long _lastTick;
    private bool _subscribed;

    public int MinIntervalMs { get; set; } = 250;

    public OutboundDispatcher(IFramework framework, IPluginLog log)
    {
        _framework = framework;
        _log = log;
        _framework.Update += OnUpdate;
        _subscribed = true;
    }

    public void Enqueue(Action action) => _queue.Enqueue(action);

    private void OnUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        if (now - _lastTick < MinIntervalMs) return;

        if (_queue.TryDequeue(out var a))
        {
            try { a(); }
            catch (Exception ex) { _log.Error(ex, "[OutboundDispatcher] task failed"); }
            finally { _lastTick = now; }
        }
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        _framework.Update -= OnUpdate;
        _subscribed = false;

        while (_queue.TryDequeue(out _)) { }
    }
}
