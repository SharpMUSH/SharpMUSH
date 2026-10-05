using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Library.Models;

/// <summary>
/// One object's flags and grants, read once. Each <c>HasFlag</c>/<c>IsWizard</c>/<c>IsPriv</c> on the
/// object itself is a fresh read; a caller that asks several of them about the same object reads the
/// set once with <see cref="HelperFunctions.ReadFlagsAsync(SharpObject, CancellationToken)"/> and asks it.
/// </summary>
/// <remarks>
/// Each question answers exactly as its per-read counterpart in <see cref="HelperFunctions"/> does:
/// <see cref="Has"/> is <c>HasFlag</c> (name or alias, invariant-culture case-insensitive),
/// <see cref="HasOrLetter"/> is <c>HasFlagOrLetter</c> (plus the exact single-letter fallback). WIZARD
/// and ROYALTY are roles (<see cref="RoleFlags"/>): they answer from <see cref="Grants"/> and appear in
/// <see cref="Flags"/> when a role or override shows them.
/// </remarks>
public sealed class ObjectFlagSet
{
	private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _namesAndAliases = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HashSet<string> _symbols = new(StringComparer.Ordinal);

	public ObjectFlagSet(DBRef owner, IReadOnlyList<SharpObjectFlag> flags, ObjectGrants grants)
	{
		Object = owner;
		Grants = grants;
		Flags = [.. flags.Where(flag => RoleFlags.Find(flag.Name) is null), .. HelperFunctions.RoleFlagsShown(grants)];
		foreach (var flag in Flags)
		{
			_names.Add(flag.Name);
			_namesAndAliases.Add(flag.Name);
			foreach (var alias in flag.Aliases ?? [])
			{
				_namesAndAliases.Add(alias);
			}

			_symbols.Add(flag.Symbol);
		}
	}

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
	public bool Has(string flag) => _namesAndAliases.Contains(flag);

	/// <summary><c>HasFlagOrLetter</c>: <see cref="Has"/>, or a single character equal to a set flag's letter.</summary>
	public bool HasOrLetter(string nameOrLetter)
		=> Has(nameOrLetter) || (nameOrLetter.Length == 1 && _symbols.Contains(nameOrLetter));

	/// <summary>PennMUSH <c>God(x)</c>.</summary>
	public bool IsGod => Object.Number == 1;

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

	public bool IsMistrust => _names.Contains("MISTRUST");

	/// <summary>PennMUSH <c>Inherit(x)</c>: the TRUST flag, by name or alias.</summary>
	public bool IsTrust => Has("Trust");
}
