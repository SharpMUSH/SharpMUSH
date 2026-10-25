namespace SharpMUSH.Configuration;

/// <summary>Which server wrote a configuration file: PennMUSH's <c>mush.cnf</c>, TinyMUX's <c>netmux.conf</c> or RhostMUSH's <c>netrhost.conf</c>.</summary>
public enum ConfigurationDialect
{
	PennMUSH,
	TinyMUX,
	RhostMUSH
}

/// <summary>
/// The TinyMUX and RhostMUSH directives that name a SharpMUSH option under another name. Both servers come from
/// TinyMUSH and share most names; a directive they share with PennMUSH under the same name means the same there,
/// and one listed in neither place is reported as one SharpMUSH has no option for.
/// </summary>
public static class ConfigurationDirectives
{
	private static readonly Dictionary<string, string> TinyMush = new(StringComparer.OrdinalIgnoreCase)
	{
		["player_starting_room"] = "player_start",
		["money_name_singular"] = "money_singular",
		["money_name_plural"] = "money_plural",
		["dig_cost"] = "room_cost",
		["open_cost"] = "exit_cost",
		["create_min_cost"] = "object_cost",
		["wait_cost"] = "queue_cost",
		["quotas"] = "use_quota",
		["max_players"] = "max_logins",
		["room_parent"] = "ancestor_room",
		["exit_parent"] = "ancestor_exit",
		["thing_parent"] = "ancestor_thing",
		["player_parent"] = "ancestor_player",
		["examine_public_attrs"] = "ex_public_attribs",
		["ip_address"] = "ip_addr",
		["conn_timeout"] = "unconnected_idle_timeout"
	};

	private static readonly Dictionary<string, string> TinyMux = new(TinyMush, StringComparer.OrdinalIgnoreCase)
	{
		["parent_recursion_limit"] = "max_parents",
		["sql_server"] = "sql_host",
		["sql_user"] = "sql_username",
		["port_ssl"] = "ssl_port",
		["ssl_certificate_key"] = "ssl_private_key_file"
	};

	/// <summary>The PennMUSH name <paramref name="directive"/> has in a <paramref name="dialect"/> file, or the name itself.</summary>
	public static string PennName(string directive, ConfigurationDialect dialect)
		=> dialect switch
		{
			ConfigurationDialect.TinyMUX => TinyMux.GetValueOrDefault(directive, directive),
			ConfigurationDialect.RhostMUSH => TinyMush.GetValueOrDefault(directive, directive),
			_ => directive
		};
}
