using System.Text.Json;

namespace KittyClaw.Core.Automation;

/// <summary>
/// Persists roster configuration: execution slots, presets, and fallback policies.
/// Storage location: {dataDir}/roster/
/// </summary>
public class RosterStore
{
    private readonly string _rosterDir;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Dictionary<string, ExecutionSlot> _slots = new();
    private Dictionary<string, RosterPreset> _presets = new();
    private Dictionary<string, FallbackPolicy> _fallbacks = new();
    private string _activePresetId = "";

    public RosterStore(string dataDir)
    {
        _rosterDir = Path.Combine(dataDir, "roster");
        Directory.CreateDirectory(_rosterDir);
    }

    public IReadOnlyDictionary<string, ExecutionSlot> Slots => _slots;
    public IReadOnlyDictionary<string, RosterPreset> Presets => _presets;
    public IReadOnlyDictionary<string, FallbackPolicy> Fallbacks => _fallbacks;
    public string ActivePresetId => _activePresetId;

    public void Load()
    {
        LoadSlots();
        LoadPresets();
        LoadFallbacks();
    }

    public void Save()
    {
        SaveSlots();
        SavePresets();
        SaveFallbacks();
    }

    // --- Slots ---

    public ExecutionSlot? GetSlot(string id) => _slots.GetValueOrDefault(id);
    
    public void UpsertSlot(ExecutionSlot slot)
    {
        _slots[slot.Id] = slot;
        SaveSlots();
    }

    public void RemoveSlot(string id)
    {
        _slots.Remove(id);
        SaveSlots();
    }

    // --- Presets ---

    public RosterPreset? GetPreset(string id) => _presets.GetValueOrDefault(id);
    
    public RosterPreset? GetActivePreset() => _presets.Values.FirstOrDefault(p => p.IsActive);
    
    public void ActivatePreset(string presetId)
    {
        foreach (var p in _presets.Values)
        {
            p.IsActive = p.Id == presetId;
            if (p.IsActive)
            {
                p.ActivatedAt = DateTime.UtcNow;
            }
        }
        _activePresetId = presetId;
        SavePresets();
    }

    public void UpsertPreset(RosterPreset preset)
    {
        _presets[preset.Id] = preset;
        SavePresets();
    }

    // --- Fallbacks ---

    public FallbackPolicy? GetFallback(string id) => _fallbacks.GetValueOrDefault(id);
    
    public void UpsertFallback(FallbackPolicy policy)
    {
        _fallbacks[policy.Id] = policy;
        SaveFallbacks();
    }

    // --- File I/O ---

    private void LoadSlots()
    {
        var path = Path.Combine(_rosterDir, "slots.json");
        if (!File.Exists(path)) return;
        
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<ExecutionSlot>>(json, _jsonOptions) ?? new();
            _slots = list.ToDictionary(s => s.Id);
        }
        catch { /* ignore corrupt file */ }
    }

    private void SaveSlots()
    {
        var path = Path.Combine(_rosterDir, "slots.json");
        var json = JsonSerializer.Serialize(_slots.Values.ToList(), _jsonOptions);
        File.WriteAllText(path, json);
    }

    private void LoadPresets()
    {
        var path = Path.Combine(_rosterDir, "presets.json");
        if (!File.Exists(path)) return;
        
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<RosterPreset>>(json, _jsonOptions) ?? new();
            _presets = list.ToDictionary(p => p.Id);
            _activePresetId = _presets.Values.FirstOrDefault(p => p.IsActive)?.Id ?? "";
        }
        catch { /* ignore corrupt file */ }
    }

    private void SavePresets()
    {
        var path = Path.Combine(_rosterDir, "presets.json");
        var json = JsonSerializer.Serialize(_presets.Values.ToList(), _jsonOptions);
        File.WriteAllText(path, json);
    }

    private void LoadFallbacks()
    {
        var path = Path.Combine(_rosterDir, "fallbacks.json");
        if (!File.Exists(path)) return;
        
        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<FallbackPolicy>>(json, _jsonOptions) ?? new();
            _fallbacks = list.ToDictionary(f => f.Id);
        }
        catch { /* ignore corrupt file */ }
    }

    private void SaveFallbacks()
    {
        var path = Path.Combine(_rosterDir, "fallbacks.json");
        var json = JsonSerializer.Serialize(_fallbacks.Values.ToList(), _jsonOptions);
        File.WriteAllText(path, json);
    }
}
