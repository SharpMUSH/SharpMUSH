using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Represents a single entry from mush-defs.json (function or command).
/// </summary>
public record HelpEntry(
    string Name,
    bool IsCommand,
    string HelpFull,
    string HelpPreview,
    string[] ParameterNames,
    int MinArgs,
    int MaxArgs,
    string[] Switches);

/// <summary>
/// Singleton service that owns the MUSH helpfile data loaded from mush-defs.json.
/// Provides lookup, listing, and a navigation event so any component can open the
/// HelpDrawer and navigate to a specific entry.
/// </summary>
public sealed class HelpService
{
    private readonly HttpClient _http;
    private Dictionary<string, HelpEntry>? _index;

    /// <summary>Fired when a component (or Monaco interop) requests navigation to an entry.</summary>
    public event Action<string>? OnNavigate;

    /// <summary>Fired when the drawer should open/close.</summary>
    public event Action<bool>? OnDrawerToggle;

    public bool IsLoaded => _index is not null;

    public HelpService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Load mush-defs.json once; safe to call multiple times.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_index is not null) return;
        var raw = await _http.GetFromJsonAsync<MushDefsRaw>("data/mush-defs.json");
        _index = BuildIndex(raw);
    }

    private static Dictionary<string, HelpEntry> BuildIndex(MushDefsRaw? raw)
    {
        var idx = new Dictionary<string, HelpEntry>(StringComparer.OrdinalIgnoreCase);

        if (raw?.Functions is not null)
        {
            foreach (var (name, def) in raw.Functions)
            {
                var e = new HelpEntry(
                    name.ToUpperInvariant(), false,
                    def.HelpFull ?? string.Empty,
                    def.HelpPreview ?? string.Empty,
                    def.ParameterNames ?? [],
                    def.MinArgs, def.MaxArgs,
                    def.Switches ?? []);
                idx[name.ToUpperInvariant()] = e;
            }
        }

        if (raw?.Commands is not null)
        {
            foreach (var (name, def) in raw.Commands)
            {
                var e = new HelpEntry(
                    name.ToUpperInvariant(), true,
                    def.HelpFull ?? string.Empty,
                    def.HelpPreview ?? string.Empty,
                    def.ParameterNames ?? [],
                    def.MinArgs, def.MaxArgs,
                    def.Switches ?? []);
                idx[name.ToUpperInvariant()] = e;
            }
        }

        return idx;
    }

    /// <summary>All entries, sorted alphabetically with functions before commands.</summary>
    public IReadOnlyList<HelpEntry> AllEntries =>
        (_index?.Values.OrderBy(e => e.IsCommand).ThenBy(e => e.Name).ToList()
         ?? (IReadOnlyList<HelpEntry>)[]);

    public HelpEntry? Get(string name)
    {
        if (_index is null) return null;
        if (_index.TryGetValue(name, out var e)) return e;
        var atName = name.StartsWith('@') ? name : '@' + name;
        if (_index.TryGetValue(atName, out e)) return e;
        return null;
    }

    /// <summary>Request navigation to a named entry. Opens the drawer if closed.</summary>
    public void NavigateTo(string name)
    {
        OnDrawerToggle?.Invoke(true);
        OnNavigate?.Invoke(name);
    }
}

// ── JSON shape ─────────────────────────────────────────────────────────────────

internal sealed class MushDefsRaw
{
    public Dictionary<string, DefEntry>? Functions { get; set; }
    public Dictionary<string, DefEntry>? Commands { get; set; }
}

internal sealed class DefEntry
{
    public string? HelpFull { get; set; }
    public string? HelpPreview { get; set; }
    public string[]? ParameterNames { get; set; }
    public int MinArgs { get; set; }
    public int MaxArgs { get; set; }
    public string[]? Switches { get; set; }
}

