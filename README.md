# AI Companion (Dalamud Plugin)

Isolated AI chat window for a truly personal in-game companion. No chat channel hooks, no whitelist, no listening—just a private window you summon with `/aic`.

- Target runtime: **.NET 9.0**
- Dalamud API Level: **13**
- Tested with Dalamud **13.0.0.4**

## Build
1. Install .NET 9 SDK.
2. Create a new Class Library project or use this one.
3. Ensure `DalamudPackager.targets` is available via your `$(DalamudLibPath)` (see `.csproj`).
4. Build and pack with Dalamud Packager as per your setup.

## Features
- **Isolated chat window** (no hooks to in-game chat).
- **Persona system** via `persona.txt` with hot-reload.
- **Configurable backend** (URL, API key, model).
- **Streaming** support (SSE) for OpenAI-compatible `/v1/chat/completions`.
- **Theme presets** for the chat window: *Eorzean Night*, *Voidglass*, *Maelstrom Red*, *Gridania Moss*, *Ul'dahn Ember*.

## Usage
- Drop the plugin into your dev plugins folder.
- Launch the game with Dalamud.
- Run `/aic` to open the chat.
- Configure backend + model in **AI Companion Settings**.
- Edit `persona.txt` in the plugin config folder; changes hot-reload.

## Backend Notes
- Works with any OpenAI-compatible endpoint that supports `/v1/chat/completions` and optional SSE streaming.
- For Ollama + OpenAI-compatible proxy, set Base URL and Model accordingly.
