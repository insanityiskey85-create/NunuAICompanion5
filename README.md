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
- **Memories**: Save conversation snippets to `memories.json` (view, search, delete, clear).
- **Private storage** in the plugin config directory.

## Usage
- `/aic` opens the chat window.
- In **Settings → Memories**, toggle memories, pick file name, and open the file.
- Use **★ Save Input** or **★ Save Last** in the chat window to record notes.
- Click **Memories** to open the viewer (search by text or `#tag`).

## File Locations
- Config & data live in your Dalamud plugin config directory (`persona.txt`, `memories.json`).
