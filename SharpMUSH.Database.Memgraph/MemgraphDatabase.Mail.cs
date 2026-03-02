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
#region Mail

public async IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var playerKey = ExtractKey(id.Id!);
var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail {folder: $folder})
RETURN m
""", new { key = playerKey, folder }, cancellationToken);

foreach (var record in result.Result)
yield return MapNodeToMail(record["m"].As<INode>());
}

public async IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var playerKey = ExtractKey(id.Id!);
var result = await ExecuteWithRetryAsync("MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail) RETURN m", new { key = playerKey }, cancellationToken);

foreach (var record in result.Result)
yield return MapNodeToMail(record["m"].As<INode>());
}

public async ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default)
{
var playerKey = ExtractKey(id.Id!);
var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail {folder: $folder})
RETURN m
SKIP $skip LIMIT 1
""", new { key = playerKey, folder, skip = mail }, cancellationToken);

return result.Result.Count > 0 ? MapNodeToMail(result.Result[0]["m"].As<INode>()) : null;
}

public async IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var senderKey = sender.Key;
var recipientKey = ExtractKey(recipient.Id!);
var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $recipientKey})-[:RECEIVED_MAIL]->(m:Mail)-[:SENT_MAIL]->(sObj:Object {key: $senderKey})
RETURN m
""", new { senderKey, recipientKey }, cancellationToken);

foreach (var record in result.Result)
yield return MapNodeToMail(record["m"].As<INode>());
}

public async IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var senderKey = sender.Key;
var result = await ExecuteWithRetryAsync("MATCH (m:Mail)-[:SENT_MAIL]->(sObj:Object {key: $key}) RETURN m", new { key = senderKey }, cancellationToken);

foreach (var record in result.Result)
yield return MapNodeToMail(record["m"].As<INode>());
}

public async ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default)
{
var senderKey = sender.Key;
var recipientKey = ExtractKey(recipient.Id!);
var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $recipientKey})-[:RECEIVED_MAIL]->(m:Mail)-[:SENT_MAIL]->(sObj:Object {key: $senderKey})
RETURN m
SKIP $skip LIMIT 1
""", new { senderKey, recipientKey, skip = mail }, cancellationToken);

return result.Result.Count > 0 ? MapNodeToMail(result.Result[0]["m"].As<INode>()) : null;
}

public async ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
{
var playerKey = ExtractKey(id.Id!);
var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail)
RETURN DISTINCT m.folder AS folder
""", new { key = playerKey }, cancellationToken);

return result.Result.Select(r => r["folder"].As<string>()).ToArray();
}

public async ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default)
{
var fromKey = from.Key;
var toKey = ExtractKey(to.Id!);
var mailKey = Guid.NewGuid().ToString("N");

await ExecuteWithRetryAsync("""
CREATE (m:Mail {key: $mailKey, dateSent: $dateSent, fresh: $fresh, read: $read, tagged: $tagged,
urgent: $urgent, forwarded: $forwarded, cleared: $cleared, folder: $folder,
content: $content, subject: $subject})
""", new
{
mailKey,
dateSent = mail.DateSent.ToUnixTimeMilliseconds(),
fresh = mail.Fresh,
read = mail.Read,
tagged = mail.Tagged,
urgent = mail.Urgent,
forwarded = mail.Forwarded,
cleared = mail.Cleared,
folder = mail.Folder,
content = MarkupStringModule.serialize(mail.Content),
subject = MarkupStringModule.serialize(mail.Subject)
}, cancellationToken);

// RECEIVED_MAIL: Player -> Mail
await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $toKey}), (m:Mail {key: $mailKey})
CREATE (p)-[:RECEIVED_MAIL]->(m)
""", new { toKey, mailKey }, cancellationToken);

// SENT_MAIL: Mail -> Object (sender's Object node)
await ExecuteWithRetryAsync("""
MATCH (m:Mail {key: $mailKey}), (o:Object {key: $fromKey})
CREATE (m)-[:SENT_MAIL]->(o)
""", new { mailKey, fromKey }, cancellationToken);
}

public async ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default)
{
var mailKey = ExtractKeyString(mailId);
switch (commandMail)
{
case { IsReadEdit: true }:
await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.read = $val, m.fresh = false", new { key = mailKey, val = commandMail.AsReadEdit }, cancellationToken);
return;
case { IsClearEdit: true }:
await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.cleared = $val", new { key = mailKey, val = commandMail.AsClearEdit }, cancellationToken);
return;
case { IsTaggedEdit: true }:
await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.tagged = $val", new { key = mailKey, val = commandMail.AsTaggedEdit }, cancellationToken);
return;
case { IsUrgentEdit: true }:
await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.urgent = $val", new { key = mailKey, val = commandMail.AsUrgentEdit }, cancellationToken);
return;
}
}

public async ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
{
var mailKey = ExtractKeyString(mailId);
await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) DETACH DELETE m", new { key = mailKey }, cancellationToken);
}

public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
{
var playerKey = ExtractKey(player.Id!);
await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail {folder: $folder})
SET m.folder = $newFolder
""", new { key = playerKey, folder, newFolder }, cancellationToken);
}

public async ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
{
var mailKey = ExtractKeyString(mailId);
await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.folder = $newFolder", new { key = mailKey, newFolder }, cancellationToken);
}

public async IAsyncEnumerable<SharpMail> GetAllSystemMailAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
{
var result = await ExecuteWithRetryAsync("MATCH (m:Mail) RETURN m", ct: cancellationToken);
foreach (var record in result.Result)
yield return MapNodeToMail(record["m"].As<INode>());
}

private SharpMail MapNodeToMail(INode node)
{
var mailKey = node["key"].As<string>();
return new SharpMail
{
Id = MailId(mailKey),
DateSent = DateTimeOffset.FromUnixTimeMilliseconds(node["dateSent"].As<long>()),
Fresh = node["fresh"].As<bool>(),
Read = node["read"].As<bool>(),
Tagged = node["tagged"].As<bool>(),
Urgent = node["urgent"].As<bool>(),
Forwarded = node["forwarded"].As<bool>(),
Cleared = node["cleared"].As<bool>(),
Folder = node["folder"].As<string>(),
Content = MarkupStringModule.deserialize(node["content"].As<string>()),
Subject = MarkupStringModule.deserialize(node["subject"].As<string>()),
From = new AsyncLazy<AnyOptionalSharpObject>(async ct => await MailFromAsync(mailKey, ct))
};
}

private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string mailKey, CancellationToken ct)
{
var result = await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key})-[:SENT_MAIL]->(o:Object) RETURN o", new { key = mailKey }, ct);

if (result.Result.Count == 0) return new None();
var objNode = result.Result[0]["o"].As<INode>();
return await BuildTypedObjectFromObjectNode(objNode, ct);
}

#endregion
}
