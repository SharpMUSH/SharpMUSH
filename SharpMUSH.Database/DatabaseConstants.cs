
namespace SharpMUSH.Database
{
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
		internal const string attributeEntries = "node_attribute_entries";

		internal const string isObject = "edge_is_object";
		internal const string atLocation = "edge_at_location";
		internal const string hasObjectOwner = "edge_has_object_owner";
		internal const string hasHome = "edge_has_home";
		internal const string hasFlags = "edge_has_flags";
		internal const string hasAttribute = "edge_has_attribute";
		internal const string hasHook = "edge_has_hook";
		internal const string hasAttributeOwner = "edge_has_attribute_owner";

		internal const string graphObjects = "graph_objects";
		internal const string graphAttributes = "graph_attributes";

		internal const string typeObject = "object";
		internal const string typeString = "string";
		internal const string typeNumber = "number";
		internal const string typeArray = "array";
	}
}
