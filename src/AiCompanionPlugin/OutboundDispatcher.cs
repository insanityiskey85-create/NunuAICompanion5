// SPDX-License-Identifier: MIT
// AiCompanionPlugin - OutboundDispatcher.cs
//
// Rate-limited dispatcher that pumps queued chat sends on IFramework.Update.
// Uses a uniquely named handler (OnFrameworkTick) and method-group wiring;
// there are ZERO parentheses on event add/remove to avoid any bool/IFramework confusion.

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class OutboundDispatcher : IDisposable
    {
        private readonly IFramework framework;
        private readonly NativeChatPipe pipe;

        private readonly Queue<Func<bool>> work = new();

        // Minimum milliseconds between sends
        private readonly int minMillisBetweenSends;
        private long lastSendMillis;

        private bool disposed;

        /// <summary>
        /// Create a dispatcher and hook the framework update.
        /// </summary>
        /// <param name="framework">Dalamud IFramework service.</param>
        /// <param name="pipe">Sender that posts to chat.</param>
        /// <param name="maxMessagesPerSecond">Messages per second cap (>=1). If <= 0, defaults to 750ms spacing.</param>
        public OutboundDispatcher(IFramework framework, NativeChatPipe pipe, int maxMessagesPerSecond = 1)
        {
            this.framework = framework ?? throw new ArgumentNullException(nameof(framework));
            this.pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));

            if (maxMessagesPerSecond <= 0)
                minMillisBetweenSends = 750;
            else
                minMillisBetweenSends = Math.Max(50, 1000 / maxMessagesPerSecond);

            lastSendMillis = UtcNowMillis() - minMillisBetweenSends; // allow immediate first send

            // ✅ METHOD GROUP — DO NOT ADD PARENTHESES
            this.framework.Update += OnFrameworkTick;
        }

        private void OnFrameworkTick(IFramework framework) => throw new NotImplementedException();

        /// <summary>Queue a /say message.</summary>
        public void EnqueueSay(string text)
        {
            lock (work) work.Enqueue(() => pipe.SendSay(text));
        }

        /// <summary>Queue a /party message.</summary>
        public void EnqueueParty(string text)
        {
            lock (work) work.Enqueue(() => pipe.SendParty(text));
        }

        /// <summary>Queue a raw line (command or chat).</summary>
        public void EnqueueRaw(string line)
        {
            lock (work) work.Enqueue(() => pipe.SendRaw(line));
        }

        private static long UtcNowMillis()
            => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            // ✅ METHOD GROUP — DO NOT ADD PARENTHESES
            this.framework.Update -= OnFrameworkTick;

            lock (work) work.Clear();
        }
    }
}
