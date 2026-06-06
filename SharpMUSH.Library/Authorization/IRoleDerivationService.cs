using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Derives a <see cref="PortalRole"/> from the flags carried by a MUSH character.
/// </summary>
public interface IRoleDerivationService
{
	/// <summary>
	/// Returns the highest <see cref="PortalRole"/> applicable to a single character
	/// whose DBRef number and flag set are supplied.
	/// </summary>
	/// <param name="dbrefNumber">The numeric part of the character's DBRef. 1 = God character.</param>
	/// <param name="flags">All flags currently set on the character.</param>
	PortalRole DeriveRole(int dbrefNumber, IEnumerable<SharpObjectFlag> flags);

	/// <summary>
	/// Returns the highest <see cref="PortalRole"/> among all characters linked to an account.
	/// Each tuple is (dbrefNumber, flags).
	/// </summary>
	PortalRole DeriveAccountRole(IEnumerable<(int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)> characters);
}
