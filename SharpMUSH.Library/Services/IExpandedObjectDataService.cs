using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface IExpandedObjectDataService
{
	public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj, Type type);
	public ValueTask SetExpandedDataAsync<T>(SharpObject obj, Type type, T data);
}