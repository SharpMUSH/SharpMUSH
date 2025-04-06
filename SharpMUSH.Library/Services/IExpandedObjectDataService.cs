using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Retrieves and Sets Expanded Data.
/// </summary>
public interface IExpandedObjectDataService
{
	/// <summary>
	/// Gets Expanded Data from the database.
	/// </summary>
	/// <param name="obj">Associated SharpObject. It will use its Key or Id.</param>
	/// <typeparam name="T">Expanded Object Type</typeparam>
	/// <returns>The Expanded Data, if found.</returns>
	public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T: class;

	/// <summary>
	/// Sets Expanded Data to the database.
	/// </summary>
	/// <param name="data">Data to set.</param>
	/// <param name="obj">Associated SharpObject to set the information on.</param>
	/// <param name="ignoreNull">Whether to ignore Null values on the passed in Data. Set to true if you intend to use this function as an UPDATE.</param>
	/// <typeparam name="T">Expanded Object Type</typeparam>
	public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T: class;
}