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

	public IReadOnlyCollection<UserDefinedFunction> All() => _functions.Values.ToArray();
}
