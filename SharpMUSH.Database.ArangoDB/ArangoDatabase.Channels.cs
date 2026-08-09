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
	#region Channels

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
						Title: MModule.deserialize(x.Status.Title ?? string.Empty)
					)));

		return result;
	}

	private SharpChannel SharpChannelQueryToSharpChannel(SharpChannelQueryResult x) =>
		new()
		{
			Id = x.Id,
			Name = MModule.deserialize(x.MarkedUpName),
			Description = MModule.deserialize(x.Description ?? string.Empty),
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
				$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN v",
				new Dictionary<string, object>
				{
					{ StartVertex, obj.Object().Id! }
				}, cancellationToken: ct)
			.Select(SharpChannelQueryToSharpChannel);

	public async ValueTask<ChannelCreationResult> CreateChannelAsync(MString channel, string[] privs,
		SharpPlayer owner, CancellationToken ct = default)
	{
		var channelName = channel.ToPlainText();

		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction
			{
				Collections = new ArangoTransactionScope
				{
					Exclusive = [DatabaseConstants.Channels, DatabaseConstants.OwnerOfChannel, DatabaseConstants.OnChannel]
				}
			}, ct);

		var settled = false;

		try
		{
			// Uniqueness is decided INSIDE this transaction, on purpose. The Exclusive scope already holds a
			// write lock on Channels for its whole lifetime, so a second creator either waits here or reads
			// the committed winner — an existence check taken now is atomic with the create that follows it.
			// ChannelAdd's own read-before-create is outside any transaction and cannot be: it is a fast path
			// for the friendly "Channel already exists." message, not the guarantee.
			//
			// No unique index on Name is added for this. It would be redundant with the exclusive lock on the
			// only path that writes channels, and it cannot be created at all on a database that already
			// holds duplicates — which is exactly the state the bug this closes produces. Memgraph does need
			// a constraint, because snapshot isolation there lets both writers observe "absent"; see
			// MemgraphDatabase.Migration.cs.
			var existing = await arangoDb.Query.ExecuteAsync<string>(transaction,
				"FOR c IN @@C FILTER c.Name == @name LIMIT 1 RETURN c._id",
				new Dictionary<string, object>
				{
					{ "@C", DatabaseConstants.Channels },
					{ "name", channelName }
				}, cancellationToken: ct);

			if (existing.Count > 0)
			{
				settled = true;
				await arangoDb.Transaction.AbortAsync(transaction, ct);
				return new ChannelNameTaken();
			}

			var newChannel = new SharpChannelCreateRequest(
				Name: channelName,
				MarkedUpName: MModule.serialize(channel),
				Privs: privs
			);

			var createdChannel = await arangoDb.Graph.Vertex.CreateAsync<SharpChannelCreateRequest, SharpChannelQueryResult>(
				transaction, DatabaseConstants.GraphChannels, DatabaseConstants.Channels, newChannel, returnNew: true,
				cancellationToken: ct);

			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphChannels,
				DatabaseConstants.OwnerOfChannel,
				new SharpEdgeCreateRequest(createdChannel.New.Id, owner.Object.Id!), cancellationToken: ct);
			await arangoDb.Graph.Edge.CreateAsync(transaction, DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
				new SharpEdgeCreateRequest(owner.Object.Id!, createdChannel.New.Id), cancellationToken: ct);

			settled = true;
			await arangoDb.Transaction.CommitAsync(transaction, ct);
			return new Success();
		}
		catch (Exception ex)
		{
			// This used to be `catch { await AbortAsync(...); }` with no rethrow and no result, so every
			// failure — schema violation, lost owner, dropped connection — was reported to the caller as a
			// created channel.
			logger.LogError(ex, "Failed to create channel {ChannelName}", channelName);

			if (!settled)
			{
				try
				{
					await arangoDb.Transaction.AbortAsync(transaction, ct);
				}
				catch (Exception abortEx)
				{
					logger.LogError(abortEx, "Failed to abort the transaction for channel {ChannelName}", channelName);
				}
			}

			return new Error<string>(ex.Message);
		}
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel, MString? name,
		MString? description, string[]? privs,
		string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock, string? mogrifier,
		int? buffer, CancellationToken ct = default)
		// Vertex.UpdateAsync takes the document KEY, not the full "collection/key" _id that
		// SharpChannel.Id carries — passing the _id produced a 404 on every channel update.
		=> await arangoDb.Graph.Vertex.UpdateAsync(handle,
			DatabaseConstants.GraphChannels, DatabaseConstants.Channels, ExtractKey(channel.Id!),
			new
			{
				// Name is the plain-text lookup key (GetChannelAsync filters on it) and MarkedUpName holds
				// the serialized MString — the same way CreateChannelAsync writes them. These two were
				// swapped, so any channel update wrote serialized markup into Name and left the channel
				// unfindable, with @channel/what then failing to deserialize the plain text in MarkedUpName.
				Name = name is not null
					? name.ToPlainText()
					: channel.Name.ToPlainText(),
				MarkedUpName = name is not null
					? MModule.serialize(name)
					: MModule.serialize(channel.Name),
				Description = description is not null
					? MModule.serialize(description)
					: MModule.serialize(channel.Description),
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
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN e._key",
			new Dictionary<string, object> { { StartVertex, channel.Id! } }, cancellationToken: ct);
		var ownerEdgeKey = response.First();
		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OwnerOfChannel,
			ownerEdgeKey, new { To = newOwner.Id }, cancellationToken: ct);
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken ct = default) =>
		await arangoDb.Graph.Vertex.RemoveAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.Channels,
			ExtractKey(channel.Id!), cancellationToken: ct);

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken ct = default)
		=> await arangoDb.Graph.Edge.CreateAsync(
			handle,
			DatabaseConstants.GraphChannels,
			DatabaseConstants.OnChannel,
			new SharpEdgeCreateRequest(obj.Object().Id!, channel.Id!),
			cancellationToken: ct);

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj,
		CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN e",
			new Dictionary<string, object>
			{
				{ StartVertex, obj.Object().Id! }
			}, cancellationToken: ct);

		// Find all edges connecting to the specific channel (there might be duplicates)
		var edges = result?.Where(x => x.To == channel.Id).ToList();
		if (edges is null || edges.Count == 0) return;

		foreach (var edge in edges)
		{
			await arangoDb.Graph.Edge.RemoveAsync<ArangoVoid>(handle,
				DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
				edge.Key, cancellationToken: ct);
		}
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj,
		SharpChannelStatus status, CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN e",
			new Dictionary<string, object>
			{
				{ StartVertex, obj.Object().Id! }
			}, cancellationToken: ct);

		var edge = result?.FirstOrDefault(x => x.To == channel.Id);
		if (edge is null) return;

		// A List<KeyValuePair<..>> serializes as a JSON array, which Arango rejects with
		// "VPack error: Expecting Object". The patch body has to be an object.
		var updates = new Dictionary<string, object>();
		if (status.Combine is { } combine)
		{
			updates[nameof(status.Combine)] = combine;
		}

		if (status.Gagged is { } gagged)
		{
			updates[nameof(status.Gagged)] = gagged;
		}

		if (status.Hide is { } hide)
		{
			updates[nameof(status.Hide)] = hide;
		}

		if (status.Mute is { } mute)
		{
			updates[nameof(status.Mute)] = mute;
		}

		if (status.Title is { } title)
		{
			updates[nameof(status.Title)] = MModule.serialize(title);
		}

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OnChannel,
			edge.Key, updates, cancellationToken: ct);
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

	#endregion
}
