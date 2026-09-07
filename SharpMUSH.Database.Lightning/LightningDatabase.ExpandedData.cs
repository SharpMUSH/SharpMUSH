namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IExpandedDataStore"/>: typed JSON side-documents attached to an object or the server. Not
/// ported yet — every member throws <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
