using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The attribute table's read and write keys (<see cref="AttributeLockKind"/>), set by staff with <c>@attribute/lock</c>:
/// each grants what the usual rules refuse. On an account's holder a write key also binds the account's own characters,
/// who write that attribute only by passing it.
/// </summary>
public interface IAttributeKeyAccess
{
	/// <summary>
	/// Whether <paramref name="who"/> passes the <paramref name="kind"/> key that applies to <paramref name="longName"/> on
	/// <paramref name="target"/>: the attribute table key of the nearest level that has one, from the leaf up. False when
	/// no key applies.
	/// </summary>
	ValueTask<bool> PassesAttributeKey(AnySharpObject who, AnySharpObject target, AttributeLockKind kind, string longName);

	/// <summary>
	/// <see cref="IPermissionService.CanSet"/> once the caller has already established control or the write key for the whole path
	/// being written (<see cref="AttributeKeyAccessExtensions.ControlsOrPassesWriteKey"/>): only the attribute flags of
	/// <paramref name="attribute"/> are checked.
	/// </summary>
	ValueTask<bool> CanSetValueOnceAllowed(AnySharpObject executor, AnySharpObject target, params SharpAttribute[] attribute);
}

public static class AttributeKeyAccessExtensions
{
	/// <summary>
	/// Whether <paramref name="who"/> may write <paramref name="longName"/> on <paramref name="target"/> as far as control
	/// goes: by controlling the object, or by passing the attribute's write key.
	/// </summary>
	public static async ValueTask<bool> ControlsOrPassesWriteKey(this IPermissionService permissions, AnySharpObject who,
		AnySharpObject target, string longName)
		=> await permissions.Controls(who, target)
			|| (permissions is IAttributeKeyAccess keys
				&& await keys.PassesAttributeKey(who, target, AttributeLockKind.Write, longName));

	/// <summary><see cref="IAttributeKeyAccess.CanSetValueOnceAllowed"/> when the permission service offers it, else <see cref="IPermissionService.CanSet"/>.</summary>
	public static ValueTask<bool> CanSetValueOnceAllowed(this IPermissionService permissions, AnySharpObject executor,
		AnySharpObject target, params SharpAttribute[] attribute)
		=> permissions is IAttributeKeyAccess keys ? keys.CanSetValueOnceAllowed(executor, target, attribute) : permissions.CanSet(executor, target, attribute);
}
