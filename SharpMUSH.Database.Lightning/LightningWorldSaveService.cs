using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary><see cref="IWorldSaveService"/> for the Lightning provider: a forced LMDB sync.</summary>
public sealed class LightningWorldSaveService(LightningDatabase database) : IWorldSaveService
{
	public ValueTask<Result<Success>> SaveAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<Result<Success>>(database.Store.Flush() switch
		{
			MDBResultCode.Success => new Success(),
			var code => new Error<string>(LightningStoreException.Describe(code, "save"))
		});
}
