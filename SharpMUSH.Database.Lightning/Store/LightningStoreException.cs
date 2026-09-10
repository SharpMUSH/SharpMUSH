using LightningDB;

namespace SharpMUSH.Database.Lightning.Store;

public sealed class LightningStoreException(MDBResultCode code, string message) : Exception(message)
{
	public MDBResultCode Code { get; } = code;

	public static LightningStoreException From(MDBResultCode code, string operation) => new(code, Describe(code, operation));

	/// <summary>What <paramref name="code"/> means for <paramref name="operation"/>, and what to do about it.</summary>
	public static string Describe(MDBResultCode code, string operation) => code switch
	{
		MDBResultCode.MapFull => $"{operation}: the database reached its size ceiling. Raise SHARPMUSH_LIGHTNING_MAPSIZE (bytes) and restart.",
		MDBResultCode.TxnFull => $"{operation}: a single write touched too many pages. Split the operation into smaller batches.",
		MDBResultCode.ReadersFull => $"{operation}: too many concurrent readers. Raise LightningStoreOptions.MaxReaders.",
		_ => $"{operation}: LMDB returned {code}."
	};
}
