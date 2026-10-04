namespace SharpMUSH.Library.Models;

/// <summary>
/// One object's flags, read once. Each <c>HasFlag</c>/<c>IsWizard</c>/<c>IsPriv</c> on the object
/// itself is a fresh read; a caller that asks several of them about the same object reads the set
/// once with <see cref="HelperFunctions.ReadFlagsAsync(SharpObject, CancellationToken)"/> and asks it.
/// </summary>
/// <remarks>
/// Each question answers exactly as its per-read counterpart in <see cref="HelperFunctions"/> does:
/// <see cref="Has"/> is <c>HasFlag</c> (name or alias, invariant-culture case-insensitive),
/// <see cref="HasOrLetter"/> is <c>HasFlagOrLetter</c> (plus the exact single-letter fallback), and the
/// privilege predicates match the flag's <em>name</em> only, ordinal case-insensitive, as
/// <c>IsWizard</c>/<c>IsRoyalty</c>/<c>IsMistrust</c> do.
/// </remarks>
public sealed class ObjectFlagSet
{
	private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _namesAndAliases = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HashSet<string> _symbols = new(StringComparer.Ordinal);

	public ObjectFlagSet(DBRef owner, IReadOnlyList<SharpObjectFlag> flags)
	{
		Object = owner;
		Flags = flags;
		foreach (var flag in flags)
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

	/// <summary>The flags as read, in the order the store returned them.</summary>
	public IReadOnlyList<SharpObjectFlag> Flags { get; }

	/// <summary><c>HasFlag</c>: a flag of this name or alias is set.</summary>
	public bool Has(string flag) => _namesAndAliases.Contains(flag);

	/// <summary><c>HasFlagOrLetter</c>: <see cref="Has"/>, or a single character equal to a set flag's letter.</summary>
	public bool HasOrLetter(string nameOrLetter)
		=> Has(nameOrLetter) || (nameOrLetter.Length == 1 && _symbols.Contains(nameOrLetter));

	/// <summary>PennMUSH <c>God(x)</c>.</summary>
	public bool IsGod => Object.Number == 1;

	/// <summary>PennMUSH <c>Wizard(x)</c> = God(x) || has_wizard_flag(x).</summary>
	public bool IsWizard => IsGod || _names.Contains("WIZARD");

	public bool IsRoyalty => _names.Contains("ROYALTY");

	/// <summary>PennMUSH <c>Hasprivs(x)</c>: God, Wizard or Royalty.</summary>
	public bool IsPriv => IsGod || IsWizard || IsRoyalty;

	public bool IsMistrust => _names.Contains("MISTRUST");

	/// <summary>PennMUSH <c>Inherit(x)</c>: the TRUST flag, by name or alias.</summary>
	public bool IsTrust => Has("Trust");
}
