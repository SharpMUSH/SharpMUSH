namespace SharpMUSH.Plugins.Discord.Roles;

/// <summary>A server member as the bot sees them: their id, name, and the roles they hold there.</summary>
public sealed record DiscordMember(ulong UserId, string Name, IReadOnlySet<ulong> Roles);

/// <summary>A role the bot gives to or takes from a member.</summary>
public sealed record DiscordRoleChange(ulong UserId, ulong RoleId, bool Give);

/// <summary>A mapped role held against the game's answer.</summary>
/// <param name="Added">The member holds the role and should not; false when they lack it and should hold it.</param>
public sealed record DiscordDriftSeen(ulong RoleId, ulong UserId, string Name, bool Added);

/// <summary>What one look at a member leads to: roles to change, and drift to record.</summary>
public sealed record DiscordRoleDecision(IReadOnlyList<DiscordRoleChange> Changes, IReadOnlyList<DiscordDriftSeen> Drift)
{
	public static readonly DiscordRoleDecision Nothing = new([], []);
}

/// <summary>
/// The game's answer for each mapped role, set against what a member holds on Discord. The game is the source of truth:
/// a linked member whose account is active should hold a mapped role exactly when the account, or one of its
/// characters, holds the game role; anyone else should not hold it. When the two differ, what the bot does depends on
/// why it is looking.
/// </summary>
public static class DiscordRolePlan
{
	/// <summary>Whether the game says the member holds the mapping's role.</summary>
	/// <param name="held">The game roles the member's linked, active account holds; null when it is not linked or not active.</param>
	public static bool Wants(DiscordRoleMapping mapping, IReadOnlySet<string>? held)
		=> held?.Contains(mapping.Slug) == true;

	/// <summary>
	/// Something changed for this member in the game (a role, their link, their standing) or they joined the server: the
	/// game's answer is applied to every mapped role, settling any drift on them.
	/// </summary>
	public static DiscordRoleDecision Changed(DiscordMember member, IEnumerable<DiscordRoleMapping> mappings,
		IReadOnlySet<string>? held)
		=> new([.. Mismatches(member, mappings, held).Select(mapping => Change(member, mapping, held))], []);

	/// <summary>
	/// A pass over every member, to repair what was missed while the bot was away. A linked member gets the game's
	/// answer, except on a role whose drift staff have not settled yet. A member who is not linked and holds a mapped
	/// role was given it by hand: drift, and an enforced role is taken back.
	/// </summary>
	/// <param name="drifted">Whether the member's hold on a role (by its id) has open drift.</param>
	public static DiscordRoleDecision Pass(DiscordMember member, IEnumerable<DiscordRoleMapping> mappings,
		IReadOnlySet<string>? held, bool linked, Func<ulong, bool> drifted)
	{
		List<DiscordRoleChange> changes = [];
		List<DiscordDriftSeen> drift = [];
		foreach (var mapping in Mismatches(member, mappings, held).Where(mapping => !drifted(mapping.RoleId)))
		{
			if (linked || mapping.Mode == DiscordRoleMode.Enforce)
			{
				changes.Add(Change(member, mapping, held));
			}

			if (!linked)
			{
				drift.Add(Drift(member, mapping));
			}
		}

		return new DiscordRoleDecision(changes, drift);
	}

	/// <summary>
	/// Discord says the member's roles changed. Each mapped role now held against the game's answer was changed by hand:
	/// drift, and an enforced role is put back. The bot's own changes agree with the game, so they are never drift.
	/// </summary>
	public static DiscordRoleDecision Observed(DiscordMember member, IEnumerable<DiscordRoleMapping> mappings,
		IReadOnlySet<string>? held)
	{
		var mismatched = Mismatches(member, mappings, held).ToList();
		return new DiscordRoleDecision(
			[.. mismatched.Where(mapping => mapping.Mode == DiscordRoleMode.Enforce).Select(mapping => Change(member, mapping, held))],
			[.. mismatched.Select(mapping => Drift(member, mapping))]);
	}

	private static IEnumerable<DiscordRoleMapping> Mismatches(DiscordMember member, IEnumerable<DiscordRoleMapping> mappings,
		IReadOnlySet<string>? held)
		=> mappings.Where(mapping => member.Roles.Contains(mapping.RoleId) != Wants(mapping, held));

	private static DiscordRoleChange Change(DiscordMember member, DiscordRoleMapping mapping, IReadOnlySet<string>? held)
		=> new(member.UserId, mapping.RoleId, Wants(mapping, held));

	private static DiscordDriftSeen Drift(DiscordMember member, DiscordRoleMapping mapping)
		=> new(mapping.RoleId, member.UserId, member.Name, member.Roles.Contains(mapping.RoleId));
}
