namespace SharpMUSH.Database;

public static class DatabaseConstants
{
	public const string Objects = "node_objects";
	public const string ObjectData = "node_object_data";
	public const string ServerData = "node_server_data";
	public const string Players = "node_players";
	public const string Rooms = "node_rooms";
	public const string Things = "node_things";
	public const string Exits = "node_exits";
	public const string Attributes = "node_attributes";
	public const string Functions = "node_functions";
	public const string Commands = "node_commands";
	public const string ObjectFlags = "node_object_flags";
	public const string ObjectPowers = "node_object_powers";
	public const string AttributeEntries = "node_attribute_entries";
	public const string AttributeFlags = "node_attribute_flags";
	public const string Channels = "node_channels";
	public const string Mails = "node_mails";
	
	public static readonly string[] verticesContainer = [Rooms, Players, Things];
	public static readonly string[] verticesContent = [Players, Exits, Things];
	public static readonly string[] verticesAll = [Rooms, Players, Exits, Things];

	public const string IsObject = "edge_is_object";
	public const string AtLocation = "edge_at_location";
	public const string HasObjectData = "edge_has_object_data";
	public const string HasObjectOwner = "edge_has_object_owner";
	public const string HasParent = "edge_has_parent";
	public const string HasHome = "edge_has_home";
	public const string HasExit = "edge_has_exit";
	public const string HasFlags = "edge_has_flags";
	public const string HasPowers = "edge_has_powers";
	public const string HasAttribute = "edge_has_attribute";
	public const string HasAttributeFlag = "edge_has_attribute_flag";
	public const string HasHook = "edge_has_hook";
	public const string HasAttributeOwner = "edge_has_attribute_owner";
	public const string OwnerOfChannel = "edge_owner_of_channel";
	public const string OnChannel = "edge_has_channel";
	public const string SenderOfMail = "edge_mail_sender";
	public const string ReceivedMail = "edge_received_mail";

	/// <summary>
	/// Describes the relationship between actualized types and their objects.
	/// <see cref="verticesAll"/> -> <see cref="IsObject"/> -> <see cref="Objects"/>
	/// </summary>
	public const string GraphObjects = "graph_objects";
	/// <summary>
	/// Describes the relationship between objects and their expanded data.
	/// <see cref="Objects"/> -> <see cref="HasObjectData"/> -> <see cref="ObjectData"/>
	/// </summary>
	public const string GraphObjectData = "graph_object_data";
	/// <summary>
	/// Describes the relationship between objects and their Powers.
	/// <see cref="Objects"/> -> <see cref="HasPowers"/> -> <see cref="ObjectPowers"/>
	/// </summary>
	public const string GraphPowers = "graph_powers";
	/// <summary>
	/// Describes the relationship between objects and their flags.
	/// <see cref="Objects"/> -> <see cref="HasFlags"/> -> <see cref="ObjectFlags"/>
	/// </summary>
	public const string GraphFlags = "graph_flags";
	/// <summary>
	/// Describes the relationship between realized objects and their attributes, and attributes and their branches.
	/// TODO: This should be objects and attributes, but arangoDb gives trouble for some reason.
	/// <see cref="verticesAll"/> -> <see cref="HasAttribute"/> -> <see cref="Attributes"/>
	/// <see cref="Attributes"/> -> <see cref="HasAttribute"/> -> <see cref="Attributes"/>
	/// </summary>
	public const string GraphAttributes = "graph_attributes";
	/// <summary>
	/// Describes the relationship between attributes and their flags.
	/// <see cref="Attributes"/> -> <see cref="HasAttributeFlag"/> -> <see cref="AttributeFlags"/>
	/// </summary>
	public const string GraphAttributeFlags = "graph_attribute_flags";
	/// <summary>
	/// Describes the relationship between contents and their container locations.
	/// <see cref="verticesContent"/> -> <see cref="AtLocation"/> -> <see cref="verticesContainer"/>
	/// </summary>
	public const string GraphLocations = "graph_locations";
	/// <summary>
	/// Describes the relationship between container objects and their exits.
	/// <see cref="verticesContainer"/> -> <see cref="HasExit"/> -> <see cref="Exits"/>
	/// </summary>
	public const string GraphExits = "graph_exits";
	/// <summary>
	/// Describes the relationship between contents and their container objects.
	/// <see cref="verticesContent"/> -> <see cref="HasHome"/> -> <see cref="verticesContainer"/>
	/// </summary>
	public const string GraphHomes = "graph_homes";
	/// <summary>
	/// Describes the relationship between objects and their parents.
	/// <see cref="Objects"/> -> <see cref="HasParent"/> -> <see cref="Objects"/>
	/// </summary>
	public const string GraphParents = "graph_parents";
	/// <summary>
	/// Describes the relationship between objects and their player owners.
	/// <see cref="Objects"/> -> <see cref="HasObjectOwner"/> -> <see cref="Players"/>
	/// </summary>
	public const string GraphObjectOwners = "graph_object_owners";
	/// <summary>
	/// Describes the relationship between attributes and their player owners.
	/// <see cref="Attributes"/> -> <see cref="HasAttributeOwner"/> -> <see cref="Players"/>.
	/// </summary>
	public const string GraphAttributeOwners = "graph_attribute_owners";
	/// <summary>
	/// Describes the relationship between players and channels.
	/// <see cref="Channels"/> -> <see cref="OwnerOfChannel"/> -> <see cref="Objects"/>
	/// <see cref="Objects"/> -> <see cref="OnChannel"/> -> <see cref="Channels"/>
	/// </summary>
	public const string GraphChannels = "graph_channels";
	/// <summary>
	/// Describes the relationship between players and their mails.
	/// <see cref="Players"/> -> <see cref="ReceivedMail"/> -> <see cref="Mails"/>
	/// <see cref="Mails"/> -> <see cref="SenderOfMail"/> -> <see cref="Objects"/>
	/// </summary>
	public const string GraphMail = "graph_mail";

	public const string TypeObject = "object";
	public const string TypeString = "string";
	public const string TypeNumber = "number";
	public const string TypeArray = "array";
	public const string TypeBoolean = "boolean";

	public const string TypeRoom = "ROOM";
	public const string TypePlayer = "PLAYER";
	public const string TypeExit = "EXIT";
	public const string TypeThing = "THING";

	public static readonly string[] typesRoom = [TypeRoom];
	public static readonly string[] typesPlayer = [TypePlayer];
	public static readonly string[] typesExit = [TypeExit];
	public static readonly string[] typesThing = [TypeThing];

	public static readonly string[] typesContainer = [TypeRoom, TypePlayer, TypeThing];
	public static readonly string[] typesContent = [TypePlayer, TypeExit, TypeThing];
	public static readonly string[] typesAll = [TypeRoom, TypePlayer, TypeExit, TypeThing];

	public static readonly string[] permissionsWizard = ["wizard"];
	public static readonly string[] permissionsRoyalty = ["royalty"];
	public static readonly string[] permissionsTrusted = ["trusted"];
	public static readonly string[] permissionsLog = ["log"];
	public static readonly string[] permissionsODark = ["odark"];
	public static readonly string[] permissionsMDark = ["mdark"];
}
