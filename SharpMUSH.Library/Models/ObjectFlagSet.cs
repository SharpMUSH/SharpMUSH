using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models;

/// <summary>
/// One object's flags and grants, read once. Each <c>HasFlag</c>/<c>IsWizard</c>/<c>IsPriv</c> on the
/// object itself is a fresh read; a caller that asks several of them about the same object reads the
/// set once with <see cref="HelperFunctions.ReadFlagsAsync(SharpObject, CancellationToken)"/> and asks it.
/// </summary>
/// <remarks>
/// Each question answers exactly as its per-read counterpart in <see cref="HelperFunctions"/> does:
/// <see cref="Has"/> is <c>HasFlag</c> (name or alias, case-insensitive),
/// <see cref="HasOrLetter"/> is <c>HasFlagOrLetter</c> (plus the exact single-letter fallback). WIZARD
/// and ROYALTY are roles (<see cref="RoleFlags"/>): they answer from <see cref="Grants"/> and appear in
/// <see cref="Flags"/> when a role or override shows them.
/// </remarks>
public sealed class ObjectFlagSet
{
	// Built on the first question: a listing that only shows the flags never asks one.
	private HashSet<string>? _names;
	private HashSet<string>? _namesAndAliases;
	private HashSet<string>? _symbols;

	public ObjectFlagSet(DBRef owner, IReadOnlyList<SharpObjectFlag> flags, ObjectGrants grants)
	{
		Object = owner;
		Grants = grants;
		var shown = new List<SharpObjectFlag>(flags.Count);
		// Indexed loops here and below: a foreach over the interface boxes an enumerator, once per object listed.
		for (var i = 0; i < flags.Count; i++)
		{
			if (!IsRoleFlag(flags[i])) shown.Add(flags[i]);
		}
		shown.AddRange(HelperFunctions.RoleFlagsShown(grants));
		Flags = shown;
	}

	private static bool IsRoleFlag(SharpObjectFlag flag)
	{
		for (var i = 0; i < RoleFlags.All.Count; i++)
		{
			if (string.Equals(RoleFlags.All[i].Name, flag.Name, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	private HashSet<string> Names => _names ??= new(Flags.Select(flag => flag.Name), StringComparer.OrdinalIgnoreCase);

	private HashSet<string> NamesAndAliases => _namesAndAliases ??= new(
		Flags.SelectMany(flag => (flag.Aliases ?? []).Prepend(flag.Name)), StringComparer.OrdinalIgnoreCase);

	private HashSet<string> Symbols => _symbols ??= new(Flags.Select(flag => flag.Symbol), StringComparer.Ordinal);

	/// <summary>The object the flags were read from.</summary>
	public DBRef Object { get; }

	/// <summary>The object's grants, read with the flags.</summary>
	public ObjectGrants Grants { get; }

	/// <summary>
	/// The flags as read, in the order the store returned them, then the role-backed flags the object's
	/// grants show.
	/// </summary>
	public IReadOnlyList<SharpObjectFlag> Flags { get; }

	/// <summary><c>HasFlag</c>: a flag of this name or alias is set.</summary>
	public bool Has(string flag) => NamesAndAliases.Contains(flag);

	/// <summary><c>HasFlagOrLetter</c>: <see cref="Has"/>, or a single character equal to a set flag's letter.</summary>
	public bool HasOrLetter(string nameOrLetter)
		=> Has(nameOrLetter) || (nameOrLetter.Length == 1 && Symbols.Contains(nameOrLetter));

	/// <summary>PennMUSH <c>God(x)</c>, unless an app or a privilege ceiling lowers it.</summary>
	public bool IsGod => Object.Number == 1
		&& Authorization.PrivilegeCeiling.Over(1) is null
		&& (OAuth.DelegatedApp.ActingAs(Object) is not { } app || OAuth.DelegatedScopes.HasEveryGroup(app.Scopes));

	/// <summary>PennMUSH <c>Wizard(x)</c>: God, or granted <see cref="PortalPermission.GameWizard"/>.</summary>
	public bool IsWizard => IsGod || Grants.Has(PortalPermission.GameWizard);

	/// <summary>PennMUSH <c>Royalty(x)</c>: a role or override shows <see cref="PortalPermission.GameRoyalty"/>.</summary>
	public bool IsRoyalty => Grants.Shows(PortalPermission.GameRoyalty);

	/// <summary>PennMUSH <c>Hasprivs(x)</c>: God, Wizard or Royalty.</summary>
	public bool IsPriv => IsGod || IsWizard || IsRoyalty;

	/// <summary>Controls every object but #1 (the wizard half of PennMUSH <c>controls()</c>).</summary>
	public bool ControlsAll => IsGod || Grants.Has(PortalPermission.ControlAll);

	/// <summary>Controlled only by holders of <see cref="PortalPermission.ControlAll"/>.</summary>
	public bool IsWizardProtected => Grants.Has(PortalPermission.ProtectWizard);

	/// <summary>Not controlled by anyone without <see cref="PortalPermission.ProtectAdmin"/>.</summary>
	public bool IsAdminProtected => Grants.Has(PortalPermission.ProtectAdmin);

	public bool IsMistrust => Names.Contains("MISTRUST");

	/// <summary>PennMUSH <c>Inherit(x)</c>: the TRUST flag, by name or alias.</summary>
	public bool IsTrust => Has("Trust");
}
