using System.Collections.Concurrent;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// In-memory, process-lifetime implementation of <see cref="IUserDefinedFunctionService"/>.
///
/// <para>Names are stored lower-cased so lookups are case-insensitive. Aliases are stored as their
/// own entries (with <see cref="UserDefinedFunction.AliasOf"/> set) and resolved on read.</para>
/// </summary>
public sealed class UserDefinedFunctionService : IUserDefinedFunctionService
{
	private static string Key(string name, DBRef? owner) => $"{owner?.ToString() ?? "global"}/{name.ToLowerInvariant()}";

	private readonly ConcurrentDictionary<string, UserDefinedFunction> _functions =
		new(StringComparer.OrdinalIgnoreCase);

	// Restriction overlay for BUILT-IN function names (and "deleted" built-ins): a built-in carries
	// no registry entry, so its @function/restrict restriction lives here and is consulted at call time.
	private readonly ConcurrentDictionary<string, string> _builtinRestrictions =
		new(StringComparer.OrdinalIgnoreCase);

	public void Define(UserDefinedFunction function)
	{
		var key = function.Name.ToLowerInvariant();
		_functions[Key(key, function.Owner)] = function with { Name = key, AliasOf = null };
	}

	public UserDefinedFunction? Get(string name, DBRef? owner = null)
		=> _functions.TryGetValue(Key(name, owner), out var fn) ? fn : null;

	public UserDefinedFunction? Resolve(string name, DBRef? owner = null)
	{
		if (!_functions.TryGetValue(Key(name, owner), out var entry))
		{
			return null;
		}

		// Follow at most one alias hop to the concrete definition.
		if (entry.AliasOf is not null)
		{
			if (!_functions.TryGetValue(Key(entry.AliasOf, owner), out var target) || target.AliasOf is not null)
			{
				return null;
			}

			// Present the resolved target's object/attribute/bounds under the requested name.
			entry = target with { Name = entry.Name, Enabled = entry.Enabled && (owner is null || target.Enabled) };
		}

		return entry.Enabled ? entry : null;
	}

	public bool Delete(string name, DBRef? owner = null)
	{
		if (!_functions.TryRemove(Key(name, owner), out _)) return false;
		if (owner is not null)
			foreach (var entry in _functions)
				if (entry.Value.Owner == owner && string.Equals(entry.Value.AliasOf, name, StringComparison.OrdinalIgnoreCase))
					_functions.TryRemove(entry);
		return true;
	}

	public bool SetEnabled(string name, bool enabled, DBRef? owner = null)
	{
		if (!_functions.TryGetValue(Key(name, owner), out var entry))
		{
			return false;
		}

		_functions[Key(entry.Name, owner)] = entry with { Enabled = enabled };
		return true;
	}

	public bool Alias(string alias, string target, DBRef? owner = null)
	{
		var targetKey = target.ToLowerInvariant();
		if (!_functions.TryGetValue(Key(targetKey, owner), out var targetEntry) || targetEntry.AliasOf is not null)
		{
			return false;
		}

		var aliasKey = alias.ToLowerInvariant();
		_functions[Key(aliasKey, owner)] = new UserDefinedFunction(
			Name: aliasKey,
			Object: targetEntry.Object,
			Attribute: targetEntry.Attribute,
			MinArgs: targetEntry.MinArgs,
			MaxArgs: targetEntry.MaxArgs,
			Enabled: true,
			AliasOf: targetKey,
			Owner: owner);
		return true;
	}

	public bool SetRestriction(string name, string? restriction, DBRef? owner = null)
	{
		if (!_functions.TryGetValue(Key(name, owner), out var entry))
		{
			return false;
		}

		var normalized = string.IsNullOrWhiteSpace(restriction) ? null : restriction.Trim();
		_functions[Key(entry.Name, owner)] = entry with { Restriction = normalized };
		return true;
	}

	public bool Clone(string newName, string existing, DBRef? owner = null)
	{
		// Clone the concrete (alias-resolved) target so the copy is independent of the source.
		var source = Resolve(existing, owner) ?? Get(existing, owner);
		if (source is null || source.AliasOf is not null)
		{
			return false;
		}

		var cloneKey = newName.ToLowerInvariant();
		_functions[Key(cloneKey, owner)] = source with
		{
			Name = cloneKey,
			AliasOf = null,
			Enabled = true,
			Restriction = null,
			Preserved = false
		};
		return true;
	}

	public bool SetPreserved(string name, bool preserved, DBRef? owner = null)
	{
		if (!_functions.TryGetValue(Key(name, owner), out var entry))
		{
			return false;
		}

		_functions[Key(entry.Name, owner)] = entry with { Preserved = preserved };
		return true;
	}

	public int ResetUnpreserved(DBRef? owner = null)
	{
		var removed = 0;
		foreach (var entry in _functions.Values.ToArray())
		{
			if (entry.Owner == owner && !entry.Preserved && _functions.TryRemove(Key(entry.Name, owner), out _))
			{
				removed++;
			}
		}

		return removed;
	}

	public void InvalidateLocalDefinitions(DBRef target)
	{
		var entries = _functions.ToArray();
		var invalidated = entries.Where(entry => entry.Value.Owner is { } owner &&
			(owner.Matches(target) || (entry.Value.AliasOf is null && entry.Value.Object.Matches(target))))
			.Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in entries)
		{
			if (invalidated.Contains(entry.Key) || (entry.Value.Owner is { } owner && entry.Value.AliasOf is { } alias && invalidated.Contains(Key(alias, owner))))
				_functions.TryRemove(entry);
		}
	}

	public void SetBuiltinRestriction(string name, string? restriction)
	{
		var key = name.ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(restriction))
		{
			_builtinRestrictions.TryRemove(key, out _);
			return;
		}

		_builtinRestrictions[key] = restriction.Trim();
	}

	public string? GetBuiltinRestriction(string name)
		=> _builtinRestrictions.TryGetValue(name, out var restriction) ? restriction : null;

	public IReadOnlyCollection<UserDefinedFunction> All(DBRef? owner = null) => _functions.Values.Where(entry => entry.Owner == owner).ToArray();
}
