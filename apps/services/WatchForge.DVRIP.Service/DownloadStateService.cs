namespace WatchForge.DVRIP.Service;

/// <summary>
/// Thread-safe persistent store for tracking which NVR files have already been downloaded.
/// State is persisted as JSON in {DownloadDir}/downloaded.json.
/// </summary>
public sealed class DownloadStateService
{
    private readonly string _statePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Key: "Ch{n}", Value: set of NVR FileName strings already downloaded
    private Dictionary<string, HashSet<string>> _state = new();

    public DownloadStateService(string downloadDir)
    {
        _statePath = Path.Combine(downloadDir, "downloaded.json");
        Load();
    }

    /// <summary>Returns true if the given NVR file has already been downloaded.</summary>
    public bool IsDownloaded(int channel, string nvrFileName)
    {
        var key = $"Ch{channel}";
        return _state.TryGetValue(key, out var set) && set.Contains(nvrFileName);
    }

    /// <summary>Marks a file as downloaded and persists the state atomically.</summary>
    public async Task MarkDownloadedAsync(int channel, string nvrFileName)
    {
        await _lock.WaitAsync();
        try
        {
            var key = $"Ch{channel}";
            if (!_state.TryGetValue(key, out var set))
                _state[key] = set = [];
            set.Add(nvrFileName);
            await SaveAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Load()
    {
        if (!File.Exists(_statePath)) return;
        try
        {
            var json = File.ReadAllText(_statePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (dict is not null)
                _state = dict.ToDictionary(
                    kv => kv.Key,
                    kv => new HashSet<string>(kv.Value));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️  Could not load download state: {ex.Message} — starting fresh.");
        }
    }

    private async Task SaveAsync()
    {
        var dict = _state.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value);
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        var tmpPath = _statePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, _statePath, overwrite: true);
    }

    public int TotalDownloaded => _state.Values.Sum(s => s.Count);
}
