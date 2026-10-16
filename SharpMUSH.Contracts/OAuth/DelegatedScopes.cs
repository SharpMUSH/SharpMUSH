using System.Text.RegularExpressions;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.OAuth;

/// <summary>
/// What a player may let an app do as one of their characters. Beyond <see cref="Profile"/> and
/// <see cref="Commands"/>, a scope is a group of permissions: the role editor's four groups (<see cref="Content"/>,
/// <see cref="Build"/>, <see cref="Manage"/>, <see cref="Game"/>) for the built-in ones, and a custom permission's
/// category (<see cref="CategoryScope"/>) for the rest. While an app acts, the character holds only the permissions
/// in the groups the player allowed it (<see cref="Covers"/>); everything else reads as not held. A game that wants
/// an app kept away from one command restricts the command (<c>@command/restrict &lt;command&gt;=noapp</c>).
/// </summary>
public static partial class DelegatedScopes
{
	/// <summary>Who the character is: its name and objid (<c>GET api/oauth/me</c>). Every grant has it.</summary>
	public const string Profile = "profile";

	/// <summary>
	/// Run commands and evaluate softcode as the character (<c>POST api/commands</c>, <c>api/commands/eval</c>), with the
	/// permissions that reach only the character's own things (<see cref="OwnWork"/>).
	/// </summary>
	public const string Commands = "commands";

	/// <summary>The wiki and media permissions.</summary>
	public const string Content = "content";

	/// <summary>Applications and packages, and the Builder power.</summary>
	public const string Build = "build";

	/// <summary>The portal's staff permissions: configuration, roles, players, jobs, the queue.</summary>
	public const string Manage = "manage";

	/// <summary>Staff powers in the game: wizard, royalty, the powers, control and the chat and server operators.</summary>
	public const string Game = "game";

	/// <summary>The built-in scopes, in the order lists show them. A custom permission category's scope sorts after them.</summary>
	public static readonly IReadOnlyList<string> All = [Profile, Commands, Content, Build, Manage, Game];

	/// <summary>The scopes that are groups of built-in permissions.</summary>
	public static readonly IReadOnlyList<string> PermissionGroups = [Content, Build, Manage, Game];

	/// <summary>
	/// The permissions every player holds that reach only what the character owns: the softcode editor, snapshots of its
	/// objects, its own queue and its own recurring jobs. Typing commands reaches the same things, so they come with
	/// <see cref="Commands"/>, and a player is not asked about staff groups for them.
	/// </summary>
	public static readonly IReadOnlySet<string> OwnWork = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		PortalPermission.SoftcodeUse, PortalPermission.SnapshotCapture, PortalPermission.SnapshotRestore,
		PortalPermission.JobsManageOwn, PortalPermission.QueueInspectOwn, PortalPermission.QueueControlOwn,
	};

	/// <summary>Whether <paramref name="scope"/> is one of <see cref="All"/>.</summary>
	public static bool IsKnown(string scope) => All.Contains(scope, StringComparer.Ordinal);

	/// <summary>Whether <paramref name="scope"/> is spelled as a scope can be: a built-in one, or a category's.</summary>
	public static bool IsToken(string scope) => TokenPattern().IsMatch(scope);

	/// <summary>The scope that stands for the custom permissions in <paramref name="category"/>: its name in lower case, a run of anything but letters and digits as one <c>-</c>.</summary>
	public static string CategoryScope(string category)
		=> NotTokenPattern().Replace(category.Trim().ToLowerInvariant(), "-").Trim('-');

	/// <summary>
	/// The scope that holds <paramref name="permission"/>: a built-in permission's group, or the scope of a custom
	/// permission's <paramref name="customCategory"/>; null when it is neither. The Builder power is building, so it is
	/// in <see cref="Build"/>, not with the other powers; <see cref="OwnWork"/> is in <see cref="Commands"/>.
	/// </summary>
	public static string? ScopeOf(string permission, string? customCategory = null)
	{
		if (OwnWork.Contains(permission)) return Commands;
		if (string.Equals(permission, PortalPermission.GamePower("Builder"), StringComparison.OrdinalIgnoreCase)) return Build;
		return PortalPermission.GroupOf(permission) switch
		{
			PortalPermission.GroupContent => Content,
			PortalPermission.GroupBuild => Build,
			PortalPermission.GroupManage => Manage,
			PortalPermission.GroupGame => Game,
			_ => customCategory is null ? null : CategoryScope(customCategory),
		};
	}

	/// <summary>
	/// Whether an app holding <paramref name="scopes"/> may use <paramref name="permission"/> the character holds.
	/// <see cref="PortalPermission.Administrator"/>, which stands for every permission, needs every group.
	/// </summary>
	public static bool Covers(IReadOnlyCollection<string> scopes, string permission, string? customCategory = null)
		=> string.Equals(permission, PortalPermission.Administrator, StringComparison.OrdinalIgnoreCase)
			? HasEveryGroup(scopes)
			: ScopeOf(permission, customCategory) is { } scope && scopes.Contains(scope, StringComparer.Ordinal);

	/// <summary>Whether <paramref name="scopes"/> holds all of <see cref="PermissionGroups"/>: what acting as God or an administrator needs.</summary>
	public static bool HasEveryGroup(IReadOnlyCollection<string> scopes)
		=> PermissionGroups.All(group => scopes.Contains(group, StringComparer.Ordinal));

	/// <summary>
	/// <paramref name="granted"/> as an app holding <paramref name="scopes"/> sees it: only the permissions it covers.
	/// </summary>
	/// <param name="customCategories">Each custom permission's category, by scope.</param>
	public static IReadOnlySet<string> Cap(IReadOnlySet<string> granted, IReadOnlyCollection<string> scopes,
		IReadOnlyDictionary<string, string> customCategories)
		=> granted.Where(permission => Covers(scopes, permission, customCategories.GetValueOrDefault(permission)))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// <paramref name="scopes"/> without the groups in which the character holds no permission. Allowing one would
	/// change nothing today, and would hand the app whatever the character is given there later without asking; an app
	/// that still wants it asks again once the character holds something in it.
	/// </summary>
	/// <param name="held">The permissions the character holds.</param>
	/// <param name="customCategories">Each custom permission's category, by scope.</param>
	public static IReadOnlyList<string> Relevant(IReadOnlyList<string> scopes, IReadOnlySet<string> held,
		IReadOnlyDictionary<string, string> customCategories)
	{
		if (held.Contains(PortalPermission.Administrator)) return scopes;
		var groups = held.Select(permission => ScopeOf(permission, customCategories.GetValueOrDefault(permission)))
			.OfType<string>().ToHashSet(StringComparer.Ordinal);
		return [.. scopes.Where(scope => scope is Profile or Commands || groups.Contains(scope))];
	}

	/// <summary>
	/// A <c>scope</c> parameter (space-separated, RFC 6749 §3.3) as an ordered list with <see cref="Profile"/> always in
	/// it; null when a name is not spelled as a scope. Whether a category's scope names a category is the caller's to
	/// check. Nothing named is <see cref="Profile"/> alone.
	/// </summary>
	public static IReadOnlyList<string>? Parse(string? scope)
	{
		var named = (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (named.Any(name => !IsToken(name))) return null;
		return Order([Profile, .. named]);
	}

	/// <summary><paramref name="scopes"/> as a <c>scope</c> parameter.</summary>
	public static string Format(IEnumerable<string> scopes) => string.Join(' ', scopes);

	/// <summary>The scopes in both lists, in order.</summary>
	public static IReadOnlyList<string> Intersect(IEnumerable<string> left, IEnumerable<string> right)
	{
		var other = right.ToHashSet(StringComparer.Ordinal);
		return Order(left.Where(other.Contains));
	}

	/// <summary>The scopes in either list, in order.</summary>
	public static IReadOnlyList<string> Union(IEnumerable<string> left, IEnumerable<string> right) => Order(left.Concat(right));

	/// <summary>Without repeats: <see cref="All"/>'s order, then the categories' scopes by name.</summary>
	public static IReadOnlyList<string> Order(IEnumerable<string> scopes)
	{
		var distinct = scopes.Distinct(StringComparer.Ordinal).ToArray();
		return
		[
			.. All.Where(known => distinct.Contains(known, StringComparer.Ordinal)),
			.. distinct.Where(scope => !IsKnown(scope)).Order(StringComparer.Ordinal),
		];
	}

	[GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
	private static partial Regex TokenPattern();

	[GeneratedRegex("[^a-z0-9]+")]
	private static partial Regex NotTokenPattern();
}
