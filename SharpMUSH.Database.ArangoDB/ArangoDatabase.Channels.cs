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
		=> WithOwners(arangoDb.Query.ExecuteStreamAsync<SharpChannelQueryResult>(
			handle, "FOR v IN @@C RETURN v",
			bindVars: new Dictionary<string, object>
			{
				{ "@C", DatabaseConstants.Channels }
			}, cancellationToken: ct), ct);

	/// <summary>
	/// Reads each channel's owner as the list is drawn, rather than leaving an <c>AsyncLazy</c> to go back
	/// to the database whenever something first asks.
	/// </summary>
	/// <remarks>
	/// The late read left a window: <c>@channel/add</c> resolves the owner of every channel to count the
	/// ones the executor owns, and a channel deleted between the list being read and its owner being asked
	/// for took the whole command with it. Reading here makes the listing a snapshot of one moment.
	/// <para>
	/// A channel whose owner has gone is dropped rather than reported. A channel always has an owner, so a
	/// row without one is a channel that no longer exists — it belongs out of the listing, not thrown at
	/// whoever happened to be reading.
	/// </para>
	/// </remarks>
	private async IAsyncEnumerable<SharpChannel> WithOwners(IAsyncEnumerable<SharpChannelQueryResult> channels,
		[EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var channel in channels.WithCancellation(ct))
		{
			yield return SharpChannelQueryToSharpChannel(channel, await OwnerOrGodAsync(channel.Id, ct));
		}
	}

	/// <summary>
	/// A channel is never dropped for want of an owner.
	/// </summary>
	/// <remarks>
	/// Skipping it looked tidier and was wrong: the channel vanished from the listing, and
	/// <c>GetChannelAsync</c> answered "I don't recognize that channel" for one that plainly existed.
	/// A channel missing its owner is a broken invariant, not a missing channel — say so in the log and
	/// read it as God's, which is who <c>DeleteObjectAsync</c> would have given it to.
	/// </remarks>
	private async ValueTask<SharpPlayer> OwnerOrGodAsync(string channelId, CancellationToken ct)
	{
		var owner = await GetChannelOwnerAsync(channelId, ct);
		if (owner is not null) return owner;

		logger.LogWarning(
			"Channel '{Channel}' has no owner; reading it as God's. A channel always has an owner, so this "
			+ "is data that predates DeleteObjectAsync handing ownership on.", channelId);

		return (await GetObjectNodeAsync(new DBRef(GodKey), ct)).AsPlayer;
	}

	private async ValueTask<SharpPlayer?> GetChannelOwnerAsync(string channelId, CancellationToken ct = default)
	{
		var vertexes = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v IN 1..1 OUTBOUND {channelId} GRAPH {DatabaseConstants.GraphChannels} RETURN v._id",
			cancellationToken: ct);

		// A channel always has an owner, so no vertex means the channel itself has gone -- deleted while
		// this read was walking the list. Absent, not broken: the caller drops the channel from the
		// listing rather than failing every reader of it.
		if (vertexes.Count == 0) return null;

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

	private SharpChannel SharpChannelQueryToSharpChannel(SharpChannelQueryResult x, SharpPlayer owner) =>
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
			Owner = new AsyncLazy<SharpPlayer>(_ => Task.FromResult(owner)),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
				new FreshAsyncEnumerable<SharpChannel.MemberAndStatus>(enumCt => GetChannelMembersAsync(x.Id, enumCt))),
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
		var found = result?.FirstOrDefault();
		if (found is null) return null;

		return SharpChannelQueryToSharpChannel(found, await OwnerOrGodAsync(found.Id, ct));
	}

	public IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj,
		CancellationToken ct = default) =>
		WithOwners(arangoDb.Query.ExecuteStreamAsync<SharpChannelQueryResult>(handle,
			$"FOR v in 1..1 OUTBOUND @startVertex GRAPH {DatabaseConstants.GraphChannels} RETURN v",
			new Dictionary<string, object>
			{
				{ StartVertex, obj.Object().Id! }
			}, cancellationToken: ct), ct);

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

			await arangoDb.Transaction.CommitAsync(transaction, ct);
			return new Success();
		}
		catch (Exception ex)
		{
			// This used to be `catch { await AbortAsync(...); }` with no rethrow and no result, so every
			// failure — schema violation, lost owner, dropped connection — was reported to the caller as a
			// created channel.
			logger.LogError(ex, "Failed to create channel {ChannelName}", channelName);

			// Unconditional, including when the commit itself threw: an uncommitted transaction left open
			// holds the exclusive lock until it expires, which blocks every other channel create. Aborting
			// one that did commit answers "not found", which is logged and harmless.
			try
			{
				await arangoDb.Transaction.AbortAsync(transaction, ct);
			}
			catch (Exception abortEx)
			{
				logger.LogError(abortEx, "Failed to abort the transaction for channel {ChannelName}", channelName);
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

		// A channel with no owner edge is exactly the one that most needs re-owning, so create the edge
		// rather than updating one that is not there. Giving an ownerless channel an owner is the repair
		// for it -- @channel/chown and ObjectDestructionService both arrive here.
		if (response.Count == 0)
		{
			await arangoDb.Graph.Edge.CreateAsync(
				handle,
				DatabaseConstants.GraphChannels,
				DatabaseConstants.OwnerOfChannel,
				new SharpEdgeCreateRequest(channel.Id!, newOwner.Id!),
				cancellationToken: ct);
			return;
		}

		await arangoDb.Graph.Edge.UpdateAsync(handle, DatabaseConstants.GraphChannels, DatabaseConstants.OwnerOfChannel,
			response.First(), new { To = newOwner.Id }, cancellationToken: ct);
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
