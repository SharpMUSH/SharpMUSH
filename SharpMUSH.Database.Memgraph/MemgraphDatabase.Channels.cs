using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Channels

	public async IAsyncEnumerable<SharpChannel> GetAllChannelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(ChannelWithOwner("MATCH (c:Channel)"), ct: cancellationToken);
		foreach (var record in result.Result)
			yield return await MapRecordToChannelAsync(record, cancellationToken);
	}

	public async ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(ChannelWithOwner("MATCH (c:Channel {name: $name})"),
			new { name }, cancellationToken);
		return result.Result.Count > 0 ? await MapRecordToChannelAsync(result.Result[0], cancellationToken) : null;
	}

	public async IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var result = await ExecuteWithRetryAsync(
			ChannelWithOwner("MATCH (member:Object {key: $key})-[:ON_CHANNEL]->(c:Channel)"),
			new { key = objKey }, cancellationToken);

		foreach (var record in result.Result)
			yield return await MapRecordToChannelAsync(record, cancellationToken);
	}

	/// <summary>
	/// True when Memgraph rejected a write because it would break the <c>:Channel(name)</c> uniqueness
	/// constraint. That is the whole atomicity mechanism on this backend, so it has to be told apart from a
	/// genuine failure rather than surfaced as one.
	/// </summary>
	private static bool IsUniqueConstraintViolation(string message)
		=> message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);

	public async ValueTask<ChannelCreationResult> CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		var channelName = name.ToPlainText();
		var serializedName = MModule.serialize(name);
		var ownerObjKey = owner.Object.Key;

		// Uniqueness comes from the constraint, not from a check in this method. Memgraph runs at snapshot
		// isolation, so a MATCH-then-CREATE in one query still lets two concurrent writers both observe
		// "absent" and both create; only the constraint rejects the loser at commit. ArangoDB gets the same
		// guarantee from an exclusive collection lock instead, and SurrealDB from the record id.
		try
		{
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $ownerKey})
CREATE (c:Channel {name: $name, markedUpName: $markedUpName, description: '', privs: $privs,
joinLock: '', speakLock: '', seeLock: '', hideLock: '', modLock: '',
buffer: 0, mogrifier: ''})
CREATE (c)-[:HAS_CHANNEL_OWNER]->(o)
CREATE (o)-[:ON_CHANNEL {combine: false, gagged: false, hide: false, mute: false, title: ''}]->(c)
""", new { name = channelName, markedUpName = serializedName, privs, ownerKey = ownerObjKey }, cancellationToken);

			return new Success();
		}
		catch (Neo4jException ex) when (IsUniqueConstraintViolation(ex.Message))
		{
			return new ChannelNameTaken();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to create channel {ChannelName}", channelName);
			return new Error<string>(ex.Message);
		}
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel, MString? name, MString? description, string[]? privs,
	string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock,
	string? mogrifier, int? buffer, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var newName = name is not null ? name.ToPlainText() : channelName;
		var newMarkedUpName = name is not null ? MModule.serialize(name) : MModule.serialize(channel.Name);
		var newDescription = description is not null ? MModule.serialize(description) : MModule.serialize(channel.Description);

		await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $oldName})
SET c.name = $newName, c.markedUpName = $markedUpName, c.description = $description,
c.privs = $privs, c.joinLock = $joinLock, c.speakLock = $speakLock,
c.seeLock = $seeLock, c.hideLock = $hideLock, c.modLock = $modLock,
c.buffer = $buffer, c.mogrifier = $mogrifier
""", new
		{
			oldName = channelName,
			newName,
			markedUpName = newMarkedUpName,
			description = newDescription,
			privs = privs ?? channel.Privs,
			joinLock = joinLock ?? channel.JoinLock ?? "",
			speakLock = speakLock ?? channel.SpeakLock ?? "",
			seeLock = seeLock ?? channel.SeeLock ?? "",
			hideLock = hideLock ?? channel.HideLock ?? "",
			modLock = modLock ?? channel.ModLock ?? "",
			buffer = buffer ?? channel.Buffer,
			mogrifier = mogrifier ?? channel.Mogrifier ?? ""
		}, cancellationToken);
	}

	public async ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var ownerObjKey = newOwner.Object.Key;

		// OPTIONAL on the old edge: gating the CREATE on finding one made re-owning a channel that has
		// lost its owner a silent no-op, and that channel is exactly the one that needs re-owning.
		// @channel/chown and ObjectDestructionService both arrive here.
		await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $name})
MATCH (o:Object {key: $ownerKey})
OPTIONAL MATCH (c)-[r:HAS_CHANNEL_OWNER]->()
DELETE r
CREATE (c)-[:HAS_CHANNEL_OWNER]->(o)
""", new { name = channelName, ownerKey = ownerObjKey }, cancellationToken);
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		await ExecuteWithRetryAsync("MATCH (c:Channel {name: $name}) DETACH DELETE c", new { name = channelName }, cancellationToken);
	}

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key}), (c:Channel {name: $name})
CREATE (o)-[:ON_CHANNEL {combine: false, gagged: false, hide: false, mute: false, title: ''}]->(c)
""", new { key = objKey, name = channelName }, cancellationToken);
	}

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[r:ON_CHANNEL]->(c:Channel {name: $name})
DELETE r
""", new { key = objKey, name = channelName }, cancellationToken);
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;

		var setClauses = new List<string>();
		var parameters = new Dictionary<string, object>
{
{ "key", objKey },
{ "name", channelName }
};

		if (status.Combine is { } combine)
		{
			setClauses.Add("r.combine = $combine");
			parameters["combine"] = combine;
		}
		if (status.Gagged is { } gagged)
		{
			setClauses.Add("r.gagged = $gagged");
			parameters["gagged"] = gagged;
		}
		if (status.Hide is { } hide)
		{
			setClauses.Add("r.hide = $hide");
			parameters["hide"] = hide;
		}
		if (status.Mute is { } mute)
		{
			setClauses.Add("r.mute = $mute");
			parameters["mute"] = mute;
		}
		if (status.Title is { } title)
		{
			setClauses.Add("r.title = $title");
			parameters["title"] = MModule.serialize(title);
		}

		if (setClauses.Count == 0) return;

		var cypher = "MATCH (o:Object {key: $key})-[r:ON_CHANNEL]->(c:Channel {name: $name}) SET " +
		string.Join(", ", setClauses);

		await ExecuteWithRetryAsync(cypher, parameters, cancellationToken);
	}

	/// <summary>
	/// Completes a channel match by joining through to its owner, object node and typed node together.
	/// </summary>
	/// <remarks>
	/// The owner used to be an <c>AsyncLazy</c> that queried when something first awaited it, which cost a
	/// round trip per channel — <c>@channel/add</c> resolves every owner in the list to count its own —
	/// and left a window in which the channel could be deleted between the list being read and the owner
	/// being asked for. Reading it here makes the channel a snapshot: one query, and nothing to race.
	/// <para>
	/// The join is inner on purpose. A channel always has an owner, so a row without one is not a channel;
	/// it drops out of listings rather than throwing and taking every reader of the list with it.
	/// </para>
	/// </remarks>
	private static string ChannelWithOwner(string match) => $"""
{match}
OPTIONAL MATCH (c)-[:HAS_CHANNEL_OWNER]->(ownerObj:Object)
OPTIONAL MATCH (ownerTyped:Player)-[:IS_OBJECT]->(ownerObj)
RETURN c, ownerObj, ownerTyped
""";

	/// <summary>
	/// The owner from the same row where it is there, and a direct read where it is not.
	/// </summary>
	/// <remarks>
	/// The join is OPTIONAL and the channel is never dropped for want of an owner. An inner join looked
	/// tidier and was wrong: a channel whose owner did not come back vanished from the listing, and
	/// <c>GetChannelAsync</c> answered "I don't recognize that channel" for one that plainly existed.
	/// A channel missing its owner is a broken invariant, not a missing channel — say so in the log and
	/// read it as God's, which is who <c>DeleteObjectAsync</c> would have given it to.
	/// </remarks>
	private async ValueTask<SharpChannel> MapRecordToChannelAsync(IRecord record, CancellationToken ct)
	{
		var channelNode = record["c"].As<INode>();

		if (record["ownerObj"] is not null && record["ownerTyped"] is not null)
		{
			var ownerObjNode = record["ownerObj"].As<INode>();
			var ownerTypedNode = record["ownerTyped"].As<INode>();

			if (ownerObjNode is not null && ownerTypedNode is not null)
			{
				return MapNodeToChannel(channelNode, BuildPlayer(
					PlayerId(ownerObjNode["key"].As<int>()), ownerTypedNode, MapNodeToSharpObject(ownerObjNode)));
			}
		}

		var channelName = channelNode["name"].As<string>();
		var owner = await GetChannelOwnerAsync(channelName, ct);

		if (owner is null)
		{
			logger.LogWarning(
				"Channel '{Channel}' has no owner; reading it as God's. A channel always has an owner, so "
				+ "this is data that predates DeleteObjectAsync handing ownership on.", channelName);
			owner = (await BuildTypedObjectFromKeyAsync(GodKey, ct)).AsPlayer;
		}

		return MapNodeToChannel(channelNode, owner);
	}

	private async ValueTask<AnyOptionalSharpObject> BuildTypedObjectFromKeyAsync(int key, CancellationToken ct)
	{
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) RETURN o", new { key }, ct);
		return await BuildTypedObjectFromObjectNode(result.Result[0]["o"].As<INode>(), ct);
	}

	private SharpChannel MapNodeToChannel(INode node, SharpPlayer owner)
	{
		var channelName = node["name"].As<string>();
		var markedUpName = node.Properties.ContainsKey("markedUpName")
		? node["markedUpName"].As<string>()
		: channelName;
		var description = node.Properties.ContainsKey("description") ? node["description"].As<string>() : "";

		return new SharpChannel
		{
			Id = ChannelId(channelName),
			Name = MModule.deserialize(markedUpName),
			Description = MModule.deserialize(description),
			Privs = node.Properties.ContainsKey("privs")
		? node["privs"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			JoinLock = node.Properties.ContainsKey("joinLock") ? node["joinLock"].As<string>() : "",
			SpeakLock = node.Properties.ContainsKey("speakLock") ? node["speakLock"].As<string>() : "",
			SeeLock = node.Properties.ContainsKey("seeLock") ? node["seeLock"].As<string>() : "",
			HideLock = node.Properties.ContainsKey("hideLock") ? node["hideLock"].As<string>() : "",
			ModLock = node.Properties.ContainsKey("modLock") ? node["modLock"].As<string>() : "",
			Buffer = node.Properties.ContainsKey("buffer") ? node["buffer"].As<int>() : 0,
			Mogrifier = node.Properties.ContainsKey("mogrifier") ? node["mogrifier"].As<string>() : "",
			Owner = new AsyncLazy<SharpPlayer>(_ => Task.FromResult(owner)),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
				new FreshAsyncEnumerable<SharpChannel.MemberAndStatus>(enumCt => GetChannelMembersAsync(channelName, enumCt)))
		};
	}

	private async ValueTask<SharpPlayer?> GetChannelOwnerAsync(string channelName, CancellationToken ct)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $name})-[:HAS_CHANNEL_OWNER]->(o:Object)
RETURN o
""", new { name = channelName }, ct);

		if (result.Result.Count == 0) return null;

		var objNode = result.Result[0]["o"].As<INode>();
		var ownerObj = await BuildTypedObjectFromObjectNode(objNode, ct);
		return ownerObj.AsPlayer;
	}

	private async IAsyncEnumerable<SharpChannel.MemberAndStatus> GetChannelMembersAsync(string channelName, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object)-[r:ON_CHANNEL]->(c:Channel {name: $name})
RETURN o, r
""", new { name = channelName }, ct);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var rel = record["r"].As<IRelationship>();
			var memberObj = await BuildTypedObjectFromObjectNode(objNode, ct);

			var status = new SharpChannelStatus(
			Combine: rel.Properties.ContainsKey("combine") ? rel["combine"].As<bool>() : false,
			Gagged: rel.Properties.ContainsKey("gagged") ? rel["gagged"].As<bool>() : false,
			Hide: rel.Properties.ContainsKey("hide") ? rel["hide"].As<bool>() : false,
			Mute: rel.Properties.ContainsKey("mute") ? rel["mute"].As<bool>() : false,
			Title: MModule.deserialize(
			rel.Properties.ContainsKey("title") ? rel["title"].As<string>() ?? "" : ""));

			yield return new SharpChannel.MemberAndStatus(memberObj.Known(), status);
		}
	}

	#endregion
}
