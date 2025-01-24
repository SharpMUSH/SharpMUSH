using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface IExpandedObjectDataService
{
	public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T: class;
	public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T: class;
}