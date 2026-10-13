using System.Collections.Frozen;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database;

/// <summary>
/// The synthetic flag every object carries for its own type (PLAYER, ROOM, THING, EXIT), so
/// <c>hasflag(obj, PLAYER)</c> and the type-restricted flag checks work without a stored flag.
/// Appended by the provider after the object's stored flags.
/// </summary>
public static class ObjectTypeFlag
{
	private static readonly FrozenDictionary<string, SharpObjectFlag> Known =
		new[] { "PLAYER", "ROOM", "THING", "EXIT" }.ToFrozenDictionary(type => type, Create, StringComparer.OrdinalIgnoreCase);

	/// <summary>The flag for <paramref name="type"/>, in capitals; one shared instance per object type.</summary>
	public static SharpObjectFlag For(string type)
		=> Known.TryGetValue(type, out var flag) ? flag : Create(type.ToUpperInvariant());

	private static SharpObjectFlag Create(string type) => new()
	{
		Name = type,
		SetPermissions = [],
		TypeRestrictions = [],
		Symbol = type[0].ToString(),
		System = true,
		UnsetPermissions = [],
		Id = null,
		Aliases = []
	};
}
