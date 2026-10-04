using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Derives <see cref="PortalRole"/> values from object flags, following the
/// flag-based privilege hierarchy defined in PennMUSH: WIZARD → Wizard,
/// ROYALTY → Royalty, the Builder power → Builder, otherwise Player. Character #1 is always God.
/// </summary>
public class RoleDerivationService : IRoleDerivationService
{
	private const string WizardFlag = "WIZARD";
	private const string RoyaltyFlag = "ROYALTY";
	private const string BuilderPower = "Builder";

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
	public PortalRole DeriveRole(int dbrefNumber, IEnumerable<SharpObjectFlag> flags, IEnumerable<SharpPower> powers)
	{
		var role = DeriveRole(dbrefNumber, flags);
		return role < PortalRole.Builder && powers.Any(p => string.Equals(p.Name, BuilderPower, StringComparison.OrdinalIgnoreCase))
			? PortalRole.Builder
			: role;
	}

	/// <inheritdoc />
	public PortalRole DeriveAccountRole(IEnumerable<(int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)> characters)
		=> characters
			.Select(character => DeriveRole(character.DbrefNumber, character.Flags))
			.Append(PortalRole.Guest)
			.Max();
}

/// <summary>Reads a character's flags and powers and derives its tier.</summary>
public static class RoleDerivationExtensions
{
	public static async ValueTask<PortalRole> DeriveRoleAsync(this IRoleDerivationService derivation, SharpPlayer character, CancellationToken ct = default)
		=> derivation.DeriveRole(character.Object.Key,
			await character.Object.Flags.Value.ToListAsync(ct),
			await character.Object.Powers.Value.ToListAsync(ct));
}
