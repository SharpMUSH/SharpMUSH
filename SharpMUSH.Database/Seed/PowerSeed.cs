namespace SharpMUSH.Database.Seed;

/// <summary>
/// The built-in powers, shared verbatim across all database providers. Copied from
/// <c>SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs</c> (<c>CreateInitialPowers</c>).
/// </summary>
public static class PowerSeed
{
	public static readonly (string Name, string Alias, string[] SetPerms, string[] UnsetPerms)[] Powers =
	[
		("Announce", "", ["wizard","log"], ["wizard"]),
		("Boot", "", ["wizard","log"], ["wizard"]),
		("Builder", "", ["wizard","log"], ["wizard"]),
		("Can_Dark", "", ["wizard","log"], []),
		("Can_HTTP", "", ["wizard","log"], []),
		("Can_Spoof", "", ["wizard","log"], ["wizard"]),
		("Chat_Privs", "", ["wizard","log"], ["wizard"]),
		("Debit", "", ["wizard","log"], []),
		("Functions", "", ["wizard","log"], ["wizard"]),
		("Guest", "", ["wizard","log"], ["wizard"]),
		("Halt", "", ["wizard","log"], ["wizard"]),
		("Hide", "", ["wizard","log"], ["wizard"]),
		("Hook", "", ["wizard","log"], []),
		("Idle", "", ["wizard","log"], ["wizard"]),
		("Immortal", "", ["wizard","log"], ["wizard"]),
		("Link_Anywhere", "", ["wizard","log"], ["wizard"]),
		("Login", "", ["wizard","log"], ["wizard"]),
		("Long_Fingers", "", ["wizard","log"], ["wizard"]),
		("Many_Attribs", "", ["wizard","log"], []),
		("No_Pay", "", ["wizard","log"], ["wizard"]),
		("No_Quota", "", ["wizard","log"], ["wizard"]),
		("Open_Anywhere", "", ["wizard","log"], ["wizard"]),
		("Pemit_All", "", ["wizard","log"], ["wizard"]),
		("Pick_DBRefs", "", ["wizard","log"], ["wizard"]),
		("Player_Create", "", ["wizard","log"], ["wizard"]),
		("Poll", "", ["wizard","log"], ["wizard"]),
		("Pueblo_Send", "", ["wizard","log"], ["wizard"]),
		("Queue", "", ["wizard","log"], ["wizard"]),
		("Search", "", ["wizard","log"], ["wizard"]),
		("See_All", "", ["wizard","log"], ["wizard"]),
		("See_Queue", "", ["wizard","log"], ["wizard"]),
		("See_OOB", "", ["wizard","log"], ["wizard"]),
		("SQL_OK", "", ["wizard","log"], ["wizard"]),
		("Tport_Anything", "", ["wizard","log"], ["wizard"]),
		("Tport_Anywhere", "", ["wizard","log"], ["wizard"]),
		("Unkillable", "", ["wizard","log"], ["wizard"]),
	];
}
