using LightningDB;

namespace SharpMUSH.Database.Lightning.Store;

public sealed class LightningStoreException(MDBResultCode code, string message) : Exception(message)
{
	public MDBResultCode Code { get; } = code;

	public static LightningStoreException From(MDBResultCode code, string operation) => code switch
	{
		MDBResultCode.MapFull => new(code, $"{operation}: the database reached its size ceiling. Raise SHARPMUSH_LIGHTNING_MAPSIZE (bytes) and restart."),
		MDBResultCode.TxnFull => new(code, $"{operation}: a single write touched too many pages. Split the operation into smaller batches."),
		MDBResultCode.ReadersFull => new(code, $"{operation}: too many concurrent readers. Raise LightningStoreOptions.MaxReaders."),
		_ => new(code, $"{operation}: LMDB returned {code}.")
	};
}
