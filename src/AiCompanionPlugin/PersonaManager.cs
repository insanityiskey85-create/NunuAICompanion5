using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class PersonaManager : System.IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private FileSystemWatcher? watcher;

    public string ActiveSystemPrompt => string.IsNullOrWhiteSpace(config.SystemPromptOverride)
        ? DefaultSystemPrompt
        : config.SystemPromptOverride;

    public static readonly string DefaultSystemPrompt =
        "You are a personal, isolated FFXIV companion.\n" +
        "You exist only in this private window. Do not read or write to game chat.\n" +
        "Be concise, kind, and focused on the player's goals.\n";

    public PersonaManager(IDalamudPluginInterface pi, IPluginLog log, Configuration config)
    {
        this.pi = pi; this.log = log; this.config = config;
        EnsurePersonaFile();
        StartWatcher();
    }

    private void EnsurePersonaFile()
    {
        var path = config.GetPersonaAbsolutePath(pi);
        if (!File.Exists(path))
        {
            File.WriteAllText(path,
                "You are a helpful, strictly isolated personal AI companion.\n" +
                "Stay within this private window. Do not read or write to game chat.\n" +
                "Style: concise, kind, and attentive to the user's goals.\n");
            log.Info($"Created default persona at {path}");
        }

        LoadPersona(path);
    }

    private void StartWatcher()
    {
        var dir = pi.GetPluginConfigDirectory();
        watcher = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            Filter = System.IO.Path.GetFileName(config.PersonaFileRelative),
            EnableRaisingEvents = true,
        };
        watcher.Changed += OnPersonaChanged;
        watcher.Created += OnPersonaChanged;
        watcher.Renamed += OnPersonaChanged;
    }

    private void OnPersonaChanged(object sender, FileSystemEventArgs e)
    {
        try { LoadPersona(e.FullPath); }
        catch (System.Exception ex) { log.Error(ex, "Failed to reload persona.txt"); }
    }

    private void LoadPersona(string path)
    {
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        config.SystemPromptOverride = text;
        config.Save();
        log.Info("Persona reloaded.");
    }

    public void Dispose()
    {
        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnPersonaChanged;
            watcher.Created -= OnPersonaChanged;
            watcher.Renamed -= OnPersonaChanged;
            watcher.Dispose();
        }
    }
}
