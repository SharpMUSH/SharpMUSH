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
	#region Mail

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

	/// <summary>
	/// Gets ALL mail in the system regardless of owner.
	/// WARNING: This bypasses all access controls and should only be used in administrative operations.
	/// </summary>
	public IAsyncEnumerable<SharpMail> GetAllSystemMailAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpMailQueryResult>(handle,
			$"FOR v IN {DatabaseConstants.Mails} RETURN v",
			cancellationToken: ct).Select(ConvertMailQueryResult);

	private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string id, CancellationToken ct = default)
	{
		var edges = await arangoDb.Query.ExecuteAsync<SharpEdgeQueryResult>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphMail} RETURN e", cancellationToken: ct);

		return edges switch
		{
			null or [] => new None(),
			[var edge, ..] => await GetObjectNodeAsync(edge.To, CancellationToken.None)
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

	#endregion
}
