using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using DotNext.Collections.Generic;
using DotNext.Threading;
using FSharpPlus.Control;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Services.Interfaces;
using AsyncEnumerable = System.Linq.AsyncEnumerable;

namespace SharpMUSH.Database.ArangoDB;

// TODO: Unit of Work / Transaction around all of this! Otherwise it risks the stability of the Database.
public class ArangoDatabase(
	ILogger<ArangoDatabase> logger,
	IArangoContext arangoDb,
	ArangoHandle handle,
	IMediator mediator,
	IPasswordService passwordService // TODO: This doesn't belong in the database layer
) : ISharpDatabase, ISharpDatabaseWithLogging
{
	private const string StartVertex = "startVertex";

	public async ValueTask Migrate()
	{
		logger.LogInformation("Migrating Database");

		var migrator = new ArangoMigrator(arangoDb)
		{
			HistoryCollection = "MigrationHistory"
		};

		if (!await migrator.Context.Database.ExistAsync(handle))
		{
			await migrator.Context.Database.CreateAsync(handle);
		}

		migrator.AddMigrations(typeof(ArangoDatabase).Assembly);
		await migrator.UpgradeAsync(handle);

		logger.LogInformation("Migration Completed.");
	}

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var objectLocation = await GetObjectNodeAsync(location);

		var transaction = new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			Collections = new ArangoTransactionScope
			{
				Exclusive =
				[
					DatabaseConstants.Objects,
					DatabaseConstants.Players,
					DatabaseConstants.IsObject,
					DatabaseConstants.HasObjectOwner,
					DatabaseConstants.AtLocation,
					DatabaseConstants.HasHome
				]
			}
		};

		var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, transaction);

		var obj = await arangoDb.Graph.Vertex.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(
			transactionHandle, DatabaseConstants.GraphObjects,
			DatabaseConstants.Objects, new SharpObjectCreateRequest(
				name,
				DatabaseConstants.TypePlayer,
				[],
				time,
				time
			), returnNew: true);

		var hashedPassword = passwordService.HashPassword($"#{obj.New.Key}:{obj.New.CreationTime}", password);

		var playerResult = await arangoDb.Document.CreateAsync<SharpPlayerCreateRequest, SharpPlayerQueryResult>(
			transactionHandle,
			DatabaseConstants.Players,
			new SharpPlayerCreateRequest([], hashedPassword));

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(playerResult.Id, obj.New.Id));

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphObjectOwners,
			DatabaseConstants.HasObjectOwner, new SharpEdgeCreateRequest(playerResult.Id, playerResult.Id));

		var idx = objectLocation.Match(
			player => player.Id,
			room => room.Id,
			_ => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			_ => throw new ArgumentException("A player must have a valid creation location!"));

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphLocations,
			DatabaseConstants.AtLocation, new SharpEdgeCreateRequest(playerResult.Id, idx!));

		// TODO: This should use a Default Home, which should be passed down from above.
		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(playerResult.Id, idx!));

		await arangoDb.Transaction.CommitAsync(transactionHandle);

		return new DBRef(int.Parse(obj.New.Key), time);
	}

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeRoom, [], time, time));
		var room = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Rooms, new SharpRoomCreateRequest());

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(room.Id, obj.Id));
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction()
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive =
					[
						DatabaseConstants.Objects, DatabaseConstants.Things, DatabaseConstants.IsObject,
						DatabaseConstants.AtLocation, DatabaseConstants.HasHome, DatabaseConstants.HasObjectOwner
					]
				}
			});
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(transaction,
			DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeThing, [], time, time));
		var thing = await arangoDb.Document.CreateAsync(transaction, DatabaseConstants.Things,
			new SharpThingCreateRequest([]));

		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(thing.Id, obj.Id));
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			new SharpEdgeCreateRequest(thing.Id, location.Id));
		// TODO: Fix, this should use a default home location, passed down to this.
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(thing.Id, location.Id));
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjectOwners,
			DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!));

		await arangoDb.Transaction.CommitAsync(transaction);
		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location)
	{
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(exit.Id!, location.Id));
		return true;
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
		SharpPlayer creator)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle,
			DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeExit, [], time, time));
		var exit = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Exits,
			new SharpExitCreateRequest(aliases));

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(exit.Id, obj.Id));
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			new SharpEdgeCreateRequest(exit.Id, location.Id));
		/* await arangoDB.Graph.Edge.CreateAsync(handle, DatabaseConstants.graphHomes, DatabaseConstants.hasHome,
			new SharpEdgeCreateRequest(exit.Id, location.Id)); */
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!));

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name)
		=> (await arangoDb.Query.ExecuteAsync<SharpObjectFlagQueryResult>(
			handle,
			$"FOR v in @@C1 FILTER v.Name == @flag RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C1", DatabaseConstants.ObjectFlags },
				{ "flag", name }
			},
			cache: true)).Select(SharpObjectFlagQueryToSharpChannel).FirstOrDefault();

	public async ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync()
		=> (await arangoDb.Query.ExecuteAsync<SharpObjectFlagQueryResult>(
			handle,
			$"FOR v in {DatabaseConstants.ObjectFlags:@} RETURN v",
			cache: true)).Select(SharpObjectFlagQueryToSharpChannel);

	private async ValueTask<string?> GetObjectFlagEdge(AnySharpObject target, SharpObjectFlag flag)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {target.Object().Id} GRAPH {DatabaseConstants.GraphFlags} FILTER v._id == {flag.Id} RETURN e._id");
		return result.FirstOrDefault()?.Id;
	}
	
	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag)
	{
		var edge = await GetObjectFlagEdge(target, flag);
		if (edge is not null) return false;
		
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphFlags, DatabaseConstants.HasFlags,
			new SharpEdgeCreateRequest(target.Object().Id!, flag.Id!));

		return true;
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag)
	{
		var edge = await GetObjectFlagEdge(target, flag);
		if (edge == null) return false;
		
		await arangoDb.Graph.Edge.RemoveAsync<string>(handle, DatabaseConstants.GraphFlags, DatabaseConstants.HasFlags,
			edge);

		return true;
	}

	private async ValueTask<IEnumerable<SharpPower>> GetPowersAsync(string id)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpPowerQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphPowers} RETURN v");

		return result.Select(x => new SharpPower()
		{
			Alias = x.Alias,
			Name = x.Alias,
			System = x.System,
			SetPermissions = x.SetPermissions,
			TypeRestrictions = x.TypeRestrictions,
			UnsetPermissions = x.UnsetPermissions,
			Id = x.Id
		});
	}

	public async ValueTask<IEnumerable<SharpObjectFlag>> GetObjectFlagsAsync(string id)
		=> (await arangoDb.Query.ExecuteAsync<SharpObjectFlagQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphFlags} RETURN v"))
			.Select(SharpObjectFlagQueryToSharpChannel);

	public async ValueTask<IEnumerable<SharpMail>> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR path IN 1..1 INBOUND ALL_SHORTEST_PATH {recipient.Id} TO {sender.Id} GRAPH {DatabaseConstants.GraphMail} RETURN path.vertices[1]");

		var convertedResults = results.Select(ConvertMailQueryResult);

		return convertedResults;
	}

	public async ValueTask<IEnumerable<SharpMail>> GetAllSentMailsAsync(SharpObject id)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR v IN 1..1 INBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} RETURN v");

		var convertedResults = results.Select(ConvertMailQueryResult);

		return convertedResults;
	}

	public async ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR path IN 1..1 INBOUND ALL_SHORTEST_PATH {recipient.Id} TO {sender.Id} GRAPH {DatabaseConstants.GraphMail} RETURN path.vertices[1]");

		var convertedResults = results.Select(ConvertMailQueryResult).Skip(mail).Take(1);

		return convertedResults.FirstOrDefault();
	}

	public async ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id)
	{
		var results = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} RETURN DISTINCT(v.Folder)");
		return results.ToArray();
	}

	public async ValueTask<IEnumerable<SharpMail>> GetAllIncomingMailsAsync(SharpPlayer id)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} RETURN v");
		return results.Select(ConvertMailQueryResult);
	}

	private SharpMail ConvertMailQueryResult(SharpMailQueryResult x)
		=> new()
		{
			Id = x.Id,
			DateSent = DateTimeOffset.FromUnixTimeMilliseconds(x.DateSent),
			Content = MarkupStringModule.deserialize(x.Content),
			Subject = MarkupStringModule.deserialize(x.Subject),
			Folder = x.Folder,
			Cleared = x.Cleared,
			Fresh = x.Fresh,
			Read = x.Read,
			Forwarded = x.Forwarded,
			Tagged = x.Tagged,
			Urgent = x.Urgent,
			From = new AsyncLazy<AnyOptionalSharpObject>(async _ =>
				await MailFromAsync(x.Id))
		};

	public async ValueTask<IEnumerable<SharpMail>> GetIncomingMailsAsync(SharpPlayer id, string folder)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} FILTER v.Folder == {folder} RETURN v");

		return results.Select(ConvertMailQueryResult);
	}

	private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string id)
	{
		var edges = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphMail} RETURN e");

		return edges switch
		{
			null or [] => new None(),
			[var edge, ..] => await GetObjectNodeAsync(edge.From)
		};
	}

	public async ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction()
		{
			Collections = new ArangoTransactionScope
			{
				Exclusive = [DatabaseConstants.Mails, DatabaseConstants.ReceivedMail, DatabaseConstants.SenderOfMail],
			}
		});

		var newMail = new SharpMailCreateRequest(
			DateSent: mail.DateSent.ToUnixTimeMilliseconds(),
			Content: MarkupStringModule.serialize(mail.Content),
			Subject: MarkupStringModule.serialize(mail.Subject),
			Folder: mail.Folder,
			Fresh: mail.Fresh,
			Read: mail.Read,
			Cleared: mail.Cleared,
			Forwarded: mail.Forwarded,
			Tagged: mail.Tagged,
			Urgent: mail.Urgent
		);

		var mailResult = await arangoDb.Graph.Vertex.CreateAsync<SharpMailCreateRequest, SharpMailQueryResult>(transaction,
			DatabaseConstants.GraphMail, DatabaseConstants.Mails, newMail);
		var id = mailResult.Vertex.Id;

		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphMail, DatabaseConstants.ReceivedMail,
			new SharpEdgeCreateRequest(to.Id!, id));
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphMail, DatabaseConstants.SenderOfMail,
			new SharpEdgeCreateRequest(id, from.Id!));

		await arangoDb.Transaction.CommitAsync(transaction);
	}

	public async ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail)
	{
		var key = mailId.Split("/")[1];

		switch (commandMail)
		{
			case { IsReadEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Read = commandMail.AsReadEdit, Fresh = false });
				return;
			case { IsClearEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Read = commandMail.AsClearEdit });
				return;
			case { IsTaggedEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Urgent = commandMail.AsTaggedEdit });
				return;
			case { IsUrgentEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Urgent = commandMail.AsUrgentEdit });
				return;
		}
	}

	public async ValueTask DeleteMailAsync(string mailId)
	{
		var key = mailId.Split("/")[1];
		await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails, key);
	}

	public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder)
	{
		var list = await GetIncomingMailsAsync(player, folder);
		var updates = list.Select(x => new { Key = x.Id!.Split("/")[1], Folder = newFolder });
		await arangoDb.Document.UpdateManyAsync(handle, DatabaseConstants.Mails, updates);
	}

	public async ValueTask MoveMailFolderAsync(string mailId, string newFolder)
		=> await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
			mailId.Split("/")[1], new { Folder = newFolder });

	public async ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} FILTER v.Folder == {folder} LIMIT {mail},1 RETURN v");

		var convertedResults = results.Select(ConvertMailQueryResult);

		return convertedResults.FirstOrDefault();
	}

	public async Task SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data)
	{
		// Get the edge that leads to it, otherwise we will have to create one.
		var result = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {sharpObjectId} GRAPH {DatabaseConstants.GraphObjectData} RETURN v");

		var first = result.FirstOrDefault();
		if (first?.ContainsKey("_key") ?? false)
		{
			var vertexKey = (string)first!["_key"];
			await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphObjectData, DatabaseConstants.ObjectData,
				vertexKey, new Dictionary<string, object> { { dataType, data } });
			return;
		}

		var newJson = new Dictionary<string, object> { { dataType, data } };

		var newVertex = await arangoDb.Graph.Vertex.CreateAsync<dynamic, dynamic>(handle,
			DatabaseConstants.GraphObjectData,
			DatabaseConstants.ObjectData,
			newJson);

		await arangoDb.Graph.Edge.CreateAsync(handle,
			DatabaseConstants.GraphObjectData,
			DatabaseConstants.HasObjectData, new SharpEdgeCreateRequest(
				From: sharpObjectId,
				To: (string)newVertex.Vertex._id)
		);
	}

	public async ValueTask<string?> GetExpandedObjectData(string sharpObjectId, string dataType)
	{
		// Get the edge that leads to it, otherwise we will have to create one.
		var result = await arangoDb.Query.ExecuteAsync<JObject>(handle,
			$"FOR v IN 1..1 OUTBOUND {sharpObjectId} GRAPH {DatabaseConstants.GraphObjectData} RETURN v");
		var resultingValue = result.FirstOrDefault()?.GetValue(dataType);
		return resultingValue?.ToString(Formatting.None);
	}

	public async ValueTask<IEnumerable<SharpChannel>> GetAllChannelsAsync()
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpChannelQueryResult>(
			handle, "FOR v IN @@C RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C", DatabaseConstants.Channels }
			});
		return result.Select(SharpChannelQueryToSharpChannel);
	}

	private async ValueTask<SharpPlayer> GetChannelOwnerAsync(string channelId)
	{
		var vertexes = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {channelId} GRAPH {DatabaseConstants.GraphChannels} RETURN v._id");
		var vertex = vertexes.First();
		var owner = await GetObjectNodeAsync(vertex);
		return owner.AsPlayer;
	}

	private async ValueTask<IEnumerable<(AnySharpObject Member, SharpChannelStatus Status)>> GetChannelMembersAsync(
		string channelId)
	{
		var vertexes = await arangoDb.Query.ExecuteAsync<(string Id, SharpChannelUserStatusQueryResult Status)>(handle,
			$"FOR v IN 1..1 INBOUND {channelId} GRAPH {DatabaseConstants.GraphChannels} RETURN {{Id: v._id, Status: e}}");

		return await AsyncEnumerable.ToAsyncEnumerable(vertexes)
			.SelectAwait(async x =>
				((
						await GetObjectNodeAsync(x.Id)).Known(),
					new SharpChannelStatus(
						Combine: x.Status.Combine,
						Gagged: x.Status.Gagged,
						Hide: x.Status.Hide,
						Mute: x.Status.Mute,
						Title: MarkupStringModule.deserialize(x.Status.Title)
					)))
			.ToArrayAsync(CancellationToken.None);
	}

	private SharpChannel SharpChannelQueryToSharpChannel(SharpChannelQueryResult x)
	{
		return new SharpChannel
		{
			Id = x.Id,
			Name = MarkupStringModule.deserialize(x.Name),
			Description = MarkupStringModule.deserialize(x.Description),
			Privs = x.Privs,
			JoinLock = x.JoinLock,
			SpeakLock = x.SpeakLock,
			SeeLock = x.SeeLock,
			HideLock = x.HideLock,
			ModLock = x.ModLock,
			Owner = new AsyncLazy<SharpPlayer>(async _ => await GetChannelOwnerAsync(x.Id)),
			Members = new AsyncLazy<IEnumerable<(AnySharpObject, SharpChannelStatus)>>(async _ =>
				await GetChannelMembersAsync(x.Id)),
			Mogrifier = x.Mogrifier,
			Buffer = x.Buffer
		};
	}

	public async ValueTask<SharpChannel?> GetChannelAsync(string name)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpChannelQueryResult>(
			handle,
			$"FOR v IN @@c FILTER v.Name = {name} RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Channels }
			});
		return result?.Select(SharpChannelQueryToSharpChannel).FirstOrDefault();
	}

	public async ValueTask<IEnumerable<SharpChannel>> GetMemberChannelsAsync(AnySharpObject obj)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpChannelQueryResult>(handle,
			$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.OnChannel} RETURN v",
			new Dictionary<string, object> { { StartVertex, obj.Object().Id! } });
		return result.Select(SharpChannelQueryToSharpChannel);
	}

	public async ValueTask CreateChannelAsync(MarkupString.MarkupStringModule.MarkupString channel, string[] privs,
		SharpPlayer owner)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive = [DatabaseConstants.Channels, DatabaseConstants.OwnerOfChannel, DatabaseConstants.OnChannel]
				}
			}
		);

		var newChannel = new SharpChannelCreateRequest(
			Name: MarkupStringModule.serialize(channel),
			Privs: privs
		);

		var createdChannel = await arangoDb.Graph.Vertex.CreateAsync<SharpChannelCreateRequest, SharpChannelQueryResult>(
			transaction, DatabaseConstants.GraphChannels, DatabaseConstants.Channels, newChannel);

		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphChannels,
			DatabaseConstants.OwnerOfChannel,
			new SharpEdgeCreateRequest(createdChannel.New.Id, owner.Id!));
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
			new SharpEdgeCreateRequest(owner.Id!, createdChannel.New.Id));

		await arangoDb.Transaction.CommitAsync(transaction);
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel, MarkupString.MarkupStringModule.MarkupString? name,
		MarkupString.MarkupStringModule.MarkupString? description, string[]? privs,
		string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock, string? mogrifier,
		int? buffer)
		=> await arangoDb.Graph.Vertex.UpdateAsync(handle,
			DatabaseConstants.GraphChannels, DatabaseConstants.Channels, channel.Id,
			new
			{
				Name = name is not null
					? MarkupStringModule.serialize(name)
					: MarkupStringModule.serialize(channel.Name),
				Description = description is not null
					? MarkupStringModule.serialize(description)
					: MarkupStringModule.serialize(channel.Description),
				Privs = privs ?? channel.Privs,
				JoinLock = joinLock ?? channel.JoinLock,
				SpeakLock = speakLock ?? channel.SpeakLock,
				SeeLock = seeLock ?? channel.SeeLock,
				HideLock = hideLock ?? channel.HideLock,
				ModLock = modLock ?? channel.ModLock,
				Buffer = buffer ?? channel.Buffer,
				Mogrifier = mogrifier ?? channel.Mogrifier
			});

	public async ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.OwnerOfChannel} RETURN e._id",
			new Dictionary<string, object> { { StartVertex, channel.Id! } });
		var ownerEdge = response.First();
		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OwnerOfChannel,
			ownerEdge, new { To = newOwner.Id });
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel) =>
		await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.Channels,
			channel.Id);

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj) =>
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
			new SharpEdgeCreateRequest(channel.Id!, obj.Object().Id!));

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.OnChannel} RETURN v",
			new Dictionary<string, object> { { StartVertex, obj.Object().Id! } }
		);

		var singleResult = result?.FirstOrDefault();
		if (singleResult is null) return;

		await arangoDb.Graph.Edge.RemoveAsync<dynamic>(handle,
			DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
			singleResult.Key);
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj,
		SharpChannelStatus status)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.OnChannel} RETURN v",
			new Dictionary<string, object> { { StartVertex, obj.Object().Id! } }
		);

		var singleResult = result?.FirstOrDefault();
		if (singleResult is null) return;

		var updates = new List<KeyValuePair<string, object>>();
		if (status.Combine is { } combine)
		{
			updates.Add(new KeyValuePair<string, object>(nameof(status.Combine), combine));
		}

		if (status.Gagged is { } gagged)
		{
			updates.Add(new KeyValuePair<string, object>(nameof(status.Gagged), gagged));
		}

		if (status.Hide is { } hide)
		{
			updates.Add(new KeyValuePair<string, object>(nameof(status.Hide), hide));
		}

		if (status.Mute is { } mute)
		{
			updates.Add(new KeyValuePair<string, object>(nameof(status.Mute), mute));
		}

		if (status.Title is { } title)
		{
			updates.Add(new KeyValuePair<string, object>(nameof(status.Title), MarkupStringModule.serialize(title)));
		}

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
			singleResult.Key, updates);
	}

	private SharpObjectFlag SharpObjectFlagQueryToSharpChannel(SharpObjectFlagQueryResult x)
	{
		return new SharpObjectFlag
		{
			Id = x.Id,
			Name = x.Name,
			Symbol = x.Symbol,
			System = x.system, 
			SetPermissions = x.SetPermissions,
			UnsetPermissions = x.UnsetPermissions,
			Aliases = x.Aliases,
			TypeRestrictions = x.TypeRestrictions
		};
	}
	
	private async ValueTask<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync(string id)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpAttributeFlagQueryResult>(handle,
			$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributeFlags} RETURN v",
			new Dictionary<string, object> { { StartVertex, id } });
		return result.Select(x =>
			new SharpAttributeFlag()
			{
				Name = x.Name,
				Symbol = x.Symbol,
				System = x.System,
				Inheritable = x.Inheritable,
				Id = x.Id
			});
	}

	private async ValueTask<IEnumerable<SharpAttribute>> GetAllAttributesAsync(string id)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } });
		}
		else
		{
			sharpAttributeResults = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..999 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { "startVertex", id } });
		}

		var sharpAttributes = sharpAttributeResults.Select(async x =>
			new SharpAttribute(
				Key: x.Key,
				Name: x.Name,
				Flags: await GetAttributeFlagsAsync(x.Id),
				CommandListIndex: null,
				LongName: x.LongName,
				Leaves: new(async ct => await GetTopLevelAttributesAsync(x.Id)),
				Owner: new(async ct => await GetAttributeOwnerAsync(x.Id)),
				SharpAttributeEntry: new(async ct => await Task.FromResult<SharpAttributeEntry?>(null)))
			{
				Value = MarkupStringModule.deserialize(x.Value)
			});

		return await Task.WhenAll(sharpAttributes);
	}

	private async ValueTask<SharpPlayer> GetObjectOwnerAsync(string id)
	{
		var owner = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphObjectOwners} RETURN v._id")).First();

		var populatedOwner = await GetObjectNodeAsync(owner);

		return populatedOwner.AsPlayer;
	}

	private async ValueTask<SharpPlayer> GetAttributeOwnerAsync(string id)
	{
		var owner = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphAttributeOwners} RETURN v._id")).First();

		var populatedOwner = await GetObjectNodeAsync(owner);

		return populatedOwner.AsPlayer;
	}

	public async ValueTask<AnyOptionalSharpObject> GetParentAsync(string id)
	{
		// TODO: Optimize
		var parentId = (await arangoDb.Query.ExecuteAsync<int>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v._key", cache: true))
			.FirstOrDefault();
		return await GetObjectNodeAsync(new DBRef(parentId));
	}

	private async ValueTask<IEnumerable<SharpObject?>> GetChildrenAsync(string id)
		=> await arangoDb.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1..1 INBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v", cache: true) ?? [];

	public async ValueTask<IEnumerable<SharpObject>> GetParentsAsync(string id)
		=> await arangoDb.Query.ExecuteAsync<SharpObject>(handle,
			$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v", cache: true);

	private async ValueTask<AnySharpContainer> GetHomeAsync(string id)
	{
		var homeId = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphHomes} RETURN v._id", cache: true)).First();
		var homeObject = await GetObjectNodeAsync(homeId);

		return homeObject.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));
	}

	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDb.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.Objects,
			dbref.Number.ToString());

		if (obj is null) return new None();
		if (dbref.CreationMilliseconds is not null && obj.CreationTime != dbref.CreationMilliseconds) return new None();

		var startVertex = obj.Id;
		var res = (await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.GraphObjects} RETURN v", cache: true))
			.FirstOrDefault();

		if (res is null) return new None();

		var id = res.Id;

		var convertObject = new SharpObject
		{
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = (obj.Locks ?? []).ToImmutableDictionary(),
			Id = obj.Id,
			Key = int.Parse(obj.Key),
			Flags = new(async ct => await mediator.Send(new GetObjectFlagsQuery(startVertex), ct) ?? []),
			Powers = new(async ct => await GetPowersAsync(startVertex)),
			Attributes = new(async ct => await GetTopLevelAttributesAsync(startVertex)),
			AllAttributes = new(async ct => await GetAllAttributesAsync(startVertex)),
			Owner = new(async ct => await GetObjectOwnerAsync(startVertex)),
			Parent = new(async ct => await GetParentAsync(startVertex)),
			Children = new(async ct => (await GetChildrenAsync(startVertex))!)
		};

		return obj.Type switch
		{
			DatabaseConstants.TypeThing => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id))
			},
			DatabaseConstants.TypePlayer => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id)),
				PasswordHash = res.PasswordHash
			},
			DatabaseConstants.TypeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.TypeExit => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'")
		};
	}

	private async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(string dbId)
	{
		ArangoList<dynamic>? query;
		if (dbId.StartsWith(DatabaseConstants.Objects))
		{
			query = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 INBOUND {dbId} GRAPH {DatabaseConstants.GraphObjects} RETURN v",
				cache: true);
			query.Reverse();
		}
		else
		{
			query = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 OUTBOUND {dbId} GRAPH {DatabaseConstants.GraphObjects} RETURN v", cache: true);
		}

		var res = query.First();
		var obj = query.Last();

		string id = res._id;
		string objId = obj._id;
		var collection = id.Split("/")[0];
		var convertObject = new SharpObject
		{
			Id = objId,
			Key = int.Parse((string)obj._key),
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = ImmutableDictionary<string, string>
				.Empty, // FIX: ((Dictionary<string, string>?)obj.Locks ?? []).ToImmutableDictionary(),
			Flags = new(async ct => await GetObjectFlagsAsync(objId)),
			Powers = new(async ct => await GetPowersAsync(objId)),
			Attributes = new(async ct => await GetTopLevelAttributesAsync(objId)),
			AllAttributes = new(async ct => await GetAllAttributesAsync(objId)),
			Owner = new(async ct => await GetObjectOwnerAsync(objId)),
			Parent = new(async ct => await GetParentAsync(objId)),
			Children = new(async ct => (await GetChildrenAsync(objId))!)
		};

		return collection switch
		{
			DatabaseConstants.Things => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id))
			},
			DatabaseConstants.Players => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id)), PasswordHash = res.PasswordHash
			},
			DatabaseConstants.Rooms => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.Exits => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref)
	{
		var obj = await arangoDb.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.Objects,
			dbref.Number.ToString());

		if (dbref.CreationMilliseconds.HasValue && obj.CreationTime != dbref.CreationMilliseconds)
		{
			return null;
		}

		return obj is null
			? null
			: new SharpObject()
			{
				Name = obj.Name,
				Type = obj.Type,
				Id = obj.Id,
				Key = int.Parse(obj.Key),
				Locks = (obj.Locks ?? []).ToImmutableDictionary(),
				CreationTime = obj.CreationTime,
				ModifiedTime = obj.ModifiedTime,
				Flags = new(async ct => await GetObjectFlagsAsync(obj.Id)),
				Powers = new(async ct => await GetPowersAsync(obj.Id)),
				Attributes = new(async ct => await GetTopLevelAttributesAsync(obj.Id)),
				AllAttributes = new(async ct => await GetAllAttributesAsync(obj.Id)),
				Owner = new(async ct => await GetObjectOwnerAsync(obj.Id)),
				Parent = new(async ct => await GetParentAsync(obj.Id)),
				Children = new(async ct => (await GetChildrenAsync(obj.Id))!)
			};
	}

	private async ValueTask<IEnumerable<SharpAttribute>> GetTopLevelAttributesAsync(string id)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } });
		}
		else
		{
			sharpAttributeResults = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } });
		}

		var sharpAttributes = sharpAttributeResults.Select(async x =>
			new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName,
				new(async ct => await GetTopLevelAttributesAsync(x.Id)),
				new(async ct => await GetAttributeOwnerAsync(x.Id)),
				new(async ct => await Task.FromResult<SharpAttributeEntry?>(null)))
			{
				Value = MarkupStringModule.deserialize(x.Value)
			});

		return await Task.WhenAll(sharpAttributes);
	}

	public async ValueTask<IEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attributePattern)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true);
		var pattern = attributePattern.Replace("_", "\\_").Replace("%", "\\%").Replace("?", "_").Replace("*", "%");

		if (!result.Any())
		{
			return null;
		}

		// TODO: This is a lazy implementation and does not appropriately support the ` section of pattern matching for attribute trees.
		// TODO: A pattern with a wildcard can match multiple levels of attributes.
		// This means it can also match attributes deeper in its structure that need to be reported on.
		// It already does this right now. But not in a sorted manner!

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName LIKE @pattern RETURN v";

		var result2 = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex },
				{ "pattern", pattern }
			});

		return await Task.WhenAll(result2.Select(async x =>
			new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName,
				new(async ct => await GetTopLevelAttributesAsync(x.Id)),
				new(async ct => await GetObjectOwnerAsync(x.Id)),
				new(async ct => await Task.FromResult<SharpAttributeEntry?>(null)))
			{
				Value = MarkupStringModule.deserialize(x.Value)
			}));
	}

	public async ValueTask<IEnumerable<SharpAttribute>?> GetAttributesRegexAsync(DBRef dbref, string attributePattern)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true);

		if (!result.Any())
		{
			return null;
		}

		// TODO: Create an Inverted Index on LongName.
		var query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
				{ StartVertex, startVertex },
				{ "pattern", attributePattern }
			});

		return await Task.WhenAll(result2.Select(async x =>
			new SharpAttribute(x.Key, x.Name, await GetAttributeFlagsAsync(x.Id), null, x.LongName,
				new(async ct => await GetTopLevelAttributesAsync(x.Id)),
				new(async ct => await GetObjectOwnerAsync(x.Id)),
				new(async ct => await Task.FromResult<SharpAttributeEntry?>(null)))
			{
				Value = MarkupStringModule.deserialize(x.Value)
			}));
	}

	public async ValueTask SetLockAsync(SharpObject target, string lockName, string lockString)
		=> await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects, new
		{
			target.Key,
			Locks = target.Locks.Add(lockName, lockString)
		}, mergeObjects: true);

	public async ValueTask<IEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, params string[] attribute)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		const string let =
			$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string query =
			$"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = await arangoDb.Query.ExecuteAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
				{ "attr", attribute.Select(x => x.ToUpper()) },
				{ StartVertex, startVertex },
				{ "max", attribute.Length }
			});

		if (result.Count < attribute.Length) return null;

		return await Task.WhenAll(result.Select(async x => new SharpAttribute(x.Key, x.Name,
			await GetAttributeFlagsAsync(x.Id), null, x.LongName,
			new(async ct => await GetTopLevelAttributesAsync(x.Id)),
			new(async ct => await GetObjectOwnerAsync(x.Id)),
			new(async ct => await Task.FromResult<SharpAttributeEntry?>(null)))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		}));
	}

	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MarkupStringModule.MarkupString value,
		SharpPlayer owner)
	{
		ArgumentException.ThrowIfNullOrEmpty(owner.Id);

		var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			AllowImplicit = false,
			Collections = new ArangoTransactionScope
			{
				Exclusive = [DatabaseConstants.Attributes, DatabaseConstants.HasAttribute, DatabaseConstants.HasAttributeOwner],
				Read =
				[
					DatabaseConstants.Attributes, DatabaseConstants.HasAttribute, DatabaseConstants.Objects,
					DatabaseConstants.IsObject, DatabaseConstants.Players, DatabaseConstants.Rooms, DatabaseConstants.Things,
					DatabaseConstants.Exits
				]
			}
		});

		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		const string let1 =
			$"LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string let2 =
			$"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v)";
		const string query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

		var result = await arangoDb.Query.ExecuteAsync<dynamic[]>(handle, query, new Dictionary<string, object>
		{
			{ "attr", attribute.Select(x => x.ToUpper()) },
			{ StartVertex, startVertex },
			{ "max", attribute.Length }
		});

		var actualResult = result.First();

		var matches = actualResult.Length;
		var remaining = attribute.Skip(matches - 1).ToArray();
		var last = actualResult.Last();
		string lastId = last._id;

		// Create Path
		foreach (var nextAttr in remaining.Select((attrName, i) => (value: attrName, i)))
		{
			var newOne = await arangoDb.Document.CreateAsync<SharpAttributeCreateRequest, SharpAttributeQueryResult>(
				transactionHandle, DatabaseConstants.Attributes,
				new SharpAttributeCreateRequest(nextAttr.value.ToUpper(), [],
					nextAttr.i == remaining.Length - 1
						? MarkupStringModule.serialize(value)
						: string.Empty,
					string.Join('`', attribute.SkipLast(remaining.Length - 1 - nextAttr.i).Select(x => x.ToUpper()))),
				waitForSync: true);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.HasAttribute,
				new SharpEdgeCreateRequest(lastId, newOne.Id), waitForSync: true);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributeOwners,
				DatabaseConstants.HasAttributeOwner,
				new SharpEdgeCreateRequest(newOne.Id, owner.Id!), waitForSync: true);

			lastId = newOne.Id;
		}

		// Update Path
		if (remaining.Length == 0)
		{
			await arangoDb.Document.UpdateAsync(transactionHandle, DatabaseConstants.Attributes,
				new { Key = lastId.Split('/')[1], Value = MarkupStringModule.serialize(value) }, waitForSync: true,
				mergeObjects: true);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributeOwners,
				DatabaseConstants.HasAttributeOwner,
				new SharpEdgeCreateRequest(lastId, owner.Id!), waitForSync: true);
		}

		await arangoDb.Transaction.CommitAsync(transactionHandle);

		return true;
	}

	public async ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag)
	{
		var attrInfo = await GetAttributeAsync(dbref.DBRef, attribute);
		if (attrInfo is null) return false;
		var attr = attrInfo.Last();

		await SetAttributeFlagAsync(attr, flag);
		return true;
	}

	public async ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag) =>
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Attributes, new
		{
			attr.Key,
			Value = attr.Flags.ToImmutableArray().Add(flag)
		});

	public async ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag)
	{
		var attrInfo = await GetAttributeAsync(dbref.DBRef, attribute);
		if (attrInfo is null) return false;
		var attr = attrInfo.Last();

		await UnsetAttributeFlagAsync(attr, flag);
		return true;
	}

	public async ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag) =>
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Attributes, new
		{
			attr.Key,
			Value = attr.Flags.ToImmutableArray().Remove(flag)
		});

	public async ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName) =>
		(await arangoDb.Query.ExecuteAsync<SharpAttributeFlag>(handle,
			"FOR v in @@C1 FILTER v.Name == @flag RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C1", DatabaseConstants.AttributeFlags },
				{ "flag", flagName }
			}, cache: true)).FirstOrDefault();

	public async ValueTask<IEnumerable<SharpAttributeFlag>> GetAttributeFlagsAsync() =>
		await arangoDb.Query.ExecuteAsync<SharpAttributeFlag>(handle,
			$"FOR v in {DatabaseConstants.AttributeFlags:@} RETURN v",
			cache: true);

	public ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute)
	{
		// Set the contents to empty.

		throw new NotImplementedException();
	}

	public ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute)
	{
		// Wipe a list of attributes. We assume the calling code figured out the permissions part.

		throw new NotImplementedException();
	}

	public async ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj)
	{
		var self = (await GetObjectNodeAsync(obj)).WithoutNone();
		var location = await self.Where();

		return
		[
			self,
			.. (await GetContentsAsync(self.Object().DBRef))!.Select(x => x.WithRoomOption()),
			.. (await GetContentsAsync(location.Object().DBRef))!.Select(x => x.WithRoomOption()),
		];
	}

	public async ValueTask<IEnumerable<AnySharpObject>> GetNearbyObjectsAsync(AnySharpObject obj)
	{
		var location = await obj.Where();

		return
		[
			obj,
			.. (await GetContentsAsync(obj.Object().DBRef))!.Select(x => x.WithRoomOption()),
			.. (await GetContentsAsync(location.Object().DBRef))!.Select(x => x.WithRoomOption()),
		];
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="obj">Location</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsNone) return new None();

		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ StartVertex, baseObject.Id()! }
		});
		var locationBaseObj = await GetObjectNodeAsync(query.Last());
		var trueLocation = locationBaseObj.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="id">Location ID</param>
	/// <param name="depth">Depth</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1)
	{
		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ StartVertex, id }
		});
		var locationBaseObj = await GetObjectNodeAsync(query.Last());
		var trueLocation = locationBaseObj.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1) =>
		(await GetLocationAsync(obj.Object().DBRef, depth)).WithoutNone();

	public async ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj)
	{
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsNone) return null;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ StartVertex, baseObject.Object()!.Id! }
			});
		var result = query
			.Select(x => x)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(async x => (await x).Match<AnySharpContent>(
				player => player,
				_ => throw new Exception("Invalid Contents found"),
				exit => exit,
				thing => thing,
				_ => throw new Exception("Invalid Contents found")
			));

		return await Task.WhenAll(result);
	}

	public async ValueTask<IEnumerable<AnySharpContent>?> GetContentsAsync(AnySharpContainer node)
	{
		var startVertex = node.Id;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v";
		var query = await arangoDb.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex }
			});

		var ids = query.Select(x => (string)x._id).ToArray();
		var objects = await Task.WhenAll(ids.Select(async x => await GetObjectNodeAsync(x)));

		var result = objects.Select(x => x.Match<AnySharpContent>(
			player => player,
			_ => throw new Exception("Invalid Contents found"),
			exit => exit,
			thing => thing,
			_ => throw new Exception("Invalid Contents found")
		));

		return result;
	}

	public async ValueTask<IEnumerable<SharpExit>?> GetExitsAsync(DBRef obj)
	{
		// This is bad code. We can't use graphExits for this.
		var baseObject = await GetObjectNodeAsync(obj);
		if (baseObject.IsNone) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphExits} RETURN v";
		var query = await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, exitQuery,
			new Dictionary<string, object>
			{
				{ StartVertex, baseObject.Known().Id()! }
			});
		var result = query
			.Select(x => x.Id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(async x => (await x).Match(
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found"),
				exit => exit,
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found")
			));

		return await Task.WhenAll(result);
	}

	public async ValueTask<IEnumerable<SharpExit>> GetExitsAsync(AnySharpContainer node)
	{
		// This is bad code. We can't use graphExits for this.
		var startVertex = node.Id;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphExits} RETURN v";
		var query = await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, exitQuery,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex }
			});
		var result = query
			.Select(x => x.Id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(async x => (await x).Match(
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found"),
				exit => exit,
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found")
			));

		return await Task.WhenAll(result);
	}

	public async ValueTask<IEnumerable<SharpPlayer>> GetPlayerByNameAsync(string name)
	{
		// TODO: Look up by Alias.
		var query = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN {DatabaseConstants.Objects} FILTER v.Type == @type && v.Name == @name RETURN v._id",
			bindVars: new Dictionary<string, object>
			{
				{ "name", name },
				{ "type", DatabaseConstants.TypePlayer }
			});

		// TODO: Edit to return multiple players and let the above layer figure out which one it wants.
		var result = query.FirstOrDefault();
		if (result is null) return [];

		return await Task.WhenAll(query.Select(GetObjectNodeAsync).Select(async x => (await x).AsPlayer));
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination)
	{
		var edge = (await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
				$"FOR v,e IN 1..1 OUTBOUND {enactorObj.Id} GRAPH {DatabaseConstants.GraphLocations} RETURN e"))
			.Single();

		await arangoDb.Graph.Edge.UpdateAsync(handle,
			DatabaseConstants.GraphLocations,
			DatabaseConstants.AtLocation,
			edge.Key,
			new
			{
				From = enactorObj.Id,
				To = destination.Id
			},
			waitForSync: true);
	}

	ValueTask<bool> ISharpDatabase.UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag)
	{
		throw new NotImplementedException();
	}

	public async ValueTask SetupLogging()
	{
		await ValueTask.CompletedTask;
	}
}