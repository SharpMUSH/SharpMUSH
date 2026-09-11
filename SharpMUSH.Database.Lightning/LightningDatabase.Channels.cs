using System.Runtime.CompilerServices;
using DotNext.Threading;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IChannelStore"/>: chat channels and memberships. Ported from
/// <c>SurrealDatabase.Channels.cs</c>. There is no <c>owner_of_channel</c>/<c>member_of_channel</c> graph
/// here — <see cref="Tables.Chan"/> is keyed <c>Keys.Upper(name)</c> with the owner as a plain dbref field
/// on <see cref="ChannelRecord"/>; <see cref="Tables.ChanMember"/> is keyed
/// <c>Keys.Composite(NAME, dbref)</c> so every member of one channel is a single prefix range; and
/// <see cref="Tables.RevChanMember"/> is keyed by the member's dbref (duplicates) so
/// <c>LightningDatabase.Objects.cs</c>'s object-delete cascade can find and drop every channel a deleted
/// object belonged to without scanning every channel.
///
/// <para><c>CreateChannelAsync</c> needs no lock beyond the store's own single-writer serialization:
/// unlike SurrealDB's optimistic embedded engine, only one job runs on the LMDB writer thread at a time,
/// so a <c>TryGet</c> immediately followed by a <c>Put</c> inside one job is already atomic with respect
/// to every other create.</para>
/// </summary>
public partial class LightningDatabase
{
	private static byte[] ChanKey(string name) => Keys.Upper(name);

	private static byte[] ChanMemberKey(string upperName, long memberDbref) => Keys.Composite(upperName, memberDbref);

	private static byte[] ChanMemberPrefix(byte[] upperNameKey) => Keys.Concat(upperNameKey, Keys.Sep);

	private SharpChannel MapRecordToChannel(ChannelRecord record)
	{
		var channelName = record.Name;
		var key = ChanKey(channelName);

		return new SharpChannel
		{
			Id = $"Channel/{channelName}",
			Name = MarkupTextSerializer.Deserialize(record.MarkedUpName),
			Description = MarkupTextSerializer.Deserialize(record.Description),
			Privs = record.Privs,
			JoinLock = record.JoinLock,
			SpeakLock = record.SpeakLock,
			SeeLock = record.SeeLock,
			HideLock = record.HideLock,
			ModLock = record.ModLock,
			Buffer = record.Buffer,
			Mogrifier = record.Mogrifier,
			Owner = new AsyncLazy<SharpPlayer>(_ => Task.FromResult(LoadChannelOwner(record.Owner, channelName))),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
				new FreshAsyncEnumerable<SharpChannel.MemberAndStatus>(ct => GetChannelMembersCoreAsync(key, ct)))
		};
	}

	private SharpPlayer LoadChannelOwner(long ownerDbref, string channelName) => Store.Read(tx =>
	{
		var found = ReadObject(tx, ownerDbref)
			?? throw new InvalidOperationException($"No owner found for channel '{channelName}'");
		return Hydrate(found.Dbref, found.Record).AsPlayer;
	});

	private async IAsyncEnumerable<SharpChannel.MemberAndStatus> GetChannelMembersCoreAsync(byte[] key,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var prefix = ChanMemberPrefix(key);

		var members = Store.Read(tx => tx.Range(Tables.ChanMember, prefix)
			.Select(entry =>
			{
				var memberDbref = Keys.ReadDbref(entry.Key.AsSpan(entry.Key.Length - 8, 8));
				var found = ReadObject(tx, memberDbref);
				if (found is null)
				{
					return null;
				}

				var memberRecord = Codec.Deserialize<ChannelMemberRecord>(entry.Value);
				var status = new SharpChannelStatus(
					Combine: memberRecord.Combine,
					Gagged: memberRecord.Gagged,
					Hide: memberRecord.Hide,
					Mute: memberRecord.Mute,
					Title: MarkupTextSerializer.Deserialize(memberRecord.Title));

				return new SharpChannel.MemberAndStatus(Hydrate(found.Value.Dbref, found.Value.Record), status);
			})
			.Where(entry => entry is not null)
			.Select(entry => entry!)
			.ToList());

		foreach (var member in members)
		{
			ct.ThrowIfCancellationRequested();
			yield return member;
		}
	}

	public IAsyncEnumerable<SharpChannel> GetAllChannelsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpChannel>(ct => GetAllChannelsCoreAsync(ct));

	private async IAsyncEnumerable<SharpChannel> GetAllChannelsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (_, value) in Store.RangeAsync(Tables.Chan, [], ct: ct))
		{
			yield return MapRecordToChannel(Codec.Deserialize<ChannelRecord>(value));
		}
	}

	public ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.Chan, ChanKey(name), out var bytes)
			? MapRecordToChannel(Codec.Deserialize<ChannelRecord>(bytes))
			: null);
		return ValueTask.FromResult(result);
	}

	public IAsyncEnumerable<SharpChannel> GetChannelsOwnedByAsync(DBRef owner, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpChannel>(ct => GetChannelsOwnedByCoreAsync(owner.Number, ct));

	private async IAsyncEnumerable<SharpChannel> GetChannelsOwnedByCoreAsync(int ownerNumber,
		[EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (_, value) in Store.RangeAsync(Tables.Chan, [], ct: ct))
		{
			var record = Codec.Deserialize<ChannelRecord>(value);
			if (record.Owner == ownerNumber)
			{
				yield return MapRecordToChannel(record);
			}
		}
	}

	public IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpChannel>(ct => GetMemberChannelsCoreAsync((long)obj.Object().Key, ct));

	private async IAsyncEnumerable<SharpChannel> GetMemberChannelsCoreAsync(long dbref,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var channels = Store.Read(tx => tx.Dups(Tables.RevChanMember, Keys.Dbref(dbref))
			.Select(upperNameBytes => tx.TryGet(Tables.Chan, upperNameBytes, out var chanBytes)
				? Codec.Deserialize<ChannelRecord>(chanBytes)
				: null)
			.Where(record => record is not null)
			.Select(record => MapRecordToChannel(record!))
			.ToList());

		foreach (var channel in channels)
		{
			ct.ThrowIfCancellationRequested();
			yield return channel;
		}
	}

	public async ValueTask<ChannelCreationResult> CreateChannelAsync(MString name, string[] privs, SharpPlayer owner,
		CancellationToken cancellationToken = default)
	{
		var channelName = name.ToPlainText();
		var markedUpName = MarkupTextSerializer.Serialize(name);
		var ownerKey = (long)owner.Object.Key;
		var upperName = channelName.ToUpperInvariant();
		var key = ChanKey(channelName);

		return await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.Chan, key, out _))
			{
				return (ChannelCreationResult)new ChannelNameTaken();
			}

			var record = new ChannelRecord
			{
				Name = channelName,
				MarkedUpName = markedUpName,
				Description = "",
				Privs = privs,
				JoinLock = "",
				SpeakLock = "",
				SeeLock = "",
				HideLock = "",
				ModLock = "",
				Mogrifier = "",
				Buffer = 0,
				Owner = ownerKey
			};
			tx.Put(Tables.Chan, key, Codec.Serialize(record));

			var memberRecord = new ChannelMemberRecord { Gagged = false, Mute = false, Hide = false, Combine = false, Title = "" };
			tx.Put(Tables.ChanMember, ChanMemberKey(upperName, ownerKey), Codec.Serialize(memberRecord));
			tx.Put(Tables.RevChanMember, Keys.Dbref(ownerKey), key);

			return (ChannelCreationResult)new Success();
		}, cancellationToken);
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel,
		MString? name,
		MString? description,
		string[]? privs,
		string? joinLock,
		string? speakLock,
		string? seeLock,
		string? hideLock,
		string? modLock,
		string? mogrifier,
		int? buffer, CancellationToken cancellationToken = default)
	{
		var oldName = channel.Name.ToPlainText();
		var oldKey = ChanKey(oldName);
		var newName = name is not null ? name.ToPlainText() : oldName;
		var newKey = ChanKey(newName);
		var newMarkedUpName = name is not null ? MarkupTextSerializer.Serialize(name) : MarkupTextSerializer.Serialize(channel.Name);
		var newDescription = description is not null ? MarkupTextSerializer.Serialize(description) : MarkupTextSerializer.Serialize(channel.Description);

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Chan, oldKey, out var bytes))
			{
				return;
			}

			var record = Codec.Deserialize<ChannelRecord>(bytes);
			var updated = record with
			{
				Name = newName,
				MarkedUpName = newMarkedUpName,
				Description = newDescription,
				Privs = privs ?? record.Privs,
				JoinLock = joinLock ?? record.JoinLock,
				SpeakLock = speakLock ?? record.SpeakLock,
				SeeLock = seeLock ?? record.SeeLock,
				HideLock = hideLock ?? record.HideLock,
				ModLock = modLock ?? record.ModLock,
				Buffer = buffer ?? record.Buffer,
				Mogrifier = mogrifier ?? record.Mogrifier
			};

			if (oldKey.AsSpan().SequenceEqual(newKey))
			{
				tx.Put(Tables.Chan, oldKey, Codec.Serialize(updated));
				return;
			}

			// The channel's identity is its key, so a rename re-keys the channel row, every membership
			// row under the old name, and every reverse-index entry that points at it — otherwise the
			// channel becomes unreachable under its new name while its members still point at the old one.
			tx.Delete(Tables.Chan, oldKey);
			tx.Put(Tables.Chan, newKey, Codec.Serialize(updated));

			var newUpperName = newName.ToUpperInvariant();
			foreach (var (memberKey, memberValue) in tx.Range(Tables.ChanMember, ChanMemberPrefix(oldKey)).ToList())
			{
				var memberDbref = Keys.ReadDbref(memberKey.AsSpan(memberKey.Length - 8, 8));
				tx.Delete(Tables.ChanMember, memberKey);
				tx.Put(Tables.ChanMember, ChanMemberKey(newUpperName, memberDbref), memberValue);
				tx.Delete(Tables.RevChanMember, Keys.Dbref(memberDbref), oldKey);
				tx.Put(Tables.RevChanMember, Keys.Dbref(memberDbref), newKey);
			}
		}, cancellationToken);
	}

	public async ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var key = ChanKey(channel.Name.ToPlainText());
		var ownerKey = (long)newOwner.Object.Key;

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Chan, key, out var bytes))
			{
				return;
			}

			var record = Codec.Deserialize<ChannelRecord>(bytes);
			tx.Put(Tables.Chan, key, Codec.Serialize(record with { Owner = ownerKey }));
		}, cancellationToken);
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default)
	{
		var key = ChanKey(channel.Name.ToPlainText());

		await Store.WriteAsync(tx =>
		{
			foreach (var (memberKey, _) in tx.Range(Tables.ChanMember, ChanMemberPrefix(key)).ToList())
			{
				var memberDbref = Keys.ReadDbref(memberKey.AsSpan(memberKey.Length - 8, 8));
				tx.Delete(Tables.RevChanMember, Keys.Dbref(memberDbref), key);
			}

			tx.DeletePrefix(Tables.ChanMember, ChanMemberPrefix(key));
			tx.Delete(Tables.Chan, key);
		}, cancellationToken);
	}

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var key = ChanKey(channelName);
		var memberDbref = (long)obj.Object().Key;
		var memberRecord = new ChannelMemberRecord { Gagged = false, Mute = false, Hide = false, Combine = false, Title = "" };

		await Store.WriteAsync(tx =>
		{
			tx.Put(Tables.ChanMember, ChanMemberKey(channelName.ToUpperInvariant(), memberDbref), Codec.Serialize(memberRecord));
			tx.Put(Tables.RevChanMember, Keys.Dbref(memberDbref), key);
		}, cancellationToken);
	}

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var key = ChanKey(channelName);
		var memberDbref = (long)obj.Object().Key;

		await Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.ChanMember, ChanMemberKey(channelName.ToUpperInvariant(), memberDbref));
			tx.Delete(Tables.RevChanMember, Keys.Dbref(memberDbref), key);
		}, cancellationToken);
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status,
		CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var memberKey = ChanMemberKey(channelName.ToUpperInvariant(), (long)obj.Object().Key);

		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.ChanMember, memberKey, out var bytes))
			{
				return;
			}

			var record = Codec.Deserialize<ChannelMemberRecord>(bytes);
			var updated = record with
			{
				Combine = status.Combine ?? record.Combine,
				Gagged = status.Gagged ?? record.Gagged,
				Hide = status.Hide ?? record.Hide,
				Mute = status.Mute ?? record.Mute,
				Title = status.Title is not null ? MarkupTextSerializer.Serialize(status.Title) : record.Title
			};
			tx.Put(Tables.ChanMember, memberKey, Codec.Serialize(updated));
		}, cancellationToken);
	}
}
