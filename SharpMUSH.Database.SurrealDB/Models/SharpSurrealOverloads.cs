using SharpMUSH.Database.Models;

namespace SharpMUSH.Database.SurrealDB.Models;

public record SharpSurrealObjectCreateRequest(int? Id, string Name, string Type, Dictionary<string, string> Locks, long CreationTime, long ModifiedTime)
	: SharpObjectCreateRequest(Name, Type, Locks, CreationTime, ModifiedTime);

public record SharpSurrealPlayerCreateRequest(int? Id, string[]? Aliases, string PasswordHash)
	: SharpPlayerCreateRequest(Aliases, PasswordHash);

public record SharpSurrealExitCreateRequest(int? Id, string[]? Aliases)
	: SharpExitCreateRequest(Aliases);

public record SharpSurrealThingCreateRequest(int? Id, string[]? Aliases)
	: SharpThingCreateRequest(Aliases);

public record SharpSurrealRoomCreateRequest(int? Id)
	: SharpRoomCreateRequest();

public record SharpSurrealObjectFlagCreationRequest(int? Id, string Name, string[]? Aliases, string Symbol, bool System, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions)
	: SharpObjectFlagCreationRequest(Name, Aliases, Symbol, System, SetPermissions, UnsetPermissions, TypeRestrictions);

public record SharpSurrealPowerCreateRequest(int? Id, string Name, string Alias, bool System, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions)
	: SharpPowerCreateRequest(Name, Alias, System, SetPermissions, UnsetPermissions, TypeRestrictions);

public record SharpSurrealAttributeEntryCreateRequest(int? Id, string Key, string Name, string[] DefaultFlags, string? Limit, string[]? Enum)
	: SharpAttributeEntryCreateRequest(Key, Name, DefaultFlags, Limit, Enum);

public record SharpSurrealCommandCreateRequest(int? id, string Name, string? Alias, bool Enabled, string? RestrictedErrorMessage, string[] Traits, string[] Restrictions)
	: SharpCommandCreateRequest(Name, Alias, Enabled, RestrictedErrorMessage, Traits, Restrictions);