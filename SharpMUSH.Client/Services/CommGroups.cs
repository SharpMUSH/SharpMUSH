using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Where the account's channel groups are kept, as the JSON <see cref="CommGroup.ToJson"/> writes.</summary>
public interface ICommGroupStore
{
	/// <summary>The stored JSON, or <see cref="NotFound"/> when nothing is stored or it could not be read.</summary>
	Task<Found<string>> ReadAsync();

	/// <summary>Stores <paramref name="json"/>; false when it could not be stored.</summary>
	Task<bool> WriteAsync(string json);

	/// <summary>The stored groups changed somewhere else (another tab or device).</summary>
	event Action? Changed;
}

/// <summary>
/// The account's channel groups for the Play page (<see cref="CommGroup"/>): read once, kept in memory, and
/// written whole on each change. Every change is checked here, so the store only ever holds groups
/// <see cref="CommGroup.FromJson"/> reads back unchanged.
/// </summary>
public sealed class CommGroups : IDisposable
{
	private readonly ICommGroupStore _store;
	private IReadOnlyList<CommGroup> _groups = [];
	private Task? _loading;

	/// <summary>One change at a time, from the read it starts from to the groups it publishes, so two changes in flight never write over each other.</summary>
	private readonly SemaphoreSlim _changing = new(1, 1);

	public CommGroups(ICommGroupStore store)
	{
		_store = store;
		_store.Changed += OnStoreChanged;
	}

	/// <summary>The groups in the order they were made; empty until <see cref="LoadAsync"/> has read them.</summary>
	public IReadOnlyList<CommGroup> Groups => _groups;

	/// <summary>The groups changed: read in, saved here, or changed somewhere else.</summary>
	public event Action? Changed;

	/// <summary>Reads the groups the first time it is called; later calls wait for that read.</summary>
	public Task LoadAsync() => _loading ??= ReadAsync();

	/// <summary>
	/// The group a view key names, or <see cref="NotFound"/>. A key under a name the group was renamed from here
	/// still finds it, so a view open on it keeps showing it until it takes the new key.
	/// </summary>
	public Found<CommGroup> Find(string? key)
	{
		if (!CommGroup.IsGroupKey(key)) return new NotFound();
		var name = key![CommGroup.KeyPrefix.Length..];
		for (var hops = 0; hops <= CommGroup.GroupLimit; hops++)
		{
			if (Named(name) is { } group) return group;
			if (!_renamed.TryGetValue(name, out var next)) break;
			name = next;
		}
		return new NotFound();
	}

	private CommGroup? Named(string name) =>
		_groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>Names a group was renamed from in this tab, to the name it took.</summary>
	private readonly Dictionary<string, string> _renamed = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Makes a group, or changes the one called <paramref name="replacing"/>, keeping its place. Refused with the
	/// reason's resource key when the name is empty, too long or taken by another group, when fewer than
	/// <see cref="CommGroup.MinimumChannels"/> or more than <see cref="CommGroup.ChannelLimit"/> channels are chosen, when a new group would pass
	/// <see cref="CommGroup.GroupLimit"/>, or when the store would not take it.
	/// </summary>
	public Task<Result<CommGroup>> SaveAsync(string? replacing, string name, IEnumerable<string> channels,
		IReadOnlyDictionary<string, string>? colors = null) =>
		OneAtATime(() => SaveNowAsync(replacing, name, channels, colors));

	private async Task<Result<CommGroup>> SaveNowAsync(string? replacing, string name, IEnumerable<string> channels,
		IReadOnlyDictionary<string, string>? colors)
	{
		await LoadAsync();
		var trimmed = name.Trim();
		var chosen = channels.Select(c => c.Trim()).Where(c => c.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		var index = replacing is null ? -1 : IndexOf(replacing);
		var refusal = trimmed.Length == 0 ? "NavPlayGroupNameMissing"
			: trimmed.Length > CommGroup.NameLimit ? "NavPlayGroupNameTooLong"
			: _groups.Where((_, i) => i != index).Any(g => string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase)) ? "NavPlayGroupNameTaken"
			: chosen.Length < CommGroup.MinimumChannels ? "NavPlayGroupTooFewChannels"
			: chosen.Length > CommGroup.ChannelLimit ? "NavPlayGroupTooManyChannels"
			: index < 0 && _groups.Count >= CommGroup.GroupLimit ? "NavPlayGroupTooMany"
			: null;
		if (refusal is not null) return new Error<string>(refusal);

		var kept = colors?.Where(pair => chosen.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) && CommGroup.Hues.Contains(pair.Value))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
		var group = new CommGroup(trimmed, chosen, index >= 0 && _groups[index].Folded, kept is { Count: > 0 } ? kept : null);
		var next = _groups.ToList();
		if (index < 0) next.Add(group);
		else next[index] = group;
		var renamedFrom = index >= 0 && !string.Equals(_groups[index].Name, trimmed, StringComparison.OrdinalIgnoreCase)
			? _groups[index].Name
			: null;
		if (!await WriteAsync(next)) return new Error<string>("NavPlayGroupNotSaved");
		if (renamedFrom is not null) _renamed[renamedFrom] = trimmed;
		_renamed.Remove(trimmed);
		return group;
	}

	/// <summary>Removes the group called <paramref name="name"/>; false when the store would not take the change.</summary>
	public Task<bool> RemoveAsync(string name) => OneAtATime(() => RemoveNowAsync(name));

	private async Task<bool> RemoveNowAsync(string name)
	{
		await LoadAsync();
		var index = IndexOf(name);
		if (index < 0) return true;
		var next = _groups.ToList();
		next.RemoveAt(index);
		if (!await WriteAsync(next)) return false;
		foreach (var from in _renamed.Where(pair => string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase))
			.Select(pair => pair.Key).ToList())
			_renamed.Remove(from);
		return true;
	}

	/// <summary>Folds the group called <paramref name="name"/> in the sidebar, or unfolds it; false when the store would not take it.</summary>
	public Task<bool> FoldAsync(string name, bool folded) => OneAtATime(() => FoldNowAsync(name, folded));

	private async Task<bool> FoldNowAsync(string name, bool folded)
	{
		await LoadAsync();
		var index = IndexOf(name);
		if (index < 0 || _groups[index].Folded == folded) return index >= 0;
		var next = _groups.ToList();
		next[index] = next[index] with { Folded = folded };
		return await WriteAsync(next);
	}

	private async Task<T> OneAtATime<T>(Func<Task<T>> change)
	{
		await _changing.WaitAsync();
		try
		{
			return await change();
		}
		finally
		{
			_changing.Release();
		}
	}

	private int IndexOf(string name)
	{
		for (var i = 0; i < _groups.Count; i++)
		{
			if (string.Equals(_groups[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
		}
		return -1;
	}

	private async Task<bool> WriteAsync(IReadOnlyList<CommGroup> groups)
	{
		if (!await _store.WriteAsync(CommGroup.ToJson(groups))) return false;
		_groups = groups;
		Changed?.Invoke();
		return true;
	}

	private async Task ReadAsync()
	{
		_groups = await _store.ReadAsync() switch
		{
			string json => CommGroup.FromJson(json),
			NotFound => [],
		};
		Changed?.Invoke();
	}

	private void OnStoreChanged()
	{
		_loading = ReadAsync();
	}

	public void Dispose() => _store.Changed -= OnStoreChanged;
}
