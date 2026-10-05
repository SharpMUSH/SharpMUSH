using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Everything that decides one holder's permissions, whether the holder is a game object, a
/// character played through an account, or an account in the portal: the roles it holds
/// (<c>everyone</c>, the implicit <c>player</c>, its object roles and its account's roles), the
/// overrides on its account and on the object itself, and whether it is the owner (player #1).
/// </summary>
/// <param name="Roles">Every role held, <c>everyone</c> included.</param>
/// <param name="Overrides">Account Allow/Deny by scope. Absent scopes are Inherit.</param>
/// <param name="IsOwner">True for player #1 and the account linked to it, which hold every scope.</param>
public sealed record PermissionContext(
	IReadOnlyCollection<SharpRole> Roles,
	IReadOnlyDictionary<string, PermissionState> Overrides,
	bool IsOwner)
{
	/// <summary>A context that grants nothing: no roles, no overrides, not the owner.</summary>
	public static readonly PermissionContext None = new([], new Dictionary<string, PermissionState>(), false);

	/// <summary>
	/// Allow/Deny set on the game object itself (PennMUSH's powers). They outrank account overrides,
	/// being the more specific setting. Absent scopes are Inherit.
	/// </summary>
	public IReadOnlyDictionary<string, PermissionState> ObjectOverrides { get; init; } = new Dictionary<string, PermissionState>();

	/// <summary>
	/// The custom permissions the world defines (<see cref="CustomPermission"/>). They resolve like the
	/// built-in scopes; a scope that is neither is refused, so a role still naming a removed one grants nothing.
	/// </summary>
	public IReadOnlySet<string> CustomScopes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Whether <paramref name="scope"/> is a built-in permission or one of <see cref="CustomScopes"/>.</summary>
	public bool Knows(string scope) => PortalPermission.IsKnown(scope) || CustomScopes.Contains(scope);

	/// <summary>The highest priority among the held roles, or <see cref="int.MinValue"/> with none.</summary>
	public int TopPriority => Roles.Count == 0 ? int.MinValue : Roles.Max(r => r.Priority);
}

/// <summary>
/// Resolves the granted permission scopes for a <see cref="PermissionContext"/> with Discord's rules.
/// </summary>
public interface IPermissionResolver
{
	/// <summary>The scopes <paramref name="context"/> is granted.</summary>
	IReadOnlySet<string> Resolve(PermissionContext context);

	/// <summary>Whether <paramref name="context"/> holds <paramref name="scope"/>, and which layer decided it.</summary>
	PermissionExplanation Explain(PermissionContext context, string scope);
}

/// <summary>
/// Discord's permission computation, with one scope at a time standing in for one permission bit:
/// <list type="number">
/// <item>The owner holds every scope.</item>
/// <item>A held role that allows <see cref="PortalPermission.Administrator"/> grants every scope, and
/// per-account overrides do not apply.</item>
/// <item>An override on the object decides the scope when it says Allow or Deny, then one on the
/// account (Discord's member overwrite). PennMUSH's powers are object overrides.</item>
/// <item>Otherwise any held role that allows the scope grants it, whatever its priority; failing that,
/// a role that denies it refuses it (Discord pools role denies, then role allows, so an Allow on any
/// role beats a Deny on another).</item>
/// <item>Otherwise the <c>everyone</c> role decides, and a scope nobody allows is denied.</item>
/// </list>
/// Priority plays no part in this; it only orders roles for who may manage whom
/// (see <see cref="RoleHierarchy"/>). A layer's opinion on a scope it leaves on Inherit is its opinion
/// on the umbrella scope that implies it (<c>wiki.admin</c> covers <c>wiki.edit</c>), so an explicit
/// child setting always beats the umbrella within the same layer.
/// </summary>
public sealed class PermissionResolver : IPermissionResolver
{
	public IReadOnlySet<string> Resolve(PermissionContext context)
		=> PortalPermission.AllScopes.Concat(context.CustomScopes)
			.Where(scope => Explain(context, scope).Allowed).ToHashSet(StringComparer.Ordinal);

	public PermissionExplanation Explain(PermissionContext context, string scope)
	{
		if (!context.Knows(scope))
			return new(false, null, [], "unknown-scope");
		if (context.IsOwner)
			return new(true, null, [], "owner");

		var administrators = context.Roles
			.Where(r => StateOf(r.Permissions, PortalPermission.Administrator) == PermissionState.Allow).ToArray();
		if (administrators.Length > 0)
			return Decided(true, administrators, "administrator");

		switch (StateOf(context.ObjectOverrides, scope))
		{
			case PermissionState.Allow: return new(true, null, [], "object-allow");
			case PermissionState.Deny: return new(false, null, [], "object-deny");
		}

		switch (StateOf(context.Overrides, scope))
		{
			case PermissionState.Allow: return new(true, null, [], "account-allow");
			case PermissionState.Deny: return new(false, null, [], "account-deny");
		}

		var everyone = context.Roles.Where(BuiltInRoles.IsEveryone).ToArray();
		var held = context.Roles.Where(r => !BuiltInRoles.IsEveryone(r)).ToArray();
		var allowing = held.Where(r => StateOf(r.Permissions, scope) == PermissionState.Allow).ToArray();
		if (allowing.Length > 0)
			return Decided(true, allowing, "role-allow");
		var denying = held.Where(r => StateOf(r.Permissions, scope) == PermissionState.Deny).ToArray();
		if (denying.Length > 0)
			return Decided(false, denying, "role-deny");

		return everyone.Any(r => StateOf(r.Permissions, scope) == PermissionState.Allow)
			? Decided(true, everyone, "everyone-allow")
			: new(false, null, [], "default-deny");
	}

	/// <summary>
	/// One layer's stance on <paramref name="scope"/>: its explicit Allow/Deny, else its stance on an
	/// umbrella scope that implies it (Allow over Deny when two umbrellas disagree), else Inherit.
	/// </summary>
	public static PermissionState StateOf(IReadOnlyDictionary<string, PermissionState> permissions, string scope)
	{
		var explicitState = permissions
			.Where(p => string.Equals(p.Key, scope, StringComparison.OrdinalIgnoreCase))
			.Select(p => p.Value).FirstOrDefault(PermissionState.Inherit);
		if (explicitState != PermissionState.Inherit) return explicitState;
		var inherited = PortalPermission.ParentScopes(scope).Select(parent => StateOf(permissions, parent)).ToArray();
		return inherited.Contains(PermissionState.Allow) ? PermissionState.Allow
			: inherited.Contains(PermissionState.Deny) ? PermissionState.Deny
			: PermissionState.Inherit;
	}

	private static PermissionExplanation Decided(bool allowed, IReadOnlyCollection<SharpRole> roles, string reason)
		=> new(allowed, roles.Max(r => r.Priority), roles.Select(r => r.Slug).Order(StringComparer.Ordinal).ToArray(), reason);
}

/// <summary>
/// Why a scope was granted or refused. <paramref name="Priority"/> is the highest priority among the
/// deciding <paramref name="Roles"/>, null when no role decided. <paramref name="Reason"/> is one of
/// <c>owner</c>, <c>administrator</c>, <c>object-allow</c>, <c>object-deny</c>, <c>account-allow</c>,
/// <c>account-deny</c>, <c>role-allow</c>,
/// <c>role-deny</c>, <c>everyone-allow</c>, <c>default-deny</c> or <c>unknown-scope</c>.
/// </summary>
public sealed record PermissionExplanation(bool Allowed, int? Priority, string[] Roles, string Reason);
