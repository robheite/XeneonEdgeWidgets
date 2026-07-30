using System.Text.Json;

namespace EdgeCompanion.Host.Modules;

public sealed record PauseState(DateTimeOffset PausedUntil);

public interface IPauseStateStore
{
    PauseState? Read();
    void Write(PauseState state);
    void Delete();
}

public sealed class PauseStateStore : IPauseStateStore
{
    private readonly string _path;

    public PauseStateStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XeneonEdgeWidgets",
        "EdgeCompanion",
        "nordvpn-pause.json"))
    {
    }

    public PauseStateStore(string path)
    {
        _path = path;
    }

    public PauseState? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<PauseState>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            Delete();
            return null;
        }
    }

    public void Write(PauseState state)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Pause state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
