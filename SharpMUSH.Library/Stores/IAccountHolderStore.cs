using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>The holders of accounts' attributes (<see cref="AccountHolders"/>).</summary>
public interface IAccountHolderStore
{
	/// <summary>
	/// The holder <paramref name="holder"/> names, when its account exists; <see cref="NotFound"/> when it does not, or
	/// when <paramref name="holder"/> is not a holder's reference.
	/// </summary>
	ValueTask<Found<SharpAccountHolder>> GetAccountHolderAsync(DBRef holder, CancellationToken cancellationToken = default);
}
