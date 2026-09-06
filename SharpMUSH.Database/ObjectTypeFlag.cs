using SharpMUSH.Library.Models;

namespace SharpMUSH.Database;

/// <summary>
/// The synthetic flag every object carries for its own type (PLAYER, ROOM, THING, EXIT), so
/// <c>hasflag(obj, PLAYER)</c> and the type-restricted flag checks work without a stored flag.
/// Appended by every provider after the object's stored flags.
/// </summary>
public static class ObjectTypeFlag
{
	public static SharpObjectFlag For(string type) => new()
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
