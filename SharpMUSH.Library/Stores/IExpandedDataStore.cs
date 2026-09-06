using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Typed JSON side-documents attached to an object or to the server as a whole.
/// </summary>
public interface IExpandedDataStore
{
	/// <summary>
	/// Sets expanded data for a SharpObject, that does not fit on the light-weight nature of a SharpObject or Attributes.
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the Expanded Object Data for a SharpObject. 
	/// </summary>
	/// <param name="sharpObjectId">Database Id</param>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets expanded data for a SharpObject, for the server as a whole.
	/// </summary>
	/// <param name="dataType">Type being stored. Each Type gets its own storage.</param>
	/// <param name="data">Json body to set.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the Expanded Object Data for the server as a whole. 
	/// </summary>
	/// <param name="dataType">Type being queried. Each Type gets its ow n storage.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>A Json String with the data stored within.</returns>
	ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default);
}
