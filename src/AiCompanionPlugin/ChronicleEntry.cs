using System;

namespace AiCompanionPlugin;

public sealed record ChronicleEntry(
    DateTime TimestampUtc,
    string UserText,
    string AiText,
    string Model,
    string Style // freeform note (e.g., mood/voice)
);
