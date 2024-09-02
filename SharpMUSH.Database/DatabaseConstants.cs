namespace SharpMUSH.Database;

internal static class DatabaseConstants
{
	internal const string objects = "node_objects";
	internal const string players = "node_players";
	internal const string rooms = "node_rooms";
	internal const string things = "node_things";
	internal const string exits = "node_exits";
	internal const string attributes = "node_attributes";
	internal const string functions = "node_functions";
	internal const string commands = "node_commands";
	internal const string objectFlags = "node_object_flags";
	internal const string objectPowers = "node_object_powers";
	internal const string attributeEntries = "node_attribute_entries";

	internal const string isObject = "edge_is_object";
	internal const string atLocation = "edge_at_location";
	internal const string hasObjectOwner = "edge_has_object_owner";
	internal const string hasParent = "edge_has_parent";
	internal const string hasHome = "edge_has_home";
	internal const string hasExit = "edge_has_exit";
	internal const string hasFlags = "edge_has_flags";
	internal const string hasPowers = "edge_has_powers";
	internal const string hasAttribute = "edge_has_attribute";
	internal const string hasHook = "edge_has_hook";
	internal const string hasAttributeOwner = "edge_has_attribute_owner";

	internal const string graphObjects = "graph_objects";
	internal const string graphPowers = "graph_powers";
	internal const string graphFlags = "graph_flags";
	internal const string graphAttributes = "graph_attributes";
	internal const string graphLocations = "graph_locations";
	internal const string graphExits = "graph_exits";
	internal const string graphHomes = "graph_homes";
	internal const string graphParents = "graph_parents";
	internal const string graphObjectOwners = "graph_object_owners";
	internal const string graphAttributeOwners = "graph_attribute_owners";

	internal const string typeObject = "object";
	internal const string typeString = "string";
	internal const string typeNumber = "number";
	internal const string typeArray = "array";

	internal const string typeRoom = "ROOM";
	internal const string typePlayer = "PLAYER";
	internal const string typeExit = "EXIT";
	internal const string typeThing = "THING";
}
