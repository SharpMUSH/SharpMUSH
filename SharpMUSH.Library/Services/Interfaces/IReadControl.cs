using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Control for reading and finding. A NOMODIFY target under <c>protection_flags</c> does not take it away;
/// only changes need <see cref="IPermissionService.Controls(AnySharpObject, AnySharpObject)"/>.
/// </summary>
public interface IReadControl
{
	ValueTask<bool> Oversees(AnySharpObject who, AnySharpObject target);
}

public static class ReadControlExtensions
{
	/// <summary>
	/// <see cref="IReadControl.Oversees"/> when the permission service offers it, otherwise
	/// <see cref="IPermissionService.Controls(AnySharpObject, AnySharpObject)"/>.
	/// </summary>
	public static ValueTask<bool> Oversees(this IPermissionService permissions, AnySharpObject who, AnySharpObject target)
		=> permissions is IReadControl read ? read.Oversees(who, target) : permissions.Controls(who, target);
}
