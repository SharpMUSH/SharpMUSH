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
			"SELECT ->received_mail->mail.* AS mails FROM type::thing('player', $key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) yield break;

		var mailsArray = records[0].GetProperty("mails");
		if (mailsArray.ValueKind != JsonValueKind.Array) yield break;

		foreach (var mailElement in mailsArray.EnumerateArray())
		{
			if (GetStringOrDefault(mailElement, "folder") == folder)
				yield return MapElementToMail(mailElement);
		}
	}

	public async IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = playerKey };

		var response = await ExecuteAsync(
			"SELECT ->received_mail->mail.* AS mails FROM type::thing('player', $key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) yield break;

		var mailsArray = records[0].GetProperty("mails");
		if (mailsArray.ValueKind != JsonValueKind.Array) yield break;

		foreach (var mailElement in mailsArray.EnumerateArray())
			yield return MapElementToMail(mailElement);
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
			"SELECT ->received_mail->mail.* AS mails FROM type::thing('player', $key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) return null;

		var mailsArray = records[0].GetProperty("mails");
		if (mailsArray.ValueKind != JsonValueKind.Array) return null;

		var folderMails = mailsArray.EnumerateArray()
			.Where(m => GetStringOrDefault(m, "folder") == folder)
			.ToList();

		if (mail >= folderMails.Count) return null;
		return MapElementToMail(folderMails[mail]);
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
			"SELECT ->received_mail->mail.* AS mails FROM type::thing('player', $recipientKey)",
			parameters, cancellationToken);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) yield break;

		var mailsArray = records[0].GetProperty("mails");
		if (mailsArray.ValueKind != JsonValueKind.Array) yield break;

		foreach (var mailElement in mailsArray.EnumerateArray())
		{
			// Check if this mail was sent by the sender
			var mailKey = GetStringOrDefault(mailElement, "key");
			var senderParams = new Dictionary<string, object?> { ["mailKey"] = mailKey };
			var senderResponse = await ExecuteAsync(
				"SELECT ->mail_sender->object.key AS senderKeys FROM mail WHERE key = $mailKey",
				senderParams, cancellationToken);

			var senderRecords = senderResponse.GetValue<List<JsonElement>>(0)!;
			if (senderRecords.Count > 0)
			{
				var senderKeysArray = senderRecords[0].GetProperty("senderKeys");
				if (senderKeysArray.ValueKind == JsonValueKind.Array)
				{
					foreach (var sk in senderKeysArray.EnumerateArray())
					{
						if (sk.GetInt32() == senderKey)
						{
							yield return MapElementToMail(mailElement);
							break;
						}
					}
				}
			}
		}
	}

	public async IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var senderKey = sender.Key;
		var parameters = new Dictionary<string, object?> { ["key"] = senderKey };

		var response = await ExecuteAsync(
			"SELECT * FROM mail WHERE key IN (SELECT VALUE in.key FROM mail_sender WHERE out = type::thing('object', $key))",
			parameters, cancellationToken);

		var results = response.GetValue<List<JsonElement>>(0)!;
		foreach (var element in results)
			yield return MapElementToMail(element);
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
			"SELECT ->received_mail->mail.folder AS folders FROM type::thing('player', $key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) return [];

		var foldersArray = records[0].GetProperty("folders");
		if (foldersArray.ValueKind != JsonValueKind.Array) return [];

		return foldersArray.EnumerateArray()
			.Select(f => f.GetString() ?? "")
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

		await ExecuteAsync(
			"LET $m = (CREATE mail SET key = $mailKey, dateSent = $dateSent, fresh = $fresh, read = $read, tagged = $tagged, urgent = $urgent, forwarded = $forwarded, cleared = $cleared, folder = $folder, content = $content, subject = $subject);" +
			"RELATE type::thing('player', $toKey)->received_mail->$m[0].id;" +
			"RELATE $m[0].id->mail_sender->type::thing('object', $fromKey)",
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
				await ExecuteAsync("UPDATE mail SET read = $val, fresh = false WHERE key = $key", parameters, cancellationToken);
				return;
			case { IsClearEdit: true }:
				parameters["val"] = commandMail.AsClearEdit;
				await ExecuteAsync("UPDATE mail SET cleared = $val WHERE key = $key", parameters, cancellationToken);
				return;
			case { IsTaggedEdit: true }:
				parameters["val"] = commandMail.AsTaggedEdit;
				await ExecuteAsync("UPDATE mail SET tagged = $val WHERE key = $key", parameters, cancellationToken);
				return;
			case { IsUrgentEdit: true }:
				parameters["val"] = commandMail.AsUrgentEdit;
				await ExecuteAsync("UPDATE mail SET urgent = $val WHERE key = $key", parameters, cancellationToken);
				return;
		}
	}

	public async ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		var parameters = new Dictionary<string, object?> { ["key"] = mailKey };

		await ExecuteAsync(
			"LET $m = (SELECT id FROM mail WHERE key = $key);" +
			"DELETE received_mail WHERE out = $m[0].id;" +
			"DELETE mail_sender WHERE in = $m[0].id;" +
			"DELETE mail WHERE key = $key",
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

		// Get all mail IDs received by this player in the specified folder
		var response = await ExecuteAsync(
			"SELECT ->received_mail->mail.* AS mails FROM type::thing('player', $key)",
			parameters, cancellationToken);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) return;

		var mailsArray = records[0].GetProperty("mails");
		if (mailsArray.ValueKind != JsonValueKind.Array) return;

		foreach (var mailElement in mailsArray.EnumerateArray())
		{
			if (GetStringOrDefault(mailElement, "folder") == folder)
			{
				var mKey = GetStringOrDefault(mailElement, "key");
				var updateParams = new Dictionary<string, object?>
				{
					["mailKey"] = mKey,
					["newFolder"] = newFolder
				};
				await ExecuteAsync("UPDATE mail SET folder = $newFolder WHERE key = $mailKey", updateParams, cancellationToken);
			}
		}
	}

	public async ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = mailKey,
			["newFolder"] = newFolder
		};
		await ExecuteAsync("UPDATE mail SET folder = $newFolder WHERE key = $key", parameters, cancellationToken);
	}

	public async IAsyncEnumerable<SharpMail> GetAllSystemMailAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM mail", cancellationToken);
		var results = response.GetValue<List<JsonElement>>(0)!;
		foreach (var element in results)
			yield return MapElementToMail(element);
	}

	private SharpMail MapElementToMail(JsonElement element)
	{
		var mailKey = GetStringOrDefault(element, "key");
		return new SharpMail
		{
			Id = MailId(mailKey),
			DateSent = DateTimeOffset.FromUnixTimeMilliseconds(GetLongOrDefault(element, "dateSent")),
			Fresh = GetBoolOrDefault(element, "fresh"),
			Read = GetBoolOrDefault(element, "read"),
			Tagged = GetBoolOrDefault(element, "tagged"),
			Urgent = GetBoolOrDefault(element, "urgent"),
			Forwarded = GetBoolOrDefault(element, "forwarded"),
			Cleared = GetBoolOrDefault(element, "cleared"),
			Folder = GetStringOrDefault(element, "folder"),
			Content = MModule.deserialize(GetStringOrDefault(element, "content")),
			Subject = MModule.deserialize(GetStringOrDefault(element, "subject")),
			From = new AsyncLazy<AnyOptionalSharpObject>(async ct => await MailFromAsync(mailKey, ct))
		};
	}

	private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string mailKey, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = mailKey };
		var response = await ExecuteAsync(
			"SELECT ->mail_sender->object.key AS senderKeys FROM mail WHERE key = $key",
			parameters, ct);

		var records = response.GetValue<List<JsonElement>>(0)!;
		if (records.Count == 0) return new None();

		var senderKeysArray = records[0].GetProperty("senderKeys");
		if (senderKeysArray.ValueKind != JsonValueKind.Array || senderKeysArray.GetArrayLength() == 0)
			return new None();

		var senderKey = senderKeysArray[0].GetInt32();
		return await BuildTypedObjectFromKey(senderKey, ct);
	}

	#endregion
}
