namespace SharpMUSH.Library.Definitions;

public static class CacheTags
{
	public const string ObjectContents = "object-contents";
	/// <summary>
	/// Every cached attribute read, of every object. Only a write that cannot name the objects it
	/// touches uses it — a bulk owner reassignment. Ordinary attribute writes name one object and use
	/// <see cref="CacheKeys.AttributesTag"/>; they used to use this, which made every <c>&amp;ATTR</c>
	/// anywhere drop every object's cached attributes.
	/// </summary>
	public const string AllObjectAttributes = "object-attributes";

	/// <summary>
	/// The attribute reads that walk parent and zone chains. Game-wide on purpose: a write to any
	/// object in a chain changes what every object below it sees, and the chain a read walked is not
	/// recoverable from its result. See <see cref="CacheKeys.AttributesTag"/> for the per-object tag
	/// the single-object attribute reads carry instead.
	/// </summary>
	public const string InheritedAttributes = "attribute-inheritance";
	public const string ObjectOwnership = "object-ownership";
	public const string ExitList = "exit-list";
	public const string ThingList = "thing-list";
	public const string RoomList = "room-list";
	public const string PlayerList = "player-list";
	public const string PlayerNames = "player-names";
	public const string ObjectList = "object-list";
	public const string ObjectLocks = "object-locks";
	public const string ChannelList = "channel-list";
	public const string FlagList = "flag-list";
	public const string PowerList = "power-list";
	public const string ConnectionLogs = "connection-logs";
	public const string ZoneObjects = "zone-objects";
	public const string AttributeEntry = "attribute-entry";
}