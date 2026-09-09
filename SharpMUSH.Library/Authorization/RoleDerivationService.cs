using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Derives <see cref="PortalRole"/> values from object flags, following the
/// flag-based privilege hierarchy defined in PennMUSH: WIZARD → Wizard,
/// ROYALTY → Royalty, otherwise Player. Character #1 is always God.
/// </summary>
public class RoleDerivationService : IRoleDerivationService
{
	private const string WizardFlag = "WIZARD";
	private const string RoyaltyFlag = "ROYALTY";

	/// <inheritdoc />
	public PortalRole DeriveRole(int dbrefNumber, IEnumerable<SharpObjectFlag> flags)
	{
		if (dbrefNumber == 1)
			return PortalRole.God;

		return flags
			.Select(f => string.Equals(f.Name, WizardFlag, StringComparison.OrdinalIgnoreCase) ? PortalRole.Wizard
				: string.Equals(f.Name, RoyaltyFlag, StringComparison.OrdinalIgnoreCase) ? PortalRole.Royalty
				: PortalRole.Player)
			.Append(PortalRole.Player)
			.Max();
	}

	/// <inheritdoc />
	public PortalRole DeriveAccountRole(IEnumerable<(int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)> characters)
		=> characters
			.Select(character => DeriveRole(character.DbrefNumber, character.Flags))
			.Append(PortalRole.Guest)
			.Max();
}
