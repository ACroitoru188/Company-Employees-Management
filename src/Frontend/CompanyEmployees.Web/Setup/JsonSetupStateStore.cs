using System.Text.Json;

namespace CompanyEmployees.Web.Setup;

/// <summary>
/// Reads/writes setup state to App_Data/setup-state.json.
/// </summary>
public sealed class JsonSetupStateStore : ISetupStateStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private readonly string _filePath;

    public JsonSetupStateStore(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "setup-state.json");
    }

    public SetupState Load()
    {
        if (!File.Exists(_filePath))
            return new SetupState(false, null, null, null, null);

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SetupState>(json, JsonOptions)
                   ?? new SetupState(false, null, null, null, null);
        }
        catch
        {
            return new SetupState(false, null, null, null, null);
        }
    }

    public async Task SaveAsync(SetupState state, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }
}
