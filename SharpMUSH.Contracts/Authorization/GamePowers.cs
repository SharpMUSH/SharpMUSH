namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The PennMUSH powers, each backed by one permission scope (<c>game.see_all</c> for See_All). An
/// object has a power when it holds the scope, through a role or an override, so <c>@power</c> and
/// <c>haspower()</c> read and write roles rather than a separate store. A power created later with
/// <c>@power/add</c> is not in this list and is still stored on the object.
/// </summary>
/// <remarks>
/// Names and aliases match <c>PowerSeed</c> (a test keeps them equal); the seed carries the definitions
/// a game sees in <c>@list powers</c>, this list what each one means.
/// </remarks>
public static class GamePowers
{
	/// <summary>One power, its PennMUSH aliases, and the role that stands for it when there is one.</summary>
	/// <param name="Name">The power's name as PennMUSH spells it.</param>
	/// <param name="Aliases">Other names <c>haspower()</c> and <c>@power</c> accept.</param>
	/// <param name="Role">
	/// The role <c>@power</c> assigns instead of an override, for the two powers that are a kind of
	/// character rather than one ability: Guest and Builder.
	/// </param>
	/// <param name="Description">What holding it allows, in English (also the role editor's text).</param>
	public sealed record Power(string Name, string[] Aliases, string? Role, string Description)
	{
		/// <summary>The scope that backs the power.</summary>
		public string Scope => PortalPermission.GamePower(Name);

		/// <summary>The resource key stem for the role editor's label and description.</summary>
		public string ResourceKey => "EnumPermPower" + string.Concat(Name.Split('_')
			.Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

		public bool AnswersTo(string name)
			=> string.Equals(Name, name, StringComparison.OrdinalIgnoreCase)
				|| Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase));
	}

	public static readonly IReadOnlyList<Power> All =
	[
		new("Announce", ["@wall", "wall"], null, "Can broadcast to every connected player with @wall."),
		new("Boot", [], null, "Can disconnect other players with @boot."),
		new("Builder", [], BuiltInRoles.BuilderSlug, "Can build when building is restricted to builders."),
		new("CAN_DARK", [], null, "Can go DARK and drop off the WHO list."),
		new("CAN_HTTP", [], null, "Can make outgoing HTTP requests."),
		new("Can_spoof", ["Can_nspemit"], null, "Can use @nspemit and the other emits that skip the nospoof tag."),
		new("Chat_Privs", [], null, "Can create and administer channels."),
		new("DEBIT", ["steal_money"], null, "Can take money from other players with give."),
		new("Functions", [], null, "Can add and remove global functions with @function."),
		new("Guest", [], BuiltInRoles.GuestSlug, "Marks a guest character, barred from building and from changing settings."),
		new("Halt", [], null, "Can @halt any object's queue."),
		new("Hide", [], null, "Can hide from the WHO list."),
		new("HOOK", [], null, "Can attach command hooks with @hook."),
		new("Idle", [], null, "Is never disconnected for idling."),
		new("Immortal", [], null, "Needs no money or quota and cannot be killed."),
		new("Link_Anywhere", [], null, "Can @link to any room."),
		new("Login", [], null, "Can connect while logins are disabled."),
		new("Long_Fingers", [], null, "Can get, look at and use objects from a distance."),
		new("MANY_ATTRIBS", [], null, "Can set more attributes than the per-object limit."),
		new("No_Pay", [], null, "Never pays for commands."),
		new("No_Quota", ["free_quota"], null, "Has unlimited building quota."),
		new("Open_Anywhere", [], null, "Can @open exits from any room."),
		new("Pemit_All", [], null, "Can @pemit to players who are HAVEN or page-locked."),
		new("PICK_DBREFS", [], null, "Can choose the dbref of an object it creates."),
		new("Player_Create", [], null, "Can create players with @pcreate."),
		new("Poll", [], null, "Can set the WHO list's poll message with @poll."),
		new("Queue", [], null, "Has the larger command queue limit wizards have."),
		new("Quotas", [], null, "Can see other players' quotas."),
		new("Search", [], null, "Can @search, @find and @stats other players' objects."),
		new("See_All", [], null, "Can examine and see everything, without changing it."),
		new("See_Queue", [], null, "Can see every object's queue with @ps."),
		new("See_OOB", [], null, "Can see out-of-band traffic from other connections."),
		new("Send_Image", [], null, "Can show pictures with image(), figure() and Markdown, from the hosts image_hosts allows."),
		new("Send_OOB", ["Pueblo_Send"], null, "Can send out-of-band messages such as Pueblo and GMCP."),
		new("SQL_OK", ["Use_SQL"], null, "Can run SQL queries."),
		new("Tport_Anything", ["tel_anything"], null, "Can @teleport any object."),
		new("Tport_Anywhere", ["tel_anywhere"], null, "Can @teleport to any location."),
		new("Unkillable", [], null, "Cannot be killed."),
	];

	/// <summary>The power <paramref name="nameOrAlias"/> names, if it is one of these.</summary>
	public static Power? Find(string nameOrAlias) => All.FirstOrDefault(power => power.AnswersTo(nameOrAlias));

	/// <summary>The power a scope backs, if it backs one.</summary>
	public static Power? ForScope(string scope)
		=> All.FirstOrDefault(power => string.Equals(power.Scope, scope, StringComparison.OrdinalIgnoreCase));

	/// <summary>The power a role stands for, if it stands for one.</summary>
	public static Power? ForRole(string slug)
		=> All.FirstOrDefault(power => string.Equals(power.Role, slug, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The two PennMUSH flags that are roles: WIZARD is the <c>wizard</c> role and ROYALTY the <c>royalty</c>
/// role. <c>@set</c> assigns and removes the role, and <c>hasflag()</c>, <c>flags()</c> and the
/// <c>FLAG^</c> lock key answer from it. Every other flag is stored on the object as before.
/// </summary>
public static class RoleFlags
{
	/// <summary>One role-backed flag.</summary>
	/// <param name="Name">The flag's name.</param>
	/// <param name="Symbol">The letter <c>flags()</c> shows.</param>
	/// <param name="Role">The role assigned by <c>@set</c>.</param>
	/// <param name="Scope">The scope that answers <c>hasflag()</c>.</param>
	public sealed record Flag(string Name, string Symbol, string Role, string Scope);

	public static readonly IReadOnlyList<Flag> All =
	[
		new("WIZARD", "W", BuiltInRoles.WizardSlug, PortalPermission.GameWizard),
		new("ROYALTY", "r", BuiltInRoles.RoyaltySlug, PortalPermission.GameRoyalty),
	];

	/// <summary>The role-backed flag named <paramref name="name"/>, if it is one.</summary>
	public static Flag? Find(string name)
		=> All.FirstOrDefault(flag => string.Equals(flag.Name, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>The flag a role stands for, if it stands for one.</summary>
	public static Flag? ForRole(string slug)
		=> All.FirstOrDefault(flag => string.Equals(flag.Role, slug, StringComparison.OrdinalIgnoreCase));
}
