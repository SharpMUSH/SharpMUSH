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

		var flagList = flags.ToList();

		if (flagList.Any(f => string.Equals(f.Name, WizardFlag, StringComparison.OrdinalIgnoreCase)))
			return PortalRole.Wizard;

		if (flagList.Any(f => string.Equals(f.Name, RoyaltyFlag, StringComparison.OrdinalIgnoreCase)))
			return PortalRole.Royalty;

		return PortalRole.Player;
	}

	/// <inheritdoc />
	public PortalRole DeriveAccountRole(IEnumerable<(int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)> characters)
	{
		var best = PortalRole.Guest;

		foreach (var (number, flags) in characters)
		{
			var role = DeriveRole(number, flags);
			if (role > best)
				best = role;
		}

		return best;
	}
}
