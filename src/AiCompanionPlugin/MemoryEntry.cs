using System;

namespace AiCompanionPlugin;

public sealed record MemoryEntry(
    DateTime TimestampUtc,
    string Role,
    string Content
);
