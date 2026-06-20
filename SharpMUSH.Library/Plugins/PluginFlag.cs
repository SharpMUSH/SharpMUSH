namespace SharpMUSH.Library.Plugins;

/// <summary>
/// A single engine object-flag contributed by a plugin. Mirrors the shape of the built-in flags seeded
/// during database migration (name, symbol, aliases, set/unset permission sets, type restrictions,
/// system marker). Each active database provider appends these to its seeded flag set so the flag exists
/// after migration regardless of backend.
/// </summary>
/// <param name="Name">Flag name (upper-cased by convention, matching the built-ins).</param>
/// <param name="Symbol">Single-character display symbol, or empty for none.</param>
/// <param name="Aliases">Alternate names that resolve to this flag.</param>
/// <param name="SetPermissions">Permission tokens required to set the flag (e.g. "wizard", "royalty").</param>
/// <param name="UnsetPermissions">Permission tokens required to unset the flag.</param>
/// <param name="TypeRestrictions">Object types the flag may apply to (e.g. "ROOM", "PLAYER", "EXIT", "THING").</param>
/// <param name="System">Whether this is a system flag (compiled C#, like the built-ins). Defaults to true.</param>
public sealed record PluginFlag(
	string Name,
	string Symbol,
	IReadOnlyList<string> Aliases,
	IReadOnlyList<string> SetPermissions,
	IReadOnlyList<string> UnsetPermissions,
	IReadOnlyList<string> TypeRestrictions,
	bool System = true);
