using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// An attribute's read and write keys (<see cref="AttributeLockKind"/>): each grants what the usual rules refuse, and
/// takes nothing away.
/// </summary>
public interface IAttributeKeyAccess
{
	/// <summary>
	/// Whether <paramref name="who"/> passes the <paramref name="kind"/> key that applies to <paramref name="longName"/> on
	/// <paramref name="target"/>: the nearest level's own key, else that level's attribute table key, from the leaf up.
	/// <paramref name="existing"/> holds the levels that exist, root first. False when no key applies.
	/// </summary>
	ValueTask<bool> PassesAttributeKey(AnySharpObject who, AnySharpObject target, AttributeLockKind kind, string longName,
		IReadOnlyList<SharpAttribute> existing);

	/// <summary>
	/// <see cref="IPermissionService.CanSet"/> for a change to the value of <paramref name="attribute"/> (setting or clearing
	/// it), where passing the write key stands in for control of <paramref name="target"/>. Every flag still applies.
	/// </summary>
	ValueTask<bool> CanSetValue(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute);

	/// <summary>
	/// <see cref="PassesAttributeKey"/> for <paramref name="attributeName"/> as it stands on <paramref name="target"/>: its
	/// levels are looked up here. For a command that refuses an object it does not control before it writes.
	/// </summary>
	ValueTask<bool> PassesWriteKey(AnySharpObject who, AnySharpObject target, string attributeName);
}

public static class AttributeKeyAccessExtensions
{
	/// <summary>
	/// Whether <paramref name="who"/> may write <paramref name="longName"/> on <paramref name="target"/> as far as control
	/// goes: by controlling the object, or by passing the attribute's write key.
	/// </summary>
	public static async ValueTask<bool> ControlsOrPassesWriteKey(this IPermissionService permissions, AnySharpObject who,
		AnySharpObject target, string longName, IReadOnlyList<SharpAttribute> existing)
		=> await permissions.Controls(who, target)
			|| (permissions is IAttributeKeyAccess keys
				&& await keys.PassesAttributeKey(who, target, AttributeLockKind.Write, longName, existing));

	/// <summary>Whether <paramref name="who"/> controls <paramref name="target"/> or passes the write key of <paramref name="attributeName"/> on it.</summary>
	public static async ValueTask<bool> ControlsOrPassesWriteKey(this IPermissionService permissions, AnySharpObject who,
		AnySharpObject target, string attributeName)
		=> await permissions.Controls(who, target)
			|| (permissions is IAttributeKeyAccess keys && await keys.PassesWriteKey(who, target, attributeName));

	/// <summary><see cref="IAttributeKeyAccess.CanSetValue"/> when the permission service offers it, else <see cref="IPermissionService.CanSet"/>.</summary>
	public static ValueTask<bool> CanSetValue(this IPermissionService permissions, AnySharpObject executor,
		AnySharpObject target, params SharpAttribute[] attribute)
		=> permissions is IAttributeKeyAccess keys ? keys.CanSetValue(executor, target, attribute) : permissions.CanSet(executor, target, attribute);
}
