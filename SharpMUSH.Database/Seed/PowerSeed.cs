namespace SharpMUSH.Database.Seed;

/// <summary>
/// The built-in powers a new world is seeded with.
/// </summary>
public static class PowerSeed
{
	/// <remarks>
	/// The aliases are PennMUSH's power alias table — the extra rows a 1.8.8 dump writes for a power — so
	/// softcode that names a power the PennMUSH way (<c>haspower(%#, tel_anywhere)</c>) finds it here too.
	/// Each name is spelled as PennMUSH shows it (<c>@list powers</c>, <c>powers()</c>, examine): the
	/// <c>power_table</c> ones (hdrs/flag_tab.h) in their mixed case, the ones <c>src/flags.c</c> adds at
	/// startup with <c>add_power</c>, which upper-cases, in capitals. Powers are keyed by the upper-cased
	/// name, so a world seeded under the older spelling keeps its grants and only the display changes.
	/// </remarks>
	public static readonly (string Name, string[] Aliases, string[] SetPerms, string[] UnsetPerms)[] Powers =
	[
		("Announce", ["@wall", "wall"], ["wizard","log"], ["wizard"]),
		("Boot", [], ["wizard","log"], ["wizard"]),
		("Builder", [], ["wizard","log"], ["wizard"]),
		("CAN_DARK", [], ["wizard","log"], []),
		("CAN_HTTP", [], ["wizard","log"], []),
		("Can_spoof", ["Can_nspemit"], ["wizard","log"], ["wizard"]),
		("Chat_Privs", [], ["wizard","log"], ["wizard"]),
		("DEBIT", ["steal_money"], ["wizard","log"], []),
		("Functions", [], ["wizard","log"], ["wizard"]),
		("Guest", [], ["wizard","log"], ["wizard"]),
		("Halt", [], ["wizard","log"], ["wizard"]),
		("Hide", [], ["wizard","log"], ["wizard"]),
		("HOOK", [], ["wizard","log"], []),
		("Idle", [], ["wizard","log"], ["wizard"]),
		("Immortal", [], ["wizard","log"], ["wizard"]),
		("Link_Anywhere", [], ["wizard","log"], ["wizard"]),
		("Login", [], ["wizard","log"], ["wizard"]),
		("Long_Fingers", [], ["wizard","log"], ["wizard"]),
		("MANY_ATTRIBS", [], ["wizard","log"], []),
		("No_Pay", [], ["wizard","log"], ["wizard"]),
		("No_Quota", ["free_quota"], ["wizard","log"], ["wizard"]),
		("Open_Anywhere", [], ["wizard","log"], ["wizard"]),
		("Pemit_All", [], ["wizard","log"], ["wizard"]),
		("PICK_DBREFS", [], ["wizard","log"], ["wizard"]),
		("Player_Create", [], ["wizard","log"], ["wizard"]),
		("Poll", [], ["wizard","log"], ["wizard"]),
		("Queue", [], ["wizard","log"], ["wizard"]),
		// PennMUSH hdrs/flag_tab.h:161 - {"Quotas", '\0', NOTYPE, CHANGE_QUOTAS, F_WIZARD | F_LOG, F_WIZARD}.
		// The macro name predates the power: do_quota (src/wiz.c:175) still restricts *setting* a quota to
		// wizards, and what the power buys is the read of another player's quota (wiz.c:179).
		("Quotas", [], ["wizard","log"], ["wizard"]),
		("Search", [], ["wizard","log"], ["wizard"]),
		("See_All", [], ["wizard","log"], ["wizard"]),
		("See_Queue", [], ["wizard","log"], ["wizard"]),
		("See_OOB", [], ["wizard","log"], ["wizard"]),
		// PennMUSH renamed Pueblo_Send to Send_OOB "to reflect its new use for other,
		// non-Pueblo-related, out of band messages. Pueblo_Send remains as an alias."
		// (game/txt/hlp/pennv186.hlp:86; the rename is applied at load in src/flags.c:850-855.)
		// SharpMUSH's own: pictures without the rest of Send_OOB. The approved role holds it.
		("Send_Image", [], ["wizard","log"], ["wizard"]),
		("Send_OOB", ["Pueblo_Send"], ["wizard","log"], ["wizard"]),
		("SQL_OK", ["Use_SQL"], ["wizard","log"], ["wizard"]),
		("Tport_Anything", ["tel_anything"], ["wizard","log"], ["wizard"]),
		("Tport_Anywhere", ["tel_anywhere"], ["wizard","log"], ["wizard"]),
		("Unkillable", [], ["wizard","log"], ["wizard"]),
	];
}
