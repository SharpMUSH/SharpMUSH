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
using MString = MarkupString.MarkupStringModule.MarkupString;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
#region Channels

public async IAsyncEnumerable<SharpChannel> GetAllChannelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var result = await ExecuteWithRetryAsync("MATCH (c:Channel) RETURN c", ct: cancellationToken);
foreach (var record in result.Result)
yield return MapNodeToChannel(record["c"].As<INode>());
}

public async ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
{
var result = await ExecuteWithRetryAsync("MATCH (c:Channel {name: $name}) RETURN c", new { name }, cancellationToken);
return result.Result.Count > 0 ? MapNodeToChannel(result.Result[0]["c"].As<INode>()) : null;
}

public async IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var objKey = obj.Object().Key;
var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:ON_CHANNEL]->(c:Channel)
RETURN c
""", new { key = objKey }, cancellationToken);

foreach (var record in result.Result)
yield return MapNodeToChannel(record["c"].As<INode>());
}

public async ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default)
{
var channelName = name.ToPlainText();
var serializedName = MarkupStringModule.serialize(name);
var ownerObjKey = owner.Object.Key;

// Single atomic query: create channel, link owner, add owner as member
await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $ownerKey})
CREATE (c:Channel {name: $name, markedUpName: $markedUpName, description: '', privs: $privs,
joinLock: '', speakLock: '', seeLock: '', hideLock: '', modLock: '',
buffer: 0, mogrifier: ''})
CREATE (c)-[:HAS_CHANNEL_OWNER]->(o)
CREATE (o)-[:ON_CHANNEL {combine: false, gagged: false, hide: false, mute: false, title: ''}]->(c)
""", new { name = channelName, markedUpName = serializedName, privs, ownerKey = ownerObjKey }, cancellationToken);
}

public async ValueTask UpdateChannelAsync(SharpChannel channel, MString? name, MString? description, string[]? privs,
string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock,
string? mogrifier, int? buffer, CancellationToken cancellationToken = default)
{
var channelName = channel.Name.ToPlainText();
var newName = name is not null ? name.ToPlainText() : channelName;
var newMarkedUpName = name is not null ? MarkupStringModule.serialize(name) : MarkupStringModule.serialize(channel.Name);
var newDescription = description is not null ? MarkupStringModule.serialize(description) : MarkupStringModule.serialize(channel.Description);

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

await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $name})-[r:HAS_CHANNEL_OWNER]->()
DELETE r
WITH c
MATCH (o:Object {key: $ownerKey})
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
parameters["title"] = MarkupStringModule.serialize(title);
}

if (setClauses.Count == 0) return;

var cypher = "MATCH (o:Object {key: $key})-[r:ON_CHANNEL]->(c:Channel {name: $name}) SET " +
string.Join(", ", setClauses);

await ExecuteWithRetryAsync(cypher, parameters, cancellationToken);
}

private SharpChannel MapNodeToChannel(INode node)
{
var channelName = node["name"].As<string>();
var markedUpName = node.Properties.ContainsKey("markedUpName")
? node["markedUpName"].As<string>()
: channelName;
var description = node.Properties.ContainsKey("description") ? node["description"].As<string>() : "";

return new SharpChannel
{
Id = ChannelId(channelName),
Name = MarkupStringModule.deserialize(markedUpName),
Description = MarkupStringModule.deserialize(description),
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
Owner = new AsyncLazy<SharpPlayer>(async ct => await GetChannelOwnerAsync(channelName, ct)),
Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
GetChannelMembersAsync(channelName, CancellationToken.None))
};
}

private async ValueTask<SharpPlayer> GetChannelOwnerAsync(string channelName, CancellationToken ct)
{
var result = await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $name})-[:HAS_CHANNEL_OWNER]->(o:Object)
RETURN o
""", new { name = channelName }, ct);

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
Title: MarkupStringModule.deserialize(
rel.Properties.ContainsKey("title") ? rel["title"].As<string>() ?? "" : ""));

yield return new SharpChannel.MemberAndStatus(memberObj.Known(), status);
}
}

#endregion
}
