using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;
using SurrealDb.Net.Models.Response;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	#region Mail

	public async IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = playerKey,
			["folder"] = folder
		};

		var response = await ExecuteAsync(
			"SELECT * FROM mail WHERE folder = $folder AND id IN (SELECT VALUE out FROM received_mail WHERE in = player:$key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<MailDbRecord>>(0)!;
		foreach (var record in records)
			yield return MapRecordToMail(record);
	}

	public async IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = playerKey };

		var response = await ExecuteAsync(
			"SELECT * FROM player:$key->received_mail->mail",
			parameters, cancellationToken);

		var records = response.GetValue<List<MailDbRecord>>(0)!;
		foreach (var record in records)
			yield return MapRecordToMail(record);
	}

	public async ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = playerKey,
			["folder"] = folder
		};

		var response = await ExecuteAsync(
			"SELECT * FROM mail WHERE folder = $folder AND id IN (SELECT VALUE out FROM received_mail WHERE in = player:$key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<MailDbRecord>>(0)!;
		if (mail >= records.Count) return null;
		return MapRecordToMail(records[mail]);
	}

	public async IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var senderKey = sender.Key;
		var recipientKey = ExtractKey(recipient.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["senderKey"] = senderKey,
			["recipientKey"] = recipientKey
		};

		// Get mails received by recipient that were sent by sender
		var response = await ExecuteAsync(
			"SELECT * FROM mail WHERE id IN (SELECT VALUE out FROM received_mail WHERE in = player:$recipientKey) AND id IN (SELECT VALUE in FROM mail_sender WHERE out = object:$senderKey)",
			parameters, cancellationToken);

		var records = response.GetValue<List<MailDbRecord>>(0)!;
		foreach (var record in records)
			yield return MapRecordToMail(record);
	}

	public async IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var senderKey = sender.Key;
		var parameters = new Dictionary<string, object?> { ["key"] = senderKey };

		var response = await ExecuteAsync(
			"SELECT * FROM object:$key<-mail_sender<-mail",
			parameters, cancellationToken);

		var results = response.GetValue<List<MailDbRecord>>(0)!;
		foreach (var record in results)
			yield return MapRecordToMail(record);
	}

	public async ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default)
	{
		var sentMails = new List<SharpMail>();
		await foreach (var m in GetSentMailsAsync(sender, recipient, cancellationToken))
			sentMails.Add(m);

		if (mail >= sentMails.Count) return null;
		return sentMails[mail];
	}

	public async ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = playerKey };

		var response = await ExecuteAsync(
			"SELECT VALUE folder FROM player:$key->received_mail->mail",
			parameters, cancellationToken);

		var folders = response.GetValue<List<string>>(0)!;
		return folders
			.Where(f => !string.IsNullOrEmpty(f))
			.Distinct()
			.ToArray();
	}

	public async ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default)
	{
		var fromKey = from.Key;
		var toKey = ExtractKey(to.Id!);
		var mailKey = Guid.NewGuid().ToString("N");

		var parameters = new Dictionary<string, object?>
		{
			["mailKey"] = mailKey,
			["dateSent"] = mail.DateSent.ToUnixTimeMilliseconds(),
			["fresh"] = mail.Fresh,
			["read"] = mail.Read,
			["tagged"] = mail.Tagged,
			["urgent"] = mail.Urgent,
			["forwarded"] = mail.Forwarded,
			["cleared"] = mail.Cleared,
			["folder"] = mail.Folder,
			["content"] = MModule.serialize(mail.Content),
			["subject"] = MModule.serialize(mail.Subject),
			["fromKey"] = fromKey,
			["toKey"] = toKey
		};

		// Create mail using the same deterministic record ID used by later record-id-based operations
		await ExecuteAsync(
			"UPSERT mail:⟨$mailKey⟩ SET key = $mailKey, dateSent = $dateSent, fresh = $fresh, read = $read, tagged = $tagged, urgent = $urgent, forwarded = $forwarded, cleared = $cleared, folder = $folder, content = $content, subject = $subject",
			parameters, cancellationToken);

		await ExecuteAsync(
			"RELATE player:$toKey->received_mail->mail:⟨$mailKey⟩;" +
			"RELATE mail:⟨$mailKey⟩->mail_sender->object:$fromKey",
			parameters, cancellationToken);
	}

	public async ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		var parameters = new Dictionary<string, object?> { ["key"] = mailKey };

		switch (commandMail)
		{
			case { IsReadEdit: true }:
				parameters["val"] = commandMail.AsReadEdit;
				await ExecuteAsync("UPDATE mail:⟨$key⟩ SET read = $val, fresh = false", parameters, cancellationToken);
				return;
			case { IsClearEdit: true }:
				parameters["val"] = commandMail.AsClearEdit;
				await ExecuteAsync("UPDATE mail:⟨$key⟩ SET cleared = $val", parameters, cancellationToken);
				return;
			case { IsTaggedEdit: true }:
				parameters["val"] = commandMail.AsTaggedEdit;
				await ExecuteAsync("UPDATE mail:⟨$key⟩ SET tagged = $val", parameters, cancellationToken);
				return;
			case { IsUrgentEdit: true }:
				parameters["val"] = commandMail.AsUrgentEdit;
				await ExecuteAsync("UPDATE mail:⟨$key⟩ SET urgent = $val", parameters, cancellationToken);
				return;
		}
	}

	public async ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		var parameters = new Dictionary<string, object?> { ["key"] = mailKey };

		await ExecuteAsync(
			"DELETE received_mail WHERE out = mail:⟨$key⟩;" +
			"DELETE mail_sender WHERE in = mail:⟨$key⟩;" +
			"DELETE mail:⟨$key⟩",
			parameters, cancellationToken);
	}

	public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(player.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = playerKey,
			["folder"] = folder,
			["newFolder"] = newFolder
		};

		// Update all mail in the folder for this player directly
		await ExecuteAsync(
			"UPDATE mail SET folder = $newFolder WHERE folder = $folder AND id IN (SELECT VALUE out FROM received_mail WHERE in = player:$key)",
			parameters, cancellationToken);
	}

	public async ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = mailKey,
			["newFolder"] = newFolder
		};
		await ExecuteAsync("UPDATE mail:⟨$key⟩ SET folder = $newFolder", parameters, cancellationToken);
	}

	public async IAsyncEnumerable<SharpMail> GetAllSystemMailAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM mail", cancellationToken);
		var results = response.GetValue<List<MailDbRecord>>(0)!;
		foreach (var record in results)
			yield return MapRecordToMail(record);
	}

	private SharpMail MapRecordToMail(MailDbRecord record)
	{
		var mailKey = record.key;
		return new SharpMail
		{
			Id = MailId(mailKey),
			DateSent = DateTimeOffset.FromUnixTimeMilliseconds(record.dateSent),
			Fresh = record.fresh,
			Read = record.read,
			Tagged = record.tagged,
			Urgent = record.urgent,
			Forwarded = record.forwarded,
			Cleared = record.cleared,
			Folder = record.folder,
			Content = MModule.deserialize(record.content),
			Subject = MModule.deserialize(record.subject),
			From = new AsyncLazy<AnyOptionalSharpObject>(async ct => await MailFromAsync(mailKey, ct))
		};
	}

	private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string mailKey, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = mailKey };
		var response = await ExecuteAsync(
			"SELECT VALUE out.key FROM mail_sender WHERE in = mail:⟨$key⟩",
			parameters, ct);

		var senderKeys = response.GetValue<List<int>>(0)!;
		if (senderKeys.Count == 0) return new None();

		var senderKey = senderKeys[0];
		return await BuildTypedObjectFromKey(senderKey, ct);
	}

	#endregion
}
