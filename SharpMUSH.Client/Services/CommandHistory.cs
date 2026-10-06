using System.Text.Json;
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The commands this browser has sent, newest last, for Up and Down to recall as a telnet client's input line
/// does. Shared by the terminal and the composer's Command mode, and kept in <c>localStorage</c> so it survives
/// a reload. A line that carries a password is never kept.
/// </summary>
public sealed class CommandHistory(IJSRuntime js)
{
	/// <summary>How many commands are kept; the oldest go first.</summary>
	public const int Capacity = 200;

	private const string StorageKey = "play.history";

	private readonly List<string> _entries = [];
	private Task? _load;

	/// <summary>Oldest first.</summary>
	public IReadOnlyList<string> Entries => _entries;

	/// <summary>Reads the kept history once; later calls return the same task.</summary>
	public Task LoadAsync() => _load ??= LoadCoreAsync();

	/// <summary>
	/// Keeps <paramref name="command"/> as the newest entry: not when it is blank, carries a password, or repeats
	/// the newest entry (a command sent five times in a row is one step back, not five).
	/// </summary>
	public async Task AddAsync(string command)
	{
		await LoadAsync();
		var line = command.Trim();
		if (line.Length == 0 || CarriesSecret(line)) return;
		if (_entries.Count > 0 && _entries[^1] == line) return;
		_entries.Add(line);
		if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
		await js.SetItemAsync(BrowserStore.Local, StorageKey, JsonSerializer.Serialize(_entries));
	}

	/// <summary>
	/// Commands whose arguments include a password: logging in (<c>connect</c> and its <c>cd</c>, <c>cv</c> and
	/// <c>ch</c> forms, <c>login</c>, <c>create</c>, <c>register</c>, <c>make</c>, <c>claim</c>, <c>@account/claim</c>) and changing one, <c>@account/newpassword</c> included. Kept out of history, which
	/// sits in plain <c>localStorage</c>. The verb ends at any whitespace, a tab as much as a space.
	/// </summary>
	public static bool CarriesSecret(string line)
	{
		var verb = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		var lower = verb.ToLowerInvariant();
		return lower is "connect" or "co" or "cd" or "cv" or "ch" or "login" or "create" or "cr"
			or "register" or "make" or "claim" or "@password" or "@newpassword" or "@pcreate"
			|| lower.StartsWith("@account/claim", StringComparison.Ordinal)
			|| lower.StartsWith("@account/newpassword", StringComparison.Ordinal);
	}

	private async Task LoadCoreAsync()
	{
		if (await js.GetItemAsync(BrowserStore.Local, StorageKey) is not { Length: > 0 } json) return;
		try
		{
			if (JsonSerializer.Deserialize<List<string>>(json) is { } kept)
			{
				_entries.InsertRange(0, kept.Where(e => !string.IsNullOrWhiteSpace(e)).TakeLast(Capacity));
			}
		}
		catch (JsonException)
		{
			// Not ours, or damaged: start afresh rather than fail the input line.
		}
	}
}

/// <summary>
/// One input line's walk through <see cref="CommandHistory"/>. Up goes back, Down forward, and Down past the
/// newest entry gives back what was being typed. Text typed before the first Up narrows the walk to entries
/// that start with it, as TinyFugue's history search does. Typing ends the walk.
/// </summary>
public sealed class HistoryWalk
{
	private int _index = -1;
	private string _draft = string.Empty;

	/// <summary>Whether Up has been pressed since the last typing or send.</summary>
	public bool Walking => _index >= 0;

	/// <summary>The entry before the current one that matches the draft, or null when there is none.</summary>
	public string? Older(IReadOnlyList<string> entries, string current)
	{
		if (!Walking)
		{
			_draft = current;
			_index = entries.Count;
		}
		for (var i = Math.Min(_index, entries.Count) - 1; i >= 0; i--)
		{
			if (!Matches(entries[i])) continue;
			_index = i;
			return entries[i];
		}
		if (_index == entries.Count) _index = -1;
		return null;
	}

	/// <summary>The entry after the current one that matches the draft, or the draft itself past the newest.</summary>
	public string? Newer(IReadOnlyList<string> entries)
	{
		if (!Walking) return null;
		for (var i = _index + 1; i < entries.Count; i++)
		{
			if (!Matches(entries[i])) continue;
			_index = i;
			return entries[i];
		}
		Reset();
		return _draft;
	}

	/// <summary>Ends the walk: the player typed, or sent.</summary>
	public void Reset() => _index = -1;

	private bool Matches(string entry) =>
		_draft.Length == 0 || entry.StartsWith(_draft, StringComparison.OrdinalIgnoreCase);
}
