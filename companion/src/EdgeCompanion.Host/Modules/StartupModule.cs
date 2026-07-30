using Microsoft.Win32;
using System.Runtime.Versioning;

namespace EdgeCompanion.Host.Modules;

public sealed record StartupResult(bool Enabled, bool Supported, string Status);
public sealed record StartupRequest(bool Enabled);

public interface IStartupStore
{
    string? Read();
    void Write(string command);
    void Delete();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryStartupStore : IStartupStore
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EdgeCompanion";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public sealed class StartupModule
{
    private readonly IStartupStore _store;
    private readonly string? _executablePath;

    public StartupModule() : this(CreateStore(), Environment.ProcessPath)
    {
    }

    public StartupModule(IStartupStore store, string? executablePath)
    {
        _store = store;
        _executablePath = executablePath;
    }

    public StartupResult Get()
    {
        if (!IsSupportedPath(_executablePath))
            return new(false, false, "unsupported_install");
        return new(
            string.Equals(_store.Read(), Command, StringComparison.OrdinalIgnoreCase),
            true,
            "available");
    }

    public StartupResult Set(bool enabled)
    {
        if (!IsSupportedPath(_executablePath))
            throw new ModuleException(
                "unsupported_install",
                "Automatic startup is available after Edge Companion is installed or extracted to a stable folder",
                409);
        if (enabled) _store.Write(Command);
        else _store.Delete();
        return Get();
    }

    public static bool IsSupportedPath(string? path)
    {
        if (!OperatingSystem.IsWindows()
            || string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(path)
            || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        var normalized = path.Replace('/', '\\');
        return !normalized.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
    }

    private string Command => $"\"{_executablePath}\"";

    private static IStartupStore CreateStore() =>
        OperatingSystem.IsWindows() ? new WindowsRegistryStartupStore() : new UnsupportedStartupStore();

    private sealed class UnsupportedStartupStore : IStartupStore
    {
        public string? Read() => null;
        public void Write(string command) => throw new PlatformNotSupportedException();
        public void Delete() => throw new PlatformNotSupportedException();
    }
}
