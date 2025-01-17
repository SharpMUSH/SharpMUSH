using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Services;

public class ExpandedObjectDataService(IMediator mediator) : IExpandedObjectDataService
{
	public async ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj, Type type)
	{
		var result = await mediator.Send(new ExpandedDataQuery(obj, type.Name));
		if (result is null) return default;
		
		var conversion = System.Text.Json.JsonSerializer.Deserialize<T>(result);
		return conversion ?? default;
	}

	public async ValueTask SetExpandedDataAsync<T>(SharpObject obj, Type type, T data)
	{
		var json = System.Text.Json.JsonSerializer.Serialize(obj);
		await mediator.Send(new SetExpandedDataCommand(obj, type.Name, json));
	}
}