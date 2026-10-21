using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Begins caller-owned package writes using the current world's service graph.</summary>
public interface IPackageWriteTransactionFactory
{
	ValueTask<PackageWriteTransaction> CreateAsync(DBRef packageManager, CancellationToken cancellationToken = default);
}
