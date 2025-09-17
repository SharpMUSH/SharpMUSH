using System.Text.Json;
using System.Text.Json.Serialization;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class ExpandedObjectDataService(IMediator mediator) : IExpandedObjectDataService
{
	private readonly JsonSerializerOptions _jsonSerializerOptionForNull = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly JsonSerializerOptions _jsonSerializerOptionForOthers = new();

	public async ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class
	{
		var result = await mediator.Send(new ExpandedDataQuery(obj, typeof(T).Name));
		if (result is null) return null;

		var conversion = JsonSerializer.Deserialize<T>(result);
		return conversion;
	}

	public async ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
	{
		var json = JsonSerializer.Serialize(data, ignoreNull ? _jsonSerializerOptionForNull : _jsonSerializerOptionForOthers);
		await mediator.Send(new SetExpandedDataCommand(obj, typeof(T).Name, json));
	}
	
	public async ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
	{
		var result = await mediator.Send(new ExpandedServerDataQuery(typeof(T).Name));
		if (result is null) return null;
		
		var conversion = JsonSerializer.Deserialize<T>(result);
		return conversion;
	}

	public async ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
	{
		var json = JsonSerializer.Serialize(data, ignoreNull 
			? _jsonSerializerOptionForNull 
			: _jsonSerializerOptionForOthers);
		await mediator.Send(new SetExpandedServerDataCommand(typeof(T).Name, json));
	}
}