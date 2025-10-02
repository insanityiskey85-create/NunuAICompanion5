# AI Companion (Dalamud Plugin)

Isolated AI chat window for a truly personal in-game companion. No chat channel hooks, no whitelist, no listening—just a private window you summon with `/aic`.

- Target runtime: **.NET 9.0**
- Dalamud API Level: **13**
- Tested with Dalamud **13.0.0.4**

## Build
1. Install **.NET 9 SDK**.
2. Open `NunuAICompanion5.sln` in Visual Studio 2022+.
3. Ensure `DalamudPackager.targets` is available via your `$(DalamudLibPath)` (see `.csproj`).
4. Build `Release` to produce a packaged zip (per your packager setup).
5. Install the zip via Dalamud’s Plugin Installer (Local Dev) or drop into your dev plugins folder.

## Features
- **Isolated chat window** (no hooks to in-game chat).
- **Persona system** via `persona.txt` with hot-reload.
- **Configurable backend** (URL, API key, model).
- **Streaming** support (SSE) for OpenAI-compatible `/v1/chat/completions`.
- **Theme presets** for the chat window.
- **Memories**: Save snippets to `memories.json` (viewer, search, delete, clear).

## Commands
- `/aic` — open the AI Companion window.
