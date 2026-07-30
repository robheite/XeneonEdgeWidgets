using System.Security.Cryptography;

namespace EdgeCompanion.Host;

public sealed class ActionTokenProvider
{
    private const string TokenFileName = "action-token";
    private readonly string _token;

    public ActionTokenProvider(IConfiguration configuration)
        : this(
            configuration["EDGE_COMPANION_TOKEN"],
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XeneonEdgeWidgets",
                "EdgeCompanion",
                TokenFileName))
    {
    }

    public ActionTokenProvider(string? configuredToken, string tokenPath)
    {
        _token = string.IsNullOrWhiteSpace(configuredToken)
            ? LoadOrCreate(tokenPath)
            : configuredToken;
    }

    public string Token => _token;

    private static string LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Action token path has no parent directory.");
        Directory.CreateDirectory(directory);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, token);
        File.Move(temporaryPath, path, overwrite: true);
        return token;
    }
}
