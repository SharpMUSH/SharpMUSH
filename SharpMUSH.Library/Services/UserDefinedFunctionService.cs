using System.Collections.Concurrent;
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
	private readonly ConcurrentDictionary<string, UserDefinedFunction> _functions =
		new(StringComparer.OrdinalIgnoreCase);

	// Restriction overlay for BUILT-IN function names (and "deleted" built-ins): a built-in carries
	// no registry entry, so its @function/restrict restriction lives here and is consulted at call time.
	private readonly ConcurrentDictionary<string, string> _builtinRestrictions =
		new(StringComparer.OrdinalIgnoreCase);

	public void Define(UserDefinedFunction function)
	{
		var key = function.Name.ToLowerInvariant();
		_functions[key] = function with { Name = key, AliasOf = null };
	}

	public UserDefinedFunction? Get(string name)
		=> _functions.TryGetValue(name, out var fn) ? fn : null;

	public UserDefinedFunction? Resolve(string name)
	{
		if (!_functions.TryGetValue(name, out var entry))
		{
			return null;
		}

		// Follow at most one alias hop to the concrete definition.
		if (entry.AliasOf is not null)
		{
			if (!_functions.TryGetValue(entry.AliasOf, out var target) || target.AliasOf is not null)
			{
				return null;
			}

			// Present the resolved target's object/attribute/bounds under the requested name.
			entry = target with { Name = entry.Name, Enabled = entry.Enabled };
		}

		return entry.Enabled ? entry : null;
	}

	public bool Delete(string name) => _functions.TryRemove(name, out _);

	public bool SetEnabled(string name, bool enabled)
	{
		if (!_functions.TryGetValue(name, out var entry))
		{
			return false;
		}

		_functions[entry.Name] = entry with { Enabled = enabled };
		return true;
	}

	public bool Alias(string alias, string target)
	{
		var targetKey = target.ToLowerInvariant();
		if (!_functions.TryGetValue(targetKey, out var targetEntry) || targetEntry.AliasOf is not null)
		{
			return false;
		}

		var aliasKey = alias.ToLowerInvariant();
		_functions[aliasKey] = new UserDefinedFunction(
			Name: aliasKey,
			Object: targetEntry.Object,
			Attribute: targetEntry.Attribute,
			MinArgs: targetEntry.MinArgs,
			MaxArgs: targetEntry.MaxArgs,
			Enabled: true,
			AliasOf: targetKey);
		return true;
	}

	public bool SetRestriction(string name, string? restriction)
	{
		if (!_functions.TryGetValue(name, out var entry))
		{
			return false;
		}

		var normalized = string.IsNullOrWhiteSpace(restriction) ? null : restriction.Trim();
		_functions[entry.Name] = entry with { Restriction = normalized };
		return true;
	}

	public bool Clone(string newName, string existing)
	{
		// Clone the concrete (alias-resolved) target so the copy is independent of the source.
		var source = Resolve(existing) ?? Get(existing);
		if (source is null || source.AliasOf is not null)
		{
			return false;
		}

		var cloneKey = newName.ToLowerInvariant();
		_functions[cloneKey] = source with
		{
			Name = cloneKey,
			AliasOf = null,
			Enabled = true,
			Restriction = null,
			Preserved = false
		};
		return true;
	}

	public bool SetPreserved(string name, bool preserved)
	{
		if (!_functions.TryGetValue(name, out var entry))
		{
			return false;
		}

		_functions[entry.Name] = entry with { Preserved = preserved };
		return true;
	}

	public int ResetUnpreserved()
	{
		var removed = 0;
		foreach (var entry in _functions.Values.ToArray())
		{
			if (!entry.Preserved && _functions.TryRemove(entry.Name, out _))
			{
				removed++;
			}
		}

		return removed;
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

	public IReadOnlyCollection<UserDefinedFunction> All() => _functions.Values.ToArray();
}
