using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using DotNext.Threading;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	#region Expanded Data

	public async ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data,
		CancellationToken ct = default)
	{
		// Get the edge that leads to it, otherwise we will have to create one.
		var result = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {sharpObjectId} GRAPH {DatabaseConstants.GraphObjectData} RETURN v._key",
			cancellationToken: ct);

		var key = result.FirstOrDefault();
		if (key is not null)
		{
			await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphObjectData, DatabaseConstants.ObjectData,
				key, new Dictionary<string, object> { { dataType, data } }, waitForSync: true, cancellationToken: ct, keepNull: false);
			return;
		}

		var newJson = new Dictionary<string, object> { { dataType, data } };

		var newVertex = await arangoDb.Graph.Vertex.CreateAsync<dynamic, dynamic>(handle,
			DatabaseConstants.GraphObjectData,
			DatabaseConstants.ObjectData,
			newJson, waitForSync: true, cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(handle,
			DatabaseConstants.GraphObjectData,
			DatabaseConstants.HasObjectData, new SharpEdgeCreateRequest(
				From: sharpObjectId,
				To: newVertex.Vertex.GetProperty("_id").GetString()!), waitForSync: true, cancellationToken: ct);
	}

	public async ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType,
		CancellationToken ct = default)
	{
		// Get the edge that leads to it, otherwise we will have to create one.
		var result = await arangoDb.Query.ExecuteAsync<T>(handle,
			$"FOR v IN 1..1 OUTBOUND {sharpObjectId} GRAPH {DatabaseConstants.GraphObjectData} RETURN v.{dataType}",
			cancellationToken: ct);
		var resultingValue = result.FirstOrDefault();
		return resultingValue;
	}

	public async ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken ct = default)
	{
		// Use a transaction with exclusive lock to prevent write-write conflicts
		var transaction = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			AllowImplicit = false,
			Collections = new ArangoTransactionScope
			{
				Exclusive = [DatabaseConstants.ServerData]
			}
		}, ct);

		try
		{
			var newJson = new Dictionary<string, object>
			{
				{ "_key", dataType },
				{ "Data", data }
			};

			_ = await arangoDb.Document.CreateAsync(transaction,
				DatabaseConstants.ServerData,
				newJson,
				overwriteMode: ArangoOverwriteMode.Update,
				mergeObjects: true,
				keepNull: true,
				waitForSync: true,
				cancellationToken: ct);

			await arangoDb.Transaction.CommitAsync(transaction, ct);
		}
		catch
		{
			// Transaction will automatically be aborted if not committed
			throw;
		}
	}

	public record ArangoDocumentWrapper<T>(T Data);

	public async ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken ct = default)
	{
		try
		{
			var result = await arangoDb.Document.GetAsync<ArangoDocumentWrapper<T>>(handle,
				DatabaseConstants.ServerData,
				dataType, cancellationToken: ct);

			return result.Data;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to retrieve expanded server data for type '{DataType}' from collection '{Collection}'", dataType, DatabaseConstants.ServerData);
			return default;
		}
	}

	#endregion
}
