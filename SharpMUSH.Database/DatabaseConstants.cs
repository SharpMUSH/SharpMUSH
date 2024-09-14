namespace SharpMUSH.Database;

public static class DatabaseConstants
{
	public const string objects = "node_objects";
	public const string players = "node_players";
	public const string rooms = "node_rooms";
	public const string things = "node_things";
	public const string exits = "node_exits";
	public const string attributes = "node_attributes";
	public const string functions = "node_functions";
	public const string commands = "node_commands";
	public const string objectFlags = "node_object_flags";
	public const string objectPowers = "node_object_powers";
	public const string attributeEntries = "node_attribute_entries";

	public const string isObject = "edge_is_object";
	public const string atLocation = "edge_at_location";
	public const string hasObjectOwner = "edge_has_object_owner";
	public const string hasParent = "edge_has_parent";
	public const string hasHome = "edge_has_home";
	public const string hasExit = "edge_has_exit";
	public const string hasFlags = "edge_has_flags";
	public const string hasPowers = "edge_has_powers";
	public const string hasAttribute = "edge_has_attribute";
	public const string hasHook = "edge_has_hook";
	public const string hasAttributeOwner = "edge_has_attribute_owner";

	public const string graphObjects = "graph_objects";
	public const string graphPowers = "graph_powers";
	public const string graphFlags = "graph_flags";
	public const string graphAttributes = "graph_attributes";
	public const string graphLocations = "graph_locations";
	public const string graphExits = "graph_exits";
	public const string graphHomes = "graph_homes";
	public const string graphParents = "graph_parents";
	public const string graphObjectOwners = "graph_object_owners";
	public const string graphAttributeOwners = "graph_attribute_owners";

	public const string typeObject = "object";
	public const string typeString = "string";
	public const string typeNumber = "number";
	public const string typeArray = "array";

	public const string typeRoom = "ROOM";
	public const string typePlayer = "PLAYER";
	public const string typeExit = "EXIT";
	public const string typeThing = "THING";

	public static readonly string[] typesRoom = [typeRoom];
	public static readonly string[] typesPlayer = [typePlayer];
	public static readonly string[] typesExit = [typeExit];
	public static readonly string[] typesThing = [typeThing];

	public static readonly string[] typesContainer = [typeRoom, typePlayer, typeThing];
	public static readonly string[] typesContent = [typePlayer, typeExit, typeThing];
	public static readonly string[] typesAll = [typeRoom, typePlayer, typeExit, typeThing];

	public static readonly string[] permissionsWizard = ["wizard"];
	public static readonly string[] permissionsRoyalty = ["wizard"];
	public static readonly string[] permissionsTrusted = ["trusted"];
	public static readonly string[] permissionsLog = ["log"];
}
