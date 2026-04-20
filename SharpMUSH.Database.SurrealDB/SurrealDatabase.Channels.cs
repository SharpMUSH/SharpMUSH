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
	#region Channels

	public async IAsyncEnumerable<SharpChannel> GetAllChannelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM channel", cancellationToken);
		var results = response.GetValue<List<ChannelDbRecord>>(0)!;
		foreach (var element in results)
			yield return MapRecordToChannel(element);
	}

	public async ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name };
		var response = await ExecuteAsync(
			"SELECT * FROM channel WHERE name = $name",
			parameters, cancellationToken);

		var results = response.GetValue<List<ChannelDbRecord>>(0)!;
		return results.Count > 0 ? MapRecordToChannel(results[0]) : null;
	}

	public async IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var response = await ExecuteAsync(
			"SELECT * FROM channel WHERE id IN (SELECT VALUE out FROM member_of_channel WHERE in = type::thing('object', $key))",
			parameters, cancellationToken);

		var records = response.GetValue<List<ChannelDbRecord>>(0)!;
		foreach (var channelRecord in records)
			yield return MapRecordToChannel(channelRecord);
	}

	public async ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		var channelName = name.ToPlainText();
		var serializedName = MModule.serialize(name);
		var ownerObjKey = owner.Object.Key;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = channelName,
			["markedUpName"] = serializedName,
			["privs"] = privs,
			["ownerKey"] = ownerObjKey
		};

		await ExecuteAsync(
			"LET $ch = (CREATE channel SET name = $name, markedUpName = $markedUpName, description = '', privs = $privs, joinLock = '', speakLock = '', seeLock = '', hideLock = '', modLock = '', buffer = 0, mogrifier = '');" +
			"RELATE $ch[0].id->owner_of_channel->type::thing('object', $ownerKey);" +
			"RELATE type::thing('object', $ownerKey)->member_of_channel SET combine = false, gagged = false, hide = false, mute = false, title = '' CONTENT { out: $ch[0].id }",
			parameters, cancellationToken);
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel, MString? name, MString? description, string[]? privs,
		string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock,
		string? mogrifier, int? buffer, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var newName = name is not null ? name.ToPlainText() : channelName;
		var newMarkedUpName = name is not null ? MModule.serialize(name) : MModule.serialize(channel.Name);
		var newDescription = description is not null ? MModule.serialize(description) : MModule.serialize(channel.Description);

		var parameters = new Dictionary<string, object?>
		{
			["oldName"] = channelName,
			["newName"] = newName,
			["markedUpName"] = newMarkedUpName,
			["description"] = newDescription,
			["privs"] = privs ?? channel.Privs,
			["joinLock"] = joinLock ?? channel.JoinLock ?? "",
			["speakLock"] = speakLock ?? channel.SpeakLock ?? "",
			["seeLock"] = seeLock ?? channel.SeeLock ?? "",
			["hideLock"] = hideLock ?? channel.HideLock ?? "",
			["modLock"] = modLock ?? channel.ModLock ?? "",
			["buffer"] = buffer ?? channel.Buffer,
			["mogrifier"] = mogrifier ?? channel.Mogrifier ?? ""
		};

		await ExecuteAsync(
			"UPDATE channel SET name = $newName, markedUpName = $markedUpName, description = $description, privs = $privs, joinLock = $joinLock, speakLock = $speakLock, seeLock = $seeLock, hideLock = $hideLock, modLock = $modLock, buffer = $buffer, mogrifier = $mogrifier WHERE name = $oldName",
			parameters, cancellationToken);
	}

	public async ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var ownerObjKey = newOwner.Object.Key;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = channelName,
			["ownerKey"] = ownerObjKey
		};

		await ExecuteAsync(
			"LET $ch = (SELECT id FROM channel WHERE name = $name);" +
			"DELETE owner_of_channel WHERE in = $ch[0].id;" +
			"RELATE $ch[0].id->owner_of_channel->type::thing('object', $ownerKey)",
			parameters, cancellationToken);
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var parameters = new Dictionary<string, object?> { ["name"] = channelName };

		await ExecuteAsync(
			"LET $ch = (SELECT id FROM channel WHERE name = $name);" +
			"DELETE member_of_channel WHERE out = $ch[0].id;" +
			"DELETE owner_of_channel WHERE in = $ch[0].id;" +
			"DELETE channel WHERE name = $name",
			parameters, cancellationToken);
	}

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = channelName,
			["key"] = objKey
		};

		await ExecuteAsync(
			"LET $ch = (SELECT id FROM channel WHERE name = $name);" +
			"RELATE type::thing('object', $key)->member_of_channel SET combine = false, gagged = false, hide = false, mute = false, title = '' CONTENT { out: $ch[0].id }",
			parameters, cancellationToken);
	}

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = channelName,
			["key"] = objKey
		};

		await ExecuteAsync(
			"LET $ch = (SELECT id FROM channel WHERE name = $name);" +
			"DELETE member_of_channel WHERE in = type::thing('object', $key) AND out = $ch[0].id",
			parameters, cancellationToken);
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;

		var setClauses = new List<string>();
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["name"] = channelName
		};

		if (status.Combine is { } combine)
		{
			setClauses.Add("combine = $combine");
			parameters["combine"] = combine;
		}
		if (status.Gagged is { } gagged)
		{
			setClauses.Add("gagged = $gagged");
			parameters["gagged"] = gagged;
		}
		if (status.Hide is { } hide)
		{
			setClauses.Add("hide = $hide");
			parameters["hide"] = hide;
		}
		if (status.Mute is { } mute)
		{
			setClauses.Add("mute = $mute");
			parameters["mute"] = mute;
		}
		if (status.Title is { } title)
		{
			setClauses.Add("title = $title");
			parameters["title"] = MModule.serialize(title);
		}

		if (setClauses.Count == 0) return;

		var query =
			"LET $ch = (SELECT id FROM channel WHERE name = $name);" +
			$"UPDATE member_of_channel SET {string.Join(", ", setClauses)} WHERE in = type::thing('object', $key) AND out = $ch[0].id";

		await ExecuteAsync(query, parameters, cancellationToken);
	}

	private SharpChannel MapRecordToChannel(ChannelDbRecord record)
	{
		var channelName = record.name;
		var markedUpName = string.IsNullOrEmpty(record.markedUpName) ? channelName : record.markedUpName;
		var description = record.description;

		return new SharpChannel
		{
			Id = ChannelId(channelName),
			Name = MModule.deserialize(markedUpName),
			Description = MModule.deserialize(description),
			Privs = record.privs,
			JoinLock = record.joinLock,
			SpeakLock = record.speakLock,
			SeeLock = record.seeLock,
			HideLock = record.hideLock,
			ModLock = record.modLock,
			Buffer = record.buffer,
			Mogrifier = record.mogrifier,
			Owner = new AsyncLazy<SharpPlayer>(async ct => await GetChannelOwnerAsync(channelName, ct)),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
				GetChannelMembersAsync(channelName, CancellationToken.None))
		};
	}

	private async ValueTask<SharpPlayer> GetChannelOwnerAsync(string channelName, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = channelName };
		var response = await ExecuteAsync(
			"SELECT VALUE out.key FROM owner_of_channel WHERE in.name = $name",
			parameters, ct);

		var ownerKeys = response.GetValue<List<int>>(0)!;
		if (ownerKeys.Count == 0)
			throw new InvalidOperationException($"No owner found for channel '{channelName}'");

		var ownerKey = ownerKeys[0];
		var typed = await BuildTypedObjectFromKey(ownerKey, ct);
		return typed.AsPlayer;
	}

	private async IAsyncEnumerable<SharpChannel.MemberAndStatus> GetChannelMembersAsync(string channelName, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = channelName };
		var response = await ExecuteAsync(
			"LET $ch = (SELECT id FROM channel WHERE name = $name);" +
			"SELECT *, in.key AS memberKey FROM member_of_channel WHERE out = $ch[0].id",
			parameters, ct);

		// The second statement (index 1) contains the edge records
		var records = response.GetValue<List<ChannelMemberEdgeRecord>>(1)!;
		foreach (var record in records)
		{
			var memberKey = record.memberKey;
			var memberObj = await BuildTypedObjectFromKey(memberKey, ct);
			if (memberObj.IsNone) continue;

			var status = new SharpChannelStatus(
				Combine: record.combine,
				Gagged: record.gagged,
				Hide: record.hide,
				Mute: record.mute,
				Title: MModule.deserialize(record.title));

			yield return new SharpChannel.MemberAndStatus(memberObj.Known(), status);
		}
	}

	#endregion
}
