using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// What lets someone do a thing, any one of which is enough: controlling <see cref="Target"/>, or holding one of
/// <see cref="Scopes"/>; or, alone, being God. <see cref="IPermissionService.Check"/> answers
/// <see cref="Success"/> to continue, or the <see cref="PermissionRefusal"/> naming what is missing, which a command
/// tells the player (<see cref="PermissionDeniedExtensions.PermissionDenied(INotifyService, AnySharpObject, PermissionRefusal)"/>)
/// and a function returns (<see cref="PermissionRefusal.Return"/>).
/// </summary>
/// <remarks>
/// A scope is read the way the check it stands for always was: <see cref="PortalPermission.GameRoyalty"/> as the ROYALTY
/// flag, a built-in power's scope as that power, anything else (<see cref="PortalPermission.GameWizard"/> included) as a
/// permission God and the <c>administrator</c> role also hold.
/// </remarks>
public sealed record Requires
{
	private Requires(bool godOnly, AnySharpObject? target, string[] scopes)
	{
		GodOnly = godOnly;
		Target = target;
		Scopes = scopes;
	}

	/// <summary>Only God passes.</summary>
	public bool GodOnly { get; }

	/// <summary>The object whose controller passes, if control is enough.</summary>
	public AnySharpObject? Target { get; }

	/// <summary>The scopes, any one of which passes.</summary>
	public IReadOnlyList<string> Scopes { get; }

	/// <summary>Only God.</summary>
	public static Requires God() => new(true, null, []);

	/// <summary>Any one of <paramref name="scopes"/>.</summary>
	public static Requires Any(params string[] scopes) => new(false, null, scopes);

	/// <summary>Control of <paramref name="target"/>, or any one of <paramref name="scopes"/>.</summary>
	public static Requires ControlOf(AnySharpObject target, params string[] scopes) => new(false, target, scopes);

	/// <summary>Why someone who passes none of it is refused.</summary>
	public PermissionRefusal Refusal => this switch
	{
		{ GodOnly: true } => PermissionRefusal.GodOnly(),
		{ Target: { } target } => PermissionRefusal.NotControlled(target, [.. Scopes]),
		_ => PermissionRefusal.Needs([.. Scopes])
	};
}
