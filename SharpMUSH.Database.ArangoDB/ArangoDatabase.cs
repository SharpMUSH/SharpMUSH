using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using DotNext.Threading;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

// TODO: Unit of Work / Transaction around all of this! Otherwise it risks the stability of the Database.
public partial class ArangoDatabase(
	ILogger<ArangoDatabase> logger,
	IArangoContext arangoDb,
	ArangoHandle handle,
	IMediator mediator,
	IPasswordService passwordService // TODO: This doesn't belong in the database layer
) : ISharpDatabase, ISharpDatabaseWithLogging
{
	private const string StartVertex = "startVertex";

	public async ValueTask Migrate(CancellationToken ct = default)
	{
		try
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
		catch (Exception ex)
		{
			logger.LogError(ex, "Migration Failed. Check details for further information.");
			throw;
		}
	}

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location,
		CancellationToken ct = default)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var objectLocation = await GetObjectNodeAsync(location, ct);

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

		var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, transaction, ct);

		var obj = await arangoDb.Graph.Vertex.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(
			transactionHandle, DatabaseConstants.GraphObjects,
			DatabaseConstants.Objects, new SharpObjectCreateRequest(
				name,
				DatabaseConstants.TypePlayer,
				[],
				time,
				time
			), returnNew: true, cancellationToken: ct);

		var hashedPassword = passwordService.HashPassword($"#{obj.New.Key}:{obj.New.CreationTime}", password);

		var playerResult = await arangoDb.Document.CreateAsync<SharpPlayerCreateRequest, SharpPlayerQueryResult>(
			transactionHandle,
			DatabaseConstants.Players,
			new SharpPlayerCreateRequest([], hashedPassword), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(playerResult.Id, obj.New.Id), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphObjectOwners,
			DatabaseConstants.HasObjectOwner, new SharpEdgeCreateRequest(playerResult.Id, playerResult.Id),
			cancellationToken: ct);

		var idx = objectLocation.Match(
			player => player.Id,
			room => room.Id,
			_ => throw new ArgumentException("An Exit is not a valid location to create a player!"),
			thing => thing.Id,
			_ => throw new ArgumentException("A player must have a valid creation location!"));

		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphLocations,
			DatabaseConstants.AtLocation, new SharpEdgeCreateRequest(playerResult.Id, idx!), cancellationToken: ct);

		// TODO: This should use a Default Home, which should be passed down from above.
		await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(playerResult.Id, idx!), cancellationToken: ct);

		await arangoDb.Transaction.CommitAsync(transactionHandle, ct);

		return new DBRef(int.Parse(obj.New.Key), time);
	}

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken ct = default)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeRoom, [], time, time), cancellationToken: ct);
		var room = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Rooms, new SharpRoomCreateRequest(),
			cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(room.Id, obj.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator,
		CancellationToken ct = default)
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
			}, ct);
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(transaction,
			DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeThing, [], time, time), cancellationToken: ct);
		var thing = await arangoDb.Document.CreateAsync(transaction, DatabaseConstants.Things,
			new SharpThingCreateRequest([]), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(thing.Id, obj.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			new SharpEdgeCreateRequest(thing.Id, location.Id), cancellationToken: ct);
		// TODO: Fix, this should use a default home location, passed down to this.
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(thing.Id, location.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphObjectOwners,
			DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

		await arangoDb.Transaction.CommitAsync(transaction, ct);
		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken ct = default)
	{
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			new SharpEdgeCreateRequest(exit.Id!, location.Id), cancellationToken: ct);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v ,eIN 1..1 INBOUND {exit.Id} GRAPH {DatabaseConstants.GraphHomes} RETURN e", cancellationToken: ct);

		if (!result.Any())
		{
			return false;
		}

		await arangoDb.Graph.Edge.RemoveAsync<object>(handle,
			DatabaseConstants.GraphHomes, DatabaseConstants.HasHome, result.First().Key, cancellationToken: ct);

		return true;
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
		SharpPlayer creator, CancellationToken ct = default)
	{
		var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var obj = await arangoDb.Document.CreateAsync<SharpObjectCreateRequest, SharpObjectQueryResult>(handle,
			DatabaseConstants.Objects,
			new SharpObjectCreateRequest(name, DatabaseConstants.TypeExit, [], time, time), cancellationToken: ct);
		var exit = await arangoDb.Document.CreateAsync(handle, DatabaseConstants.Exits,
			new SharpExitCreateRequest(aliases), cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjects, DatabaseConstants.IsObject,
			new SharpEdgeCreateRequest(exit.Id, obj.Id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			new SharpEdgeCreateRequest(exit.Id, location.Id), cancellationToken: ct);
		/* await arangoDB.Graph.Edge.CreateAsync(handle, DatabaseConstants.graphHomes, DatabaseConstants.hasHome,
			new SharpEdgeCreateRequest(exit.Id, location.Id)); */
		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			new SharpEdgeCreateRequest(obj.Id, creator.Id!), cancellationToken: ct);

		return new DBRef(int.Parse(obj.Key), time);
	}

	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken ct = default)
		=> await arangoDb.Query.ExecuteStreamAsync<SharpObjectFlagQueryResult>(
				handle,
				$"FOR v in @@C1 FILTER v.Name == @flag RETURN v",
				bindVars: new Dictionary<string, object>
				{
					{ "@C1", DatabaseConstants.ObjectFlags },
					{ "flag", name }
				},
				cache: true, cancellationToken: ct)
			.Select(SharpObjectFlagQueryToSharpFlag)
			.FirstOrDefaultAsync(cancellationToken: ct);

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObjectFlagQueryResult>(
				handle,
				$"FOR v in {DatabaseConstants.ObjectFlags:@} RETURN v",
				cache: true, cancellationToken: ct)
			.Select(SharpObjectFlagQueryToSharpFlag);

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpPowerQueryResult>(
				handle,
				$"FOR v in {DatabaseConstants.ObjectPowers:@} RETURN v",
				cache: true, cancellationToken: ct)
			.Select(SharpPowerQueryToSharpPower);

	private static SharpPower SharpPowerQueryToSharpPower(SharpPowerQueryResult arg) =>
		new()
		{
			Id = arg.Id,
			Alias = arg.Alias,
			Name = arg.Name,
			System = arg.System,
			SetPermissions = arg.SetPermissions,
			UnsetPermissions = arg.UnsetPermissions,
			TypeRestrictions = arg.TypeRestrictions
		};

	private async ValueTask<string?> GetObjectFlagEdge(AnySharpObject target, SharpObjectFlag flag,
		CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {target.Object().Id} GRAPH {DatabaseConstants.GraphFlags} FILTER v._id == {flag.Id} RETURN e._id",
			cancellationToken: ct);
		return result.FirstOrDefault()?.Id;
	}

	private async ValueTask<string?> GetObjectPowerEdge(AnySharpObject target, SharpPower flag,
		CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {target.Object().Id} GRAPH {DatabaseConstants.GraphPowers} FILTER v._id == {flag.Id} RETURN e._id",
			cancellationToken: ct);
		return result.FirstOrDefault()?.Id;
	}

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag,
		CancellationToken ct = default)
	{
		var edge = await GetObjectFlagEdge(target, flag, ct);
		if (edge is not null) return false;

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphFlags, DatabaseConstants.HasFlags,
			new SharpEdgeCreateRequest(target.Object().Id!, flag.Id!), cancellationToken: ct);

		return true;
	}

	public async ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power,
		CancellationToken ct = default)
	{
		var edge = await GetObjectPowerEdge(dbref, power, ct);
		if (edge is not null) return false;

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphPowers, DatabaseConstants.HasPowers,
			new SharpEdgeCreateRequest(dbref.Object().Id!, power.Id!), cancellationToken: ct);

		return true;
	}

	public async ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power,
		CancellationToken ct = default)
	{
		var edge = await GetObjectPowerEdge(dbref, power, ct);
		if (edge is null) return false;

		await arangoDb.Graph.Edge.RemoveAsync<string>(handle, DatabaseConstants.GraphPowers, DatabaseConstants.HasPowers,
			edge, cancellationToken: ct);

		return true;
	}

	public async ValueTask SetObjectName(AnySharpObject obj, MarkupStringModule.MarkupString value,
		CancellationToken ct = default)
		=> await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects,
			new
			{
				Id = obj.Id(),
				Name = value
			}, cancellationToken: ct);

	public async ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphHomes} RETURN e._id",
			new Dictionary<string, object> { { StartVertex, obj.Id } }, cancellationToken: ct);

		var contentEdge = response.First();

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphHomes, DatabaseConstants.HasHome,
			contentEdge, new { To = home.Id }, cancellationToken: ct);
	}

	public async ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location,
		CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN e._id",
			new Dictionary<string, object> { { StartVertex, obj.Id } }, cancellationToken: ct);

		var contentEdge = response.First();

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphLocations, DatabaseConstants.AtLocation,
			contentEdge, new { To = location.Id }, cancellationToken: ct);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphParents} RETURN e._id",
			new Dictionary<string, object> { { StartVertex, obj.Id()! } }, cancellationToken: ct);

		var contentEdge = response.FirstOrDefault();

		if (contentEdge is null && parent is null)
		{
			return;
		}

		if (contentEdge is null && parent != null)
		{
			await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphParents, DatabaseConstants.HasParent,
				new { To = parent.Id() }, cancellationToken: ct);
		}
		else if (parent is null)
		{
			await arangoDb.Graph.Edge.RemoveAsync<object>(handle, DatabaseConstants.GraphParents, DatabaseConstants.HasParent,
				contentEdge, cancellationToken: ct);
		}
		else
		{
			await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphParents, DatabaseConstants.HasParent,
				contentEdge, new { To = parent.Id() }, cancellationToken: ct);
		}
	}

	// Todo: Separate SET and UNSET better.
	public async ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken ct = default)
		=> await SetObjectParent(obj, null, ct);

	public async ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphObjectOwners} RETURN e._id",
			new Dictionary<string, object> { { StartVertex, obj.Id()! } }, cancellationToken: ct);

		var contentEdge = response.First();

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphObjectOwners, DatabaseConstants.HasObjectOwner,
			contentEdge, new { To = owner.Id }, cancellationToken: ct);
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag,
		CancellationToken ct = default)
	{
		var edge = await GetObjectFlagEdge(target, flag, ct);
		if (edge is null) return false;

		await arangoDb.Graph.Edge.RemoveAsync<string>(handle, DatabaseConstants.GraphFlags, DatabaseConstants.HasFlags,
			edge, cancellationToken: ct);

		return true;
	}

	private IAsyncEnumerable<SharpPower> GetPowersAsync(string id, CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpPowerQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphPowers} RETURN v", cancellationToken: ct)
			.Select(SharpPowerQueryToSharpPower);

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(string id, string type, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObjectFlagQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphFlags} RETURN v", cancellationToken: ct)
			.Select(SharpObjectFlagQueryToSharpFlag)
			.Append(new SharpObjectFlag()
			{
				Name = type,
				SetPermissions = [],
				TypeRestrictions = [],
				Symbol = type[0].ToString(),
				System = true,
				UnsetPermissions = [],
				Id = null,
				Aliases = []
			});

	// TODO: Fix this.
	public async ValueTask<IAsyncEnumerable<SharpObject>> GetParentsAsync(string id, CancellationToken ct = default)
		=> (await arangoDb.Query.ExecuteAsync<SharpObject>(handle,
				$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v", cache: true,
				cancellationToken: ct))
			.ToAsyncEnumerable();

	public IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient,
		CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpMailQueryResult>(handle,
				$"FOR path IN 1..1 INBOUND ALL_SHORTEST_PATH {recipient.Id} TO {sender.Id} GRAPH {DatabaseConstants.GraphMail} RETURN path.vertices[1]",
				cancellationToken: ct)
			.Select(ConvertMailQueryResult);

	public IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject id, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpMailQueryResult>(handle,
				$"FOR v IN 1..1 INBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} RETURN v", cancellationToken: ct)
			.Select(ConvertMailQueryResult);

	public async ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail,
		CancellationToken ct = default)
		=> await arangoDb.Query.ExecuteStreamAsync<SharpMailQueryResult>(handle,
				$"FOR path IN 1..1 INBOUND ALL_SHORTEST_PATH {recipient.Id} TO {sender.Id} GRAPH {DatabaseConstants.GraphMail} RETURN path.vertices[1]",
				cancellationToken: ct)
			.Select(ConvertMailQueryResult)
			.Skip(mail)
			.Take(1)
			.FirstOrDefaultAsync(cancellationToken: ct);

	public async ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken ct = default)
	{
		var results = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} RETURN DISTINCT(v.Folder)",
			cancellationToken: ct);
		return results.ToArray();
	}

	public IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id,
		CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpMailQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} RETURN v", cancellationToken: ct)
			.Select(ConvertMailQueryResult);

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
			From = new AsyncLazy<AnyOptionalSharpObject>(async ct =>
				await MailFromAsync(x.Id, ct))
		};

	public IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder,
		CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpMailQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} FILTER v.Folder == {folder} RETURN v",
			cancellationToken: ct).Select(ConvertMailQueryResult);

	private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string id, CancellationToken ct = default)
	{
		// There is an error here. 
		/*
		 *Microsoft.CSharp.RuntimeBinder.RuntimeBinderException: Cannot convert null to 'long' because it is a non-nullable value type
   at CallSite.Target(Closure, CallSite, Object)
   at System.Dynamic.UpdateDelegates.UpdateAndExecute1[T0,TRet](CallSite site, T0 arg0)
   at CallSite.Target(Closure, CallSite, Object)
   at SharpMUSH.Database.ArangoDB.ArangoDatabase.SharpObjectQueryToSharpObject(Object obj) in D:\\SharpMUSH\\SharpMUSH.Database.ArangoDB\\ArangoDatabase.cs:line 1164
   at SharpMUSH.Database.ArangoDB.ArangoDatabase.GetObjectNodeAsync(String dbId, CancellationToken cancellationToken) in D:\\SharpMUSH\\SharpMUSH.Database.ArangoDB\\ArangoDatabase.cs:line 1136
   at SharpMUSH.Database.ArangoDB.ArangoDatabase.MailFromAsync(String id, CancellationToken ct) in D:\\SharpMUSH\\SharpMUSH.Database.ArangoDB\\ArangoDatabase.cs:line 502
   at SharpMUSH.Database.ArangoDB.ArangoDatabase.<>c__DisplayClass38_0.<<ConvertMailQueryResult>b__0>d.MoveNext() in D:\\SharpMUSH\\SharpMUSH.Database.ArangoDB\\ArangoDatabase.cs:line 486
--- End of stack trace from previous location ---
   at SharpMUSH.Implementation.Functions.Functions.mailfrom(IMUSHCodeParser parser, SharpFunctionAttribute _2) in D:\\SharpMUSH\\SharpMUSH.Implementation\\Functions\\MailFunctions.cs:line 326
   at SharpMUSH.Implementation.Visitors.SharpMUSHParserVisitor.CallFunction(String name, MarkupString src, FunctionContext context, EvaluationStringContext[] args, SharpMUSHParserVisitor visitor) in D:\\SharpMUSH\\SharpMUSH.Implementation\\Visitors\\SharpMUSHParserVisitor.cs:line 264
		 *
		 */

		var edges = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphMail} RETURN e", cancellationToken: ct);

		return edges switch
		{
			null or [] => new None(),
			[var edge, ..] => await GetObjectNodeAsync(edge.From, CancellationToken.None)
		};
	}

	public async ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken ct = default)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction()
		{
			Collections = new ArangoTransactionScope
			{
				Exclusive = [DatabaseConstants.Mails, DatabaseConstants.ReceivedMail, DatabaseConstants.SenderOfMail],
			}
		}, ct);

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
			DatabaseConstants.GraphMail, DatabaseConstants.Mails, newMail, cancellationToken: ct);
		var id = mailResult.Vertex.Id;

		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphMail, DatabaseConstants.ReceivedMail,
			new SharpEdgeCreateRequest(to.Id!, id), cancellationToken: ct);
		await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphMail, DatabaseConstants.SenderOfMail,
			new SharpEdgeCreateRequest(id, from.Id!), cancellationToken: ct);

		await arangoDb.Transaction.CommitAsync(transaction, ct);
	}

	public async ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken ct = default)
	{
		var key = mailId.Split("/")[1];

		switch (commandMail)
		{
			case { IsReadEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Read = commandMail.AsReadEdit, Fresh = false }, cancellationToken: ct);
				return;
			case { IsClearEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Read = commandMail.AsClearEdit }, cancellationToken: ct);
				return;
			case { IsTaggedEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Urgent = commandMail.AsTaggedEdit }, cancellationToken: ct);
				return;
			case { IsUrgentEdit: true }:
				await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
					key, new { Urgent = commandMail.AsUrgentEdit }, cancellationToken: ct);
				return;
		}
	}

	public async ValueTask DeleteMailAsync(string mailId, CancellationToken ct = default)
		=> await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
			mailId.Split("/")[1], cancellationToken: ct);

	public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder,
		CancellationToken ct = default)
	{
		var updates = await GetIncomingMailsAsync(player, folder, ct)
			.Select(x => new { Key = x.Id!.Split("/")[1], Folder = newFolder })
			.ToArrayAsync(cancellationToken: ct);

		await arangoDb.Document.UpdateManyAsync(handle, DatabaseConstants.Mails, updates, cancellationToken: ct);
	}

	public async ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken ct = default)
		=> await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphMail, DatabaseConstants.Mails,
			mailId.Split("/")[1], new { Folder = newFolder }, cancellationToken: ct);

	public async ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail,
		CancellationToken ct = default)
	{
		var results = await arangoDb.Query.ExecuteAsync<SharpMailQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND {id.Id} GRAPH {DatabaseConstants.GraphMail} FILTER v.Folder == {folder} LIMIT {mail},1 RETURN v",
			cancellationToken: ct);

		var convertedResults = results.Select(ConvertMailQueryResult);

		return convertedResults.FirstOrDefault();
	}

	public async ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data,
		CancellationToken ct = default)
	{
		// Get the edge that leads to it, otherwise we will have to create one.
		var result = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {sharpObjectId} GRAPH {DatabaseConstants.GraphObjectData} RETURN v",
			cancellationToken: ct);

		var first = result.FirstOrDefault();
		if (first?.ContainsKey("_key") ?? false)
		{
			var vertexKey = (string)first!["_key"];
			await arangoDb.Graph.Vertex.UpdateAsync(handle, DatabaseConstants.GraphObjectData, DatabaseConstants.ObjectData,
				vertexKey, new Dictionary<string, object> { { dataType, data } }, cancellationToken: ct);
			return;
		}

		var newJson = new Dictionary<string, object> { { dataType, data } };

		var newVertex = await arangoDb.Graph.Vertex.CreateAsync<dynamic, dynamic>(handle,
			DatabaseConstants.GraphObjectData,
			DatabaseConstants.ObjectData,
			newJson, cancellationToken: ct);

		await arangoDb.Graph.Edge.CreateAsync(handle,
			DatabaseConstants.GraphObjectData,
			DatabaseConstants.HasObjectData, new SharpEdgeCreateRequest(
				From: sharpObjectId,
				To: (string)newVertex.Vertex._id), cancellationToken: ct);
	}

	public async ValueTask<string?> GetExpandedObjectData(string sharpObjectId, string dataType,
		CancellationToken ct = default)
	{
		// Get the edge that leads to it, otherwise we will have to create one.
		var result = await arangoDb.Query.ExecuteAsync<JObject>(handle,
			$"FOR v IN 1..1 OUTBOUND {sharpObjectId} GRAPH {DatabaseConstants.GraphObjectData} RETURN v",
			cancellationToken: ct);
		var resultingValue = result.FirstOrDefault()?.GetValue(dataType);
		return resultingValue?.ToString(Formatting.None);
	}

	public async ValueTask SetExpandedServerData(string dataType, string data, CancellationToken ct = default)
	{
		var newJson = new Dictionary<string, dynamic>
		{
			{ "_key", dataType },
			{ "data", data }
		};

		_ = await arangoDb.Document.CreateAsync(handle,
			DatabaseConstants.ServerData,
			newJson,
			overwriteMode: ArangoOverwriteMode.Update,
			mergeObjects: true,
			keepNull: true, cancellationToken: ct);
	}

	public class ArangoDocumentWrapper<T>
	{
		public required T Data { get; set; }
	}

	public async ValueTask<string?> GetExpandedServerData(string dataType, CancellationToken ct = default)
	{
		try
		{
			var result = await arangoDb.Document.GetAsync<ArangoDocumentWrapper<string>>(handle,
				DatabaseConstants.ServerData,
				dataType, cancellationToken: ct);

			return result.Data;
		}
		catch
		{
			return null;
		}
	}

	public IAsyncEnumerable<SharpChannel> GetAllChannelsAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpChannelQueryResult>(
				handle, "FOR v IN @@C RETURN v",
				bindVars: new Dictionary<string, object>
				{
					{ "@C", DatabaseConstants.Channels }
				}, cancellationToken: ct)
			.Select(SharpChannelQueryToSharpChannel);

	private async ValueTask<SharpPlayer> GetChannelOwnerAsync(string channelId, CancellationToken ct = default)
	{
		var vertexes = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {channelId} GRAPH {DatabaseConstants.GraphChannels} RETURN v._id",
			cancellationToken: ct);
		var vertex = vertexes.First();
		var owner = await GetObjectNodeAsync(vertex, ct);
		return owner.AsPlayer;
	}

	private IAsyncEnumerable<SharpChannel.MemberAndStatus> GetChannelMembersAsync(
		string channelId, CancellationToken ct = default)
	{
		var stream = arangoDb.Query.ExecuteStreamAsync<SharpChannelMemberListQueryResult>(handle,
			$"FOR v,e IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN {{Id: v._id, Status: e}}",
			bindVars: new Dictionary<string, object>
			{
				{ "startVertex", channelId }
			},
			cancellationToken: ct);

		var result = stream
			.Select<SharpChannelMemberListQueryResult, SharpChannel.MemberAndStatus>(async (x, cancelToken) =>
				new SharpChannel.MemberAndStatus((await GetObjectNodeAsync(x.Id, cancelToken)).Known(),
					new SharpChannelStatus(
						Combine: x.Status.Combine,
						Gagged: x.Status.Gagged,
						Hide: x.Status.Hide,
						Mute: x.Status.Mute,
						Title: MarkupStringModule.deserialize(x.Status.Title ?? string.Empty)
					)));

		return result;
	}

	private SharpChannel SharpChannelQueryToSharpChannel(SharpChannelQueryResult x) =>
		new()
		{
			Id = x.Id,
			Name = MarkupStringModule.deserialize(x.MarkedUpName),
			Description = MarkupStringModule.deserialize(x.Description ?? string.Empty),
			Privs = x.Privs,
			JoinLock = x.JoinLock,
			SpeakLock = x.SpeakLock,
			SeeLock = x.SeeLock,
			HideLock = x.HideLock,
			ModLock = x.ModLock,
			Owner = new AsyncLazy<SharpPlayer>(async ct => await GetChannelOwnerAsync(x.Id, ct)),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
				GetChannelMembersAsync(x.Id, CancellationToken.None)),
			Mogrifier = x.Mogrifier,
			Buffer = x.Buffer
		};

	public async ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpChannelQueryResult>(
			handle,
			$"FOR v IN @@c FILTER v.Name == @name RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Channels },
				{ "name", name }
			}, cancellationToken: ct);
		return result?
			.Select(SharpChannelQueryToSharpChannel)
			.FirstOrDefault();
	}

	public IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj,
		CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpChannelQueryResult>(handle,
				$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.OnChannel} RETURN v",
				new Dictionary<string, object>
				{
					{ StartVertex, obj.Object().Id! }
				}, cancellationToken: ct)
			.Select(SharpChannelQueryToSharpChannel);

	public async ValueTask CreateChannelAsync(MarkupStringModule.MarkupString channel, string[] privs,
		SharpPlayer owner, CancellationToken ct = default)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive = [DatabaseConstants.Channels, DatabaseConstants.OwnerOfChannel, DatabaseConstants.OnChannel]
				}
			}, ct);

		try
		{
			var newChannel = new SharpChannelCreateRequest(
				Name: channel.ToPlainText(),
				MarkedUpName: MarkupStringModule.serialize(channel),
				Privs: privs
			);

			var createdChannel = await arangoDb.Graph.Vertex.CreateAsync<SharpChannelCreateRequest, SharpChannelQueryResult>(
				transaction, DatabaseConstants.GraphChannels, DatabaseConstants.Channels, newChannel, returnNew: true,
				cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphChannels,
				DatabaseConstants.OwnerOfChannel,
				new SharpEdgeCreateRequest(createdChannel.New.Id, owner.Id!), cancellationToken: ct);
			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
				new SharpEdgeCreateRequest(owner.Id!, createdChannel.New.Id), cancellationToken: ct);

			await arangoDb.Transaction.CommitAsync(transaction, ct);
		}
		catch
		{
			await arangoDb.Transaction.AbortAsync(transaction, ct);
		}
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel, MarkupStringModule.MarkupString? name,
		MarkupStringModule.MarkupString? description, string[]? privs,
		string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock, string? mogrifier,
		int? buffer, CancellationToken ct = default)
		=> await arangoDb.Graph.Vertex.UpdateAsync(handle,
			DatabaseConstants.GraphChannels, DatabaseConstants.Channels, channel.Id,
			new
			{
				Name = name is not null
					? MarkupStringModule.serialize(name)
					: MarkupStringModule.serialize(channel.Name),
				MarkedUpName = name is not null
					? name.ToPlainText()
					: channel.Name.ToPlainText(),
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
			}, cancellationToken: ct);

	public async ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner,
		CancellationToken ct = default)
	{
		var response = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.OwnerOfChannel} RETURN e._id",
			new Dictionary<string, object> { { StartVertex, channel.Id! } }, cancellationToken: ct);
		var ownerEdge = response.First();
		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OwnerOfChannel,
			ownerEdge, new { To = newOwner.Id }, cancellationToken: ct);
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken ct = default) =>
		await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.Channels,
			channel.Id, cancellationToken: ct);

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken ct = default)
		=> await arangoDb.Graph.Edge.CreateAsync(
			handle,
			DatabaseConstants.GraphChannels,
			DatabaseConstants.OnChannel,
			new SharpEdgeCreateRequest(channel.Id!, obj.Object().Id!),
			cancellationToken: ct);

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj,
		CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
			$"FOR v,e IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN e",
			new Dictionary<string, object>
			{
				{ StartVertex, obj.Object().Id! }
			}, cancellationToken: ct);

		var singleResult = result?.FirstOrDefault();
		if (singleResult is null) return;

		await arangoDb.Graph.Edge.RemoveAsync<dynamic>(handle,
			DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
			singleResult.Key, cancellationToken: ct);
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj,
		SharpChannelStatus status, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN e",
			new Dictionary<string, object> { { StartVertex, obj.Object().Id! } }, cancellationToken: ct);

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
			singleResult.Key, updates, cancellationToken: ct);
	}

	private SharpObjectFlag SharpObjectFlagQueryToSharpFlag(SharpObjectFlagQueryResult x) =>
		new()
		{
			Id = x.Id,
			Name = x.Name,
			Symbol = x.Symbol,
			System = x.System,
			SetPermissions = x.SetPermissions,
			UnsetPermissions = x.UnsetPermissions,
			Aliases = x.Aliases,
			TypeRestrictions = x.TypeRestrictions
		};

	private IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(string id,
		CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpAttributeFlagQueryResult>(handle,
				$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributeFlags} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct)
			.Select(x =>
				new SharpAttributeFlag()
				{
					Name = x.Name,
					Symbol = x.Symbol,
					System = x.System,
					Inheritable = x.Inheritable,
					Id = x.Id
				});

	private IAsyncEnumerable<SharpAttribute> GetAllAttributesAsync(string id, CancellationToken ct = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } }, cancellationToken: ct);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..999 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { "startVertex", id } }, cancellationToken: ct);
		}

		return sharpAttributeResults
			.Select(SharpAttributeQueryToSharpAttribute);
	}

	private IAsyncEnumerable<LazySharpAttribute> GetAllLazyAttributesAsync(string id, CancellationToken ct = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object>() { { StartVertex, id } }, cancellationToken: ct);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..999 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { "startVertex", id } }, cancellationToken: ct);
		}

		return sharpAttributeResults
			.Select(SharpAttributeQueryToLazySharpAttribute);
	}

	private async ValueTask<SharpPlayer> GetObjectOwnerAsync(string id, CancellationToken ct = default)
	{
		var owner = (await arangoDb.Query.ExecuteAsync<string>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphObjectOwners} RETURN v._id", cancellationToken: ct))
			.First();

		var populatedOwner = await GetObjectNodeAsync(owner, CancellationToken.None);

		return populatedOwner.AsPlayer;
	}

	private async ValueTask<SharpPlayer> GetAttributeOwnerAsync(string id, CancellationToken ct = default)
	{
		var owner = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphAttributeOwners} RETURN v._id",
			cancellationToken: ct)).First();

		var populatedOwner = await GetObjectNodeAsync(owner, CancellationToken.None);

		return populatedOwner.AsPlayer;
	}

	public async ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken ct = default)
	{
		// TODO: Optimize
		var parentId = (await arangoDb.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v._key", cache: true,
				cancellationToken: ct))
			.FirstOrDefault();
		if (parentId is null)
		{
			return new None();
		}

		return await GetObjectNodeAsync(new DBRef(parentId._id), ct);
	}

	private IAsyncEnumerable<SharpObject>? GetChildrenAsync(string id, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObject>(handle,
			$"FOR v IN 1..1 INBOUND {id} GRAPH {DatabaseConstants.GraphParents} RETURN v", cache: true,
			cancellationToken: ct);

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(string id, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpPower>(handle,
			$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphPowers} RETURN v", cache: true,
			cancellationToken: ct);

	private async ValueTask<AnySharpContainer> GetHomeAsync(string id, CancellationToken ct = default)
	{
		var homeId = (await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphHomes} RETURN v._id", cache: true,
			cancellationToken: ct)).First();
		var homeObject = await GetObjectNodeAsync(homeId, CancellationToken.None);

		return homeObject.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));
	}

	public IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpAttributeEntry>(handle,
			$"FOR v IN {DatabaseConstants.AttributeEntries:@} RETURN v", true, cancellationToken: ct);

	public async ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
		=> (await arangoDb.Query.ExecuteAsync<SharpAttributeEntry>(handle,
				$"FOR v IN {DatabaseConstants.AttributeEntries:@} FILTER v.Name == {name} RETURN v", true,
				cancellationToken: ct))
			.FirstOrDefault();

	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref,
		CancellationToken cancellationToken = default)
	{
		SharpObjectQueryResult? obj;
		try
		{
			obj = await arangoDb.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.Objects,
				dbref.Number.ToString(), cancellationToken: cancellationToken);
		}
		catch
		{
			obj = null;
		}

		if (obj is null
		    || dbref.CreationMilliseconds is not null
		    && obj.CreationTime != dbref.CreationMilliseconds)
			return new None();

		var startVertex = obj.Id;
		var res = (await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle,
				$"FOR v IN 1..1 INBOUND {startVertex} GRAPH {DatabaseConstants.GraphObjects} RETURN v", cache: true,
				cancellationToken: cancellationToken))
			.FirstOrDefault();

		if (res is null) return new None();

		var id = res.Id;

		var convertObject = SharpObjectQueryToSharpObject(obj);

		return obj.Type switch
		{
			DatabaseConstants.TypeThing => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			DatabaseConstants.TypePlayer => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct)),
				PasswordHash = res.PasswordHash
			},
			DatabaseConstants.TypeRoom => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.TypeExit => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'")
		};
	}

	private async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(string dbId,
		CancellationToken cancellationToken = default)
	{
		ArangoList<dynamic>? query;
		if (dbId.StartsWith(DatabaseConstants.Objects))
		{
			query = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 INBOUND {dbId} GRAPH {DatabaseConstants.GraphObjects} RETURN v",
				cache: true, cancellationToken: cancellationToken);
			query.Reverse();
		}
		else
		{
			query = await arangoDb.Query.ExecuteAsync<dynamic>(handle,
				$"FOR v IN 0..1 OUTBOUND {dbId} GRAPH {DatabaseConstants.GraphObjects} RETURN v", cache: true,
				cancellationToken: cancellationToken);
		}

		var res = query.First();
		var obj = query.Last();

		string id = res._id;
		var collection = id.Split("/")[0];

		var convertObject = SharpObjectQueryToSharpObject(obj);

		return collection switch
		{
			DatabaseConstants.Things => new SharpThing
			{
				Id = id, Object = convertObject,
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			DatabaseConstants.Players => new SharpPlayer
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct)), PasswordHash = res.PasswordHash
			},
			DatabaseConstants.Rooms => new SharpRoom { Id = id, Object = convertObject },
			DatabaseConstants.Exits => new SharpExit
			{
				Id = id, Object = convertObject, Aliases = res.Aliases.ToObject<string[]>(),
				Location = new(async ct => await mediator.Send(new GetCertainLocationQuery(id), ct)),
				Home = new(async ct => await GetHomeAsync(id, ct))
			},
			_ => throw new ArgumentException($"Invalid Object Type found: '{obj.Type}'"),
		};
	}

	private SharpObject SharpObjectQueryToSharpObject(dynamic obj) =>
		new()
		{
			Id = obj._id,
			Key = int.Parse((string)obj._key),
			Name = obj.Name,
			Type = obj.Type,
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Locks = ImmutableDictionary<string, string>
				.Empty, // FIX: ((Dictionary<string, string>?)obj.Locks ?? []).ToImmutableDictionary(),
			Flags = new(() => GetObjectFlagsAsync(obj._id, obj.Type.ToUpper(), CancellationToken.None)),
			Powers = new(() => GetPowersAsync(obj._id, CancellationToken.None)),
			Attributes = new(() => GetTopLevelAttributesAsync(obj._id, CancellationToken.None)),
			LazyAttributes = new(() => GetTopLevelLazyAttributesAsync(obj._id, CancellationToken.None)),
			AllAttributes = new(() => GetAllAttributesAsync(obj._id, CancellationToken.None)),
			LazyAllAttributes = new(() => GetAllLazyAttributesAsync(obj._id, CancellationToken.None)),
			Owner = new(async ct => await GetObjectOwnerAsync(obj._id, ct)),
			Parent = new(async ct => await GetParentAsync(obj._id, ct)),
			Children = new(() => GetChildrenAsync(obj._id, CancellationToken.None))
		};

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref,
		CancellationToken cancellationToken = default)
	{
		var obj = await arangoDb.Document.GetAsync<SharpObjectQueryResult>(handle, DatabaseConstants.Objects,
			dbref.Number.ToString(), cancellationToken: cancellationToken);

		if (dbref.CreationMilliseconds.HasValue && obj.CreationTime != dbref.CreationMilliseconds)
		{
			return null;
		}

		return obj is null
			? null
			: SharpObjectQueryToSharpObject(obj);
	}

	private SharpObject SharpObjectQueryToSharpObject(SharpObjectQueryResult obj) =>
		new()
		{
			Name = obj.Name,
			Type = obj.Type,
			Id = obj.Id,
			Key = int.Parse(obj.Key),
			Locks = (obj.Locks ?? []).ToImmutableDictionary(),
			CreationTime = obj.CreationTime,
			ModifiedTime = obj.ModifiedTime,
			Flags =
				new Lazy<IAsyncEnumerable<SharpObjectFlag>>(() => GetObjectFlagsAsync(obj.Id, obj.Type.ToUpper(), CancellationToken.None)),
			Powers = new Lazy<IAsyncEnumerable<SharpPower>>(() => GetPowersAsync(obj.Id, CancellationToken.None)),
			Attributes =
				new Lazy<IAsyncEnumerable<SharpAttribute>>(() =>
					GetTopLevelAttributesAsync(obj.Id, CancellationToken.None)),
			LazyAttributes =
				new Lazy<IAsyncEnumerable<LazySharpAttribute>>(() =>
					GetTopLevelLazyAttributesAsync(obj.Id, CancellationToken.None)),
			AllAttributes =
				new Lazy<IAsyncEnumerable<SharpAttribute>>(() => GetAllAttributesAsync(obj.Id, CancellationToken.None)),
			LazyAllAttributes =
				new Lazy<IAsyncEnumerable<LazySharpAttribute>>(() =>
					GetAllLazyAttributesAsync(obj.Id, CancellationToken.None)),
			Owner = new AsyncLazy<SharpPlayer>(async ct => await GetObjectOwnerAsync(obj.Id, ct)),
			Parent = new AsyncLazy<AnyOptionalSharpObject>(async ct => await GetParentAsync(obj.Id, ct)),
			Children = new Lazy<IAsyncEnumerable<SharpObject>?>(() => GetChildrenAsync(obj.Id, CancellationToken.None))
		};

	private IAsyncEnumerable<SharpAttribute> GetTopLevelAttributesAsync(string id, CancellationToken ct = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct);
		}

		return sharpAttributeResults
			.Select(SharpAttributeQueryToSharpAttribute);
	}

	private async ValueTask<SharpAttributeEntry?> GetRelatedAttributeEntry(string id, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpAttributeEntryQueryResult>(handle,
			$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributeEntries} RETURN v",
			new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: ct);

		if (result is null) return null;
		var entry = result.First();

		return new SharpAttributeEntry
		{
			DefaultFlags = entry.DefaultFlags,
			Name = entry.Name,
			Enum = entry.Enum,
			Id = entry.Id,
			Limit = entry.Limit
		};
	}

	private async ValueTask<SharpAttribute> SharpAttributeQueryToSharpAttribute(SharpAttributeQueryResult x,
		CancellationToken cancellationToken = default)
		=> new(
			x.Id,
			x.Key,
			x.Name,
			await GetAttributeFlagsAsync(x.Id, cancellationToken).ToArrayAsync(cancellationToken),
			null,
			x.LongName,
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(ct => Task.FromResult(GetTopLevelAttributesAsync(x.Id, ct))),
			new AsyncLazy<SharpPlayer?>(async ct => await GetAttributeOwnerAsync(x.Id, ct)),
			new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)))
		{
			Value = MarkupStringModule.deserialize(x.Value)
		};

	private IAsyncEnumerable<LazySharpAttribute> GetTopLevelLazyAttributesAsync(string id,
		CancellationToken cancellationToken = default)
	{
		// This only works for when we get a non-attribute as our ID.
		// Adjustment is needed if we get an attribute ID.
		IAsyncEnumerable<SharpAttributeQueryResult> sharpAttributeResults;
		if (id.StartsWith(DatabaseConstants.Attributes))
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: cancellationToken);
		}
		else
		{
			sharpAttributeResults = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
				$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v) FOR v IN 1..1 OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
				new Dictionary<string, object> { { StartVertex, id } }, cancellationToken: cancellationToken);
		}

		return sharpAttributeResults
			.Select<SharpAttributeQueryResult, LazySharpAttribute>(async (x, ctOuter) =>
				new LazySharpAttribute(
					x.Id,
					x.Key,
					x.Name,
					await GetAttributeFlagsAsync(x.Id, ctOuter).ToArrayAsync(ctOuter),
					null,
					x.LongName,
					new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(ct =>
						Task.FromResult(GetTopLevelLazyAttributesAsync(x.Id, ct))),
					new AsyncLazy<SharpPlayer?>(async ct => await GetAttributeOwnerAsync(x.Id, ct)),
					new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)),
					Value: new AsyncLazy<MarkupStringModule.MarkupString>(async ct =>
						MarkupStringModule.deserialize(await GetAttributeValue(x.Key, ct)))));
	}

	public async ValueTask<IAsyncEnumerable<LazySharpAttribute>?> GetLazyAttributesAsync(DBRef dbref,
		string attributePattern, CancellationToken cancellationToken = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: cancellationToken);

		var pattern = WildcardToRegex()
			.Replace(attributePattern, m => m.Value switch
			{
				"**" => ".*",
				"*" => "[^`]*",
				"?" => ".",
				_ => $"\\{m.Value}"
			});

		if (!result.Any())
		{
			return null;
		}

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex },
				{ "pattern", pattern }
			}, cancellationToken: cancellationToken);

		return result2
			.Select(SharpAttributeQueryToLazySharpAttribute);
	}


	public async ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributesAsync(DBRef dbref, string attributePattern,
		CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, 
				$"FOR v IN 1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v",
				new Dictionary<string, object>
				{
					{ StartVertex, startVertex }
				}, cache: true,
				cancellationToken: ct);
		
		var pattern = WildcardToRegex().Replace(attributePattern, m => m.Value switch
		{
			"*" => "[^`]*",
			"**" => ".*",
			"?" => ".",
			_ => $"\\{m.Value}"
		});

		if (!result.Any())
		{
			return null;
		}

		// OPTIONS { indexHint: "inverted_index_name", forceIndexHint: true }
		// This doesn't seem like it can be done on a GRAPH query?
		const string query =
			$"FOR v IN 1..99999 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern  RETURN v";

		// FILTER v.LongName =~ @pattern 
		
		var result2 = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, result.First().Id },
				{ "pattern", $"^{pattern}$" }
			}, cancellationToken: ct);

		var matches = await result2.ToListAsync(cancellationToken: ct);
		
		return result2
			.Select(SharpAttributeQueryToSharpAttribute);
	}

	public async ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributesByRegexAsync(DBRef dbref,
		string attributePattern, CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			arangoDb.Query.ExecuteStreamAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: ct);
		var pattern = $"(?i){attributePattern}"; // Add case-insensitive flag

		if (!await result.AnyAsync(cancellationToken: ct))
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
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		var result2 = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex },
				{ "pattern", pattern }
			}, cancellationToken: ct);

		return result2
			.Select(SharpAttributeQueryToSharpAttribute);
	}


	public async ValueTask<IAsyncEnumerable<LazySharpAttribute>?> GetLazyAttributesByRegexAsync(DBRef dbref,
		string attributePattern, CancellationToken ct = default)
	{
		await ValueTask.CompletedTask;
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: ct);

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
		var pattern = $"(?i){attributePattern}"; // Add case-insensitive flag
		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		return arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
				new Dictionary<string, object>
				{
					{ StartVertex, startVertex },
					{ "pattern", pattern }
				}, cancellationToken: ct)
			.Select(SharpAttributeQueryToLazySharpAttribute);
	}

	private async ValueTask<LazySharpAttribute> SharpAttributeQueryToLazySharpAttribute(SharpAttributeQueryResult x,
		CancellationToken cancellationToken = default)
		=> new(
			x.Id,
			x.Key,
			x.Name,
			await GetAttributeFlagsAsync(x.Id, cancellationToken).ToArrayAsync(cancellationToken),
			null,
			x.LongName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(ct =>
				Task.FromResult(GetTopLevelLazyAttributesAsync(x.Id, ct))),
			new AsyncLazy<SharpPlayer?>(async ct => await GetObjectOwnerAsync(x.Id, ct)),
			new AsyncLazy<SharpAttributeEntry?>(async ct => await GetRelatedAttributeEntry(x.Id, ct)),
			new AsyncLazy<MarkupStringModule.MarkupString>(async ct =>
				MarkupStringModule.deserialize(await GetAttributeValue(x.Key, ct))));

	private async ValueTask<string> GetAttributeValue(string key, CancellationToken ct = default)
	{
		var result = await arangoDb.Document.GetAsync<SharpAttributeQueryResult>(
			handle,
			DatabaseConstants.Attributes, key,
			cancellationToken: ct);
		return result?.Value ?? string.Empty;
	}

	public async ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributesRegexAsync(DBRef dbref,
		string attributePattern,
		CancellationToken cancellationToken = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		var result =
			await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, $"RETURN DOCUMENT({startVertex})", cache: true,
				cancellationToken: cancellationToken);

		if (!result.Any())
		{
			return null;
		}

		const string query =
			$"FOR v IN 1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphAttributes} FILTER v.LongName =~ @pattern RETURN v";

		return arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
				new Dictionary<string, object>()
				{
					{ StartVertex, startVertex },
					{ "pattern", attributePattern }
				}, cancellationToken: cancellationToken)
			.Select(SharpAttributeQueryToSharpAttribute);
	}

	public async ValueTask SetLockAsync(SharpObject target, string lockName, string lockString,
		CancellationToken ct = default)
		=> await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects, new
		{
			target.Key,
			Locks = target.Locks.Add(lockName, lockString)
		}, mergeObjects: true, cancellationToken: ct);

	public async ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken ct = default)
		=> await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Objects, new
		{
			target.Key,
			Locks = target.Locks.Remove(lockName)
		}, mergeObjects: true, cancellationToken: ct);

	public async ValueTask<IAsyncEnumerable<SharpAttribute>?> GetAttributeAsync(DBRef dbref, string[] attribute,
		CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		const string let =
			$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string query =
			$"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>()
			{
				{ "attr", attribute.Select(x => x.ToUpper()) },
				{ StartVertex, startVertex },
				{ "max", attribute.Length }
			}, cancellationToken: ct);

		if (result == null)
		{
			return null;
		}

		var count = 0;
		var resulted = await result.Select(async (item, _, innerCt) =>
		{
			count++;
			return await SharpAttributeQueryToSharpAttribute(item, innerCt);
		}).ToArrayAsync(cancellationToken: ct);

		return count != attribute.Length
			? null
			: resulted.ToAsyncEnumerable();
	}

	public IAsyncEnumerable<LazySharpAttribute>? GetLazyAttributeAsync(DBRef dbref,
		string[] attribute, CancellationToken ct = default)
	{
		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";

		const string let =
			$"LET start = FIRST(FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string query =
			$"{let} FOR v,e,p IN 1..@max OUTBOUND start GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v";

		var result = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle, query,
			new Dictionary<string, object>
			{
				{ "attr", attribute.Select(x => x.ToUpper()) },
				{ StartVertex, startVertex },
				{ "max", attribute.Length }
			}, cancellationToken: ct);

		return result?
			.Select(SharpAttributeQueryToLazySharpAttribute);
	}

	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MarkupStringModule.MarkupString value,
		SharpPlayer owner, CancellationToken ct = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(owner.Id);
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		var transactionHandle = await arangoDb.Transaction.BeginAsync(handle, new ArangoTransaction
		{
			LockTimeout = DatabaseBehaviorConstants.TransactionTimeout,
			WaitForSync = true,
			AllowImplicit = false,
			Collections = new ArangoTransactionScope
			{
				Exclusive =
				[
					DatabaseConstants.Attributes,
					DatabaseConstants.HasAttribute,
					DatabaseConstants.HasAttributeFlag,
					DatabaseConstants.HasAttributeOwner
				],
				Read =
				[
					DatabaseConstants.Attributes, DatabaseConstants.HasAttribute, DatabaseConstants.Objects,
					DatabaseConstants.HasAttributeFlag,
					DatabaseConstants.IsObject, DatabaseConstants.Players, DatabaseConstants.Rooms, DatabaseConstants.Things,
					DatabaseConstants.Exits
				]
			}
		}, ct);

		var startVertex = $"{DatabaseConstants.Objects}/{dbref.Number}";
		const string let1 =
			$"LET start = (FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphObjects} RETURN v)";
		const string let2 =
			$"LET foundAttributes = (FOR v,e,p IN 1..@max OUTBOUND FIRST(start) GRAPH {DatabaseConstants.GraphAttributes} PRUNE condition = NTH(@attr,LENGTH(p.edges)-1) != v.Name FILTER !condition RETURN v)";
		const string query = $"{let1} {let2} RETURN APPEND(start, foundAttributes)";

		var result = await arangoDb.Query.ExecuteAsync<dynamic[]>(handle, query, new Dictionary<string, object>
		{
			{ "attr", attribute },
			{ StartVertex, startVertex },
			{ "max", attribute.Length }
		}, cancellationToken: ct);

		var actualResult = result.First();

		var matches = actualResult.Length;
		var remaining = attribute.Skip(matches - 1).ToArray();
		var last = actualResult.Last();
		string lastId = last._id;

		// Create Path
		foreach (var nextAttr in remaining.Select((attrName, i) => (value: attrName, i)))
		{
			var longName = string.Join('`', attribute.SkipLast(remaining.Length - 1 - nextAttr.i));

			var sharpAttributeEntry = await GetSharpAttributeEntry(longName, ct);
			var flags = (sharpAttributeEntry?.DefaultFlags ?? [])
				.ToAsyncEnumerable()
				.Select(async (x, innerCt) => await GetAttributeFlagAsync(x, innerCt));

			var newOne = await arangoDb.Document.CreateAsync<SharpAttributeCreateRequest, SharpAttributeQueryResult>(
				transactionHandle, DatabaseConstants.Attributes,
				new SharpAttributeCreateRequest(nextAttr.value.ToUpper(),
					nextAttr.i == remaining.Length - 1
						? MarkupStringModule.serialize(value)
						: string.Empty,
					longName),
				waitForSync: true, cancellationToken: ct, returnNew: true);

			await foreach (var flag in (flags ?? AsyncEnumerable.Empty<SharpAttributeFlag>()).WithCancellation(ct))
			{
				await SetAttributeFlagAsync(transactionHandle, newOne.New.Id, flag!, ct);
			}

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.HasAttribute,
				new SharpEdgeCreateRequest(lastId, newOne.Id), waitForSync: true, cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributeOwners,
				DatabaseConstants.HasAttributeOwner,
				new SharpEdgeCreateRequest(newOne.Id, owner.Id!), waitForSync: true, cancellationToken: ct);

			lastId = newOne.Id;
		}

		// Update Path
		if (remaining.Length == 0)
		{
			await arangoDb.Document.UpdateAsync(transactionHandle, DatabaseConstants.Attributes,
				new { Key = lastId.Split('/')[1], Value = MarkupStringModule.serialize(value) }, waitForSync: true,
				mergeObjects: true, cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transactionHandle, DatabaseConstants.GraphAttributeOwners,
				DatabaseConstants.HasAttributeOwner,
				new SharpEdgeCreateRequest(lastId, owner.Id!), waitForSync: true, cancellationToken: ct);
		}

		await arangoDb.Transaction.CommitAsync(transactionHandle, ct);

		return true;
	}

	public async ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag,
		CancellationToken ct = default)
	{
		var attrInfo = await GetAttributeAsync(dbref.DBRef, attribute, ct);
		if (attrInfo is null) return false;
		var attr = await attrInfo.LastAsync(cancellationToken: ct);

		await SetAttributeFlagAsync(attr, flag, ct);
		return true;
	}

	public async ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag,
		CancellationToken ct = default)
		=> await arangoDb.Graph.Edge.CreateAsync(handle,
			DatabaseConstants.AttributeFlags, DatabaseConstants.HasAttributeFlag,
			new SharpEdgeCreateRequest(attr.Id, flag.Id!), cancellationToken: ct);


	private async ValueTask SetAttributeFlagAsync(ArangoHandle transactionHandle, string attrId, SharpAttributeFlag flag,
		CancellationToken ct = default)
		=> await arangoDb.Graph.Edge.CreateAsync(transactionHandle,
			DatabaseConstants.GraphAttributeFlags, DatabaseConstants.HasAttributeFlag,
			new SharpEdgeCreateRequest(attrId, flag.Id!), cancellationToken: ct);


	public async ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag,
		CancellationToken ct = default)
	{
		var attrInfo = await GetAttributeAsync(dbref.DBRef, attribute, ct);
		if (attrInfo is null) return false;
		var attr = await attrInfo.LastAsync(cancellationToken: ct);

		await UnsetAttributeFlagAsync(attr, flag, ct);
		return true;
	}

	public async ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag,
		CancellationToken ct = default) =>
		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Attributes, new
		{
			attr.Key,
			Value = attr.Flags.ToImmutableArray().Remove(flag)
		}, cancellationToken: ct);

	public async ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken ct = default) =>
		(await arangoDb.Query.ExecuteAsync<SharpAttributeFlag>(handle,
			"FOR v in @@C1 FILTER v.Name == @flag RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C1", DatabaseConstants.AttributeFlags },
				{ "flag", flagName }
			}, cache: true, cancellationToken: ct)).FirstOrDefault();

	public IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpAttributeFlag>(handle,
			$"FOR v in {DatabaseConstants.AttributeFlags:@} RETURN v",
			cache: true, cancellationToken: ct);

	public async ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken ct = default)
	{
		// Set the contents to empty, or remove entirely if no children.
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		// Get the attribute
		var attrs = await GetAttributeAsync(dbref, attribute, ct);
		if (attrs is null) return false;

		var targetAttr = await attrs.LastOrDefaultAsync(ct);
		if (targetAttr is null) return false;

		// Check if attribute has children (just need to know if any exist)
		var children = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {targetAttr.Id} GRAPH {DatabaseConstants.GraphAttributes} LIMIT 1 RETURN v._id",
			cancellationToken: ct);

		if (children.Any())
		{
			// Has children, just clear the value
			await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Attributes,
				new { Key = targetAttr.Key, Value = MarkupStringModule.serialize(MarkupStringModule.empty()) },
				mergeObjects: true, cancellationToken: ct);
		}
		else
		{
			// No children, remove the attribute
			await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.Attributes, targetAttr.Key, cancellationToken: ct);
		}

		return true;
	}

	public async ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken ct = default)
	{
		// Wipe a list of attributes. We assume the calling code figured out the permissions part.
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		// Get the attribute
		var attrs = await GetAttributeAsync(dbref, attribute, ct);
		if (attrs is null) return false;

		var targetAttr = await attrs.LastOrDefaultAsync(ct);
		if (targetAttr is null) return false;

		// Get all descendants (children, grandchildren, etc.) - traverse to max depth
		var descendants = arangoDb.Query.ExecuteStreamAsync<SharpAttributeQueryResult>(handle,
			$"FOR v IN 1..999 OUTBOUND {targetAttr.Id} GRAPH {DatabaseConstants.GraphAttributes} RETURN v",
			cancellationToken: ct);

		// Remove all descendants first (bottom-up) to avoid orphans
		await foreach (var descendant in descendants.Reverse().WithCancellation(ct))
		{
			await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphAttributes,
				DatabaseConstants.Attributes, descendant.Key, cancellationToken: ct);
		}

		// Remove the target attribute itself
		await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphAttributes,
			DatabaseConstants.Attributes, targetAttr.Key, cancellationToken: ct);

		return true;
	}

	public async ValueTask<IAsyncEnumerable<AnySharpObject>> GetNearbyObjectsAsync(DBRef obj,
		CancellationToken ct = default)
	{
		var self = (await GetObjectNodeAsync(obj, ct)).WithoutNone();
		var location = await self.Where();

		var list = new List<AnySharpObject> { self };

		return list.ToAsyncEnumerable()
			.Union((await GetContentsAsync(self.Object().DBRef, ct))!.Select(x => x.WithRoomOption()))
			.Union((await GetContentsAsync(location.Object().DBRef, ct))!.Select(x => x.WithRoomOption()));
	}

	public async ValueTask<IAsyncEnumerable<AnySharpObject>> GetNearbyObjectsAsync(AnySharpObject obj,
		CancellationToken ct = default)
	{
		var location = await obj.Where();

		var list = new List<AnySharpObject> { obj };

		return list.ToAsyncEnumerable()
			.Union((await GetContentsAsync(obj.Object().DBRef, ct))!.Select(x => x.WithRoomOption()))
			.Union((await GetContentsAsync(location.Object().DBRef, ct))!.Select(x => x.WithRoomOption()));
	}

	/// <summary>
	/// Gets the location of an object, at X depth, with 0 returning the same object, and -1 going until it can't go deeper.
	/// </summary>
	/// <param name="obj">Location</param>
	/// <param name="depth">Depth</param>
	/// <param name="ct">Cancellation Token</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1,
		CancellationToken ct = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, ct);
		if (baseObject.IsNone) return new None();

		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ StartVertex, baseObject.Id()! }
		}, cancellationToken: ct);
		var locationBaseObj = await GetObjectNodeAsync(query.Last(), CancellationToken.None);
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
	/// <param name="ct">Cancellation Token</param>
	/// <returns>The deepest findable object based on depth</returns>
	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken ct = default)
	{
		var variableDepth = depth == -1 ? "0" : $"0..{depth}";
		var locationQuery =
			$"FOR v IN {variableDepth} OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, locationQuery, new Dictionary<string, object>()
		{
			{ StartVertex, id }
		}, cancellationToken: ct);
		var locationBaseObj = await GetObjectNodeAsync(query.Last(), CancellationToken.None);
		var trueLocation = locationBaseObj.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new Exception("Invalid Location found"),
			thing => thing,
			_ => throw new Exception("Invalid Location found"));

		return trueLocation;
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1,
		CancellationToken ct = default) =>
		(await GetLocationAsync(obj.Object().DBRef, depth, ct)).WithoutNone();

	public async ValueTask<IAsyncEnumerable<AnySharpContent>?> GetContentsAsync(DBRef obj, CancellationToken ct = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, ct);
		if (baseObject.IsNone) return null;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v._id";
		var query = await arangoDb.Query.ExecuteAsync<string>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ StartVertex, baseObject.Object()!.Id! }
			}, cancellationToken: ct);

		return query.ToAsyncEnumerable()
			.Select(GetObjectNodeAsync)
			.Select<AnyOptionalSharpObject, AnySharpContent>(x
				=> x.Match<AnySharpContent>(
					player => player,
					_ => throw new Exception("Invalid Contents found"),
					exit => exit,
					thing => thing,
					_ => throw new Exception("Invalid Contents found")
				));
	}

	public async ValueTask<IAsyncEnumerable<AnySharpContent>?> GetContentsAsync(AnySharpContainer node,
		CancellationToken ct = default)
	{
		var startVertex = node.Id;

		const string locationQuery =
			$"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphLocations} RETURN v";
		var query = await arangoDb.Query.ExecuteAsync<dynamic>(handle, $"{locationQuery}",
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex }
			}, cancellationToken: ct);

		var queryIds = query.Select(x => (string)x._id);

		return queryIds
			.ToAsyncEnumerable()
			.Select<string, AnyOptionalSharpObject>(GetObjectNodeAsync)
			.Select(x => x.Match<AnySharpContent>(
				player => player,
				_ => throw new Exception("Invalid Contents found"),
				exit => exit,
				thing => thing,
				_ => throw new Exception("Invalid Contents found")
			));
	}

	public async ValueTask<IAsyncEnumerable<SharpExit>?> GetExitsAsync(DBRef obj, CancellationToken ct = default)
	{
		// This is bad code. We can't use graphExits for this.
		var baseObject = await GetObjectNodeAsync(obj, ct);
		if (baseObject.IsNone) return null;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphExits} RETURN v";
		var query = await arangoDb.Query.ExecuteAsync<SharpObjectQueryResult>(handle, exitQuery,
			new Dictionary<string, object>
			{
				{ StartVertex, baseObject.Known().Id()! }
			}, cancellationToken: ct);

		return query
			.Select(x => x.Id)
			.ToAsyncEnumerable()
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Match(
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found"),
				exit => exit,
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found")
			));
	}

	public async ValueTask<IAsyncEnumerable<SharpExit>> GetExitsAsync(AnySharpContainer node,
		CancellationToken ct = default)
	{
		await ValueTask.CompletedTask;
		// This is bad code. We can't use graphExits for this.
		var startVertex = node.Id;

		const string exitQuery = $"FOR v IN 1..1 INBOUND @startVertex GRAPH {DatabaseConstants.GraphExits} RETURN v";
		var query = arangoDb.Query.ExecuteStreamAsync<SharpObjectQueryResult>(handle, exitQuery,
			new Dictionary<string, object>
			{
				{ StartVertex, startVertex }
			}, cancellationToken: ct);

		return query
			.Select(x => x.Id)
			.Select(GetObjectNodeAsync) // TODO: Optimize to make a single call.
			.Select(x => x.Match(
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found"),
				exit => exit,
				_ => throw new Exception("Invalid Exit found"),
				_ => throw new Exception("Invalid Exit found")
			));
	}

	public async ValueTask<IAsyncEnumerable<SharpPlayer>> GetPlayerByNameOrAliasAsync(string name,
		CancellationToken ct = default)
	{
		await ValueTask.CompletedTask;
		return (arangoDb.Query.ExecuteStreamAsync<string>(handle,
				$"FOR v IN {DatabaseConstants.Objects} FILTER v.Type == @type && (v.Name == @name || @name IN v.Aliases) RETURN v._id",
				bindVars: new Dictionary<string, object>
				{
					{ "name", name },
					{ "type", DatabaseConstants.TypePlayer }
				}, cancellationToken: ct) ?? AsyncEnumerable.Empty<string>())
			.Select(GetObjectNodeAsync)
			.Select(x => x.AsPlayer);
	}

	public async IAsyncEnumerable<SharpObject> GetAllObjectsAsync([EnumeratorCancellation] CancellationToken ct = default)
	{
		var objectIds = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$"FOR v IN {DatabaseConstants.Objects:@} RETURN v._id",
			cancellationToken: ct) ?? AsyncEnumerable.Empty<string>();

		await foreach (var id in objectIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone)
			{
				yield return optionalObj.Known.Object();
			}
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		// Query to find all exits that lead to the destination
		// Exits are connected to their destination via the AtLocation edge in GraphLocations
		var exitIds = arangoDb.Query.ExecuteStreamAsync<string>(handle,
			$@"FOR v, e IN 1..1 INBOUND @destination GRAPH @graph
			   FILTER v.Type == @exitType
			   RETURN v._id",
			bindVars: new Dictionary<string, object>
			{
				{ "destination", $"{DatabaseConstants.Objects}/{destination.Number}" },
				{ "graph", DatabaseConstants.GraphLocations },
				{ "exitType", DatabaseConstants.TypeExit }
			}, cancellationToken: ct) ?? AsyncEnumerable.Empty<string>();

		await foreach (var id in exitIds.WithCancellation(ct))
		{
			var optionalObj = await GetObjectNodeAsync(id, ct);
			if (!optionalObj.IsNone)
			{
				yield return optionalObj.AsExit;
			}
		}
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination,
		CancellationToken ct = default)
	{
		var edge = (await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
				$"FOR v,e IN 1..1 OUTBOUND {enactorObj.Id} GRAPH {DatabaseConstants.GraphLocations} RETURN e",
				cancellationToken: ct))
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
			waitForSync: true, cancellationToken: ct);
	}

	public async ValueTask SetupLogging()
	{
		_ = await arangoDb.Collection.ExistAsync(handle, DatabaseConstants.Logs);
	}

	public IAsyncEnumerable<LogEventEntity> GetChannelLogs(SharpChannel channel, int skip = 0, int count = 100)
		=> arangoDb.Query.ExecuteStreamAsync<LogEventEntity>(
			handle,
			$"FOR v IN @@c FILTER v.Properties.ChannelId == @channelId SORT v.Timestamp DESC LIMIT @skip, @count RETURN v",
			bindVars:
			new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Logs },
				{ "channelId", channel.Id! },
				{ "skip", skip },
				{ "count", count }
			});

	public IAsyncEnumerable<LogEventEntity> GetLogsFromCategory(string category, int skip = 0, int count = 100)
		=> arangoDb.Query.ExecuteStreamAsync<LogEventEntity>(
			handle,
			$"FOR v IN @@c FILTER v.Properties.Category == @category SORT v.Timestamp DESC LIMIT @skip, @count RETURN v",
			bindVars:
			new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Logs },
				{ "category", category },
				{ "skip", skip },
				{ "count", count }
			});

	public async ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, CancellationToken ct = default)
	{
		var hashed = passwordService.HashPassword(player.Object.DBRef.ToString(), password);

		await arangoDb.Document.UpdateAsync(handle, DatabaseConstants.Players, new
		{
			player.Id,
			PasswordHash = hashed
		}, mergeObjects: true, cancellationToken: ct);
	}

	[GeneratedRegex("[.*+?^${}()|[\\]/]")]
	private static partial Regex WildcardToRegex();
}