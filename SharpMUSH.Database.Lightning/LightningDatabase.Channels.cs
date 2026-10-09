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
/// <see cref="IChannelStore"/>: chat channels and memberships. There is no ownership or membership
/// graph — <see cref="Tables.Chan"/> is keyed <c>Keys.Upper(name)</c> with the owner as a plain dbref field
/// on <see cref="ChannelRecord"/>; <see cref="Tables.ChanMember"/> is keyed
/// <c>Keys.Composite(NAME, dbref)</c> so every member of one channel is a single prefix range; and
/// <see cref="Tables.RevChanMember"/> is keyed by the member's dbref (duplicates) so
/// <c>LightningDatabase.Objects.cs</c>'s object-delete cascade can find and drop every channel a deleted
/// object belonged to without scanning every channel.
///
/// <para><c>CreateChannelAsync</c> needs no lock beyond the store's own single-writer serialization:
/// only one job runs on the LMDB writer thread at a time,
/// so a <c>TryGet</c> immediately followed by a <c>Put</c> inside one job is already atomic with respect
/// to every other create.</para>
/// </summary>
public partial class LightningDatabase
{
	private static byte[] ChanKey(string name) => Keys.Upper(name);

	/// <summary>
	/// A channel's <see cref="SharpChannel.Id"/>. It is the name as stored, so it changes with a rename,
	/// case included, and whatever is keyed by it (the recall buffer, read markers) is moved then.
	/// </summary>
	private static string ChannelId(string name) => $"Channel/{name}";

	private static byte[] ChanMemberKey(string upperName, long memberDbref) => Keys.Composite(upperName, memberDbref);

	private static byte[] ChanMemberPrefix(byte[] upperNameKey) => Keys.Concat(upperNameKey, Keys.Sep);

	private SharpChannel MapRecordToChannel(ChannelRecord record)
	{
		var channelName = record.Name;
		var key = ChanKey(channelName);

		return new SharpChannel
		{
			Id = ChannelId(channelName),
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
			OwnerDBRef = new DBRef((int)record.Owner),
			Owner = new AsyncLazy<SharpPlayer>(_ => Task.FromResult(LoadChannelOwner(record.Owner, channelName))),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
				new FreshAsyncEnumerable<SharpChannel.MemberAndStatus>(ct => GetChannelMembersCoreAsync(key, ct)))
		};
	}

	private SharpPlayer LoadChannelOwner(long ownerDbref, string channelName) => Store.Read(tx =>
	{
		var found = ReadObject(tx, ownerDbref)
			?? throw new InvalidOperationException($"No owner found for channel '{channelName}'");
		return Hydrate(found.Dbref, found.Record) is SharpPlayer owner
			? owner
			: throw new InvalidOperationException($"The owner of channel '{channelName}' is not a player");
	});

	/// <summary>Every member of the channel keyed <paramref name="key"/>, in dbref order, paged. A membership row
	/// whose object is gone is not a member.</summary>
	private IAsyncEnumerable<SharpChannel.MemberAndStatus> GetChannelMembersCoreAsync(byte[] key, CancellationToken ct)
		=> Store.RangeMapAsync(Tables.ChanMember, ChanMemberPrefix(key), (tx, memberKey, value) =>
		{
			var found = ReadObject(tx, Keys.ReadDbref(memberKey.AsSpan(memberKey.Length - 8, 8)));
			return found is null
				? null
				: new SharpChannel.MemberAndStatus(Hydrate(found.Value.Dbref, found.Value.Record),
					MapChannelStatus(Codec.Deserialize<ChannelMemberRecord>(value)));
		}, ct: ct);

	private static SharpChannelStatus MapChannelStatus(ChannelMemberRecord memberRecord) => new(
		Combine: memberRecord.Combine,
		Gagged: memberRecord.Gagged,
		Hide: memberRecord.Hide,
		Mute: memberRecord.Mute,
		Title: MarkupTextSerializer.Deserialize(memberRecord.Title));

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

	public ValueTask<Found<SharpChannelStatus>> GetChannelMemberStatusAsync(SharpChannel channel, DBRef member,
		CancellationToken cancellationToken = default)
	{
		var memberKey = ChanMemberKey(channel.Name.ToPlainText().ToUpperInvariant(), member.Number);
		return ValueTask.FromResult(Store.Read<Found<SharpChannelStatus>>(tx =>
		{
			if (!tx.TryGet(Tables.ChanMember, memberKey, out var bytes)
				|| ReadObject(tx, member.Number) is not { } found
				|| (member.CreationMilliseconds is { } created && found.Record.CreationTime != created))
			{
				return new NotFound();
			}

			return MapChannelStatus(Codec.Deserialize<ChannelMemberRecord>(bytes));
		}));
	}

	public ValueTask<int> GetChannelMemberCountAsync(SharpChannel channel, CancellationToken cancellationToken = default)
	{
		var prefix = ChanMemberPrefix(ChanKey(channel.Name.ToPlainText()));
		// No member is decoded or hydrated, but this is still a cursor walk over the channel's membership rows
		// (ChanMember is not a duplicate table, so there is no O(1) count), plus a point read of each member's
		// object row: a membership is counted only when its object is there, as the member listing requires.
		return ValueTask.FromResult(Store.Read(tx => tx.Range(Tables.ChanMember, prefix)
			.Count(entry => tx.TryGet(Tables.Obj, entry.Key.AsSpan(entry.Key.Length - 8, 8), out _))));
	}

	private IAsyncEnumerable<SharpChannel> GetMemberChannelsCoreAsync(long dbref, CancellationToken ct)
		=> Store.DupsMapAsync(Tables.RevChanMember, Keys.Dbref(dbref),
			(tx, upperNameBytes) => tx.TryGet(Tables.Chan, upperNameBytes, out var chanBytes)
				? MapRecordToChannel(Codec.Deserialize<ChannelRecord>(chanBytes))
				: null,
			ct: ct);

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

			if (!string.Equals(oldName, newName, StringComparison.Ordinal))
			{
				MoveChannelReadMarkers(tx, ChannelId(oldName), ChannelId(newName));
			}

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

	/// <summary>
	/// Re-files every character's read marker for a renamed channel under its new id. A rename is rare
	/// and markers are keyed by character, so this walks the table rather than keeping an index by scope.
	/// </summary>
	private static void MoveChannelReadMarkers(ITx tx, string oldId, string newId)
	{
		var oldScope = ReadMarkerScope.Channel(oldId);
		var newScope = ReadMarkerScope.Channel(newId);
		foreach (var (key, value) in tx.Range(Tables.ReadMarker, []).ToList())
		{
			var record = Codec.Deserialize<ReadMarkerRecord>(value);
			if (!string.Equals(record.Scope, oldScope, StringComparison.Ordinal)) continue;

			tx.Delete(Tables.ReadMarker, key);
			tx.Put(Tables.ReadMarker, Keys.Composite(Keys.ReadDbref(key), newScope),
				Codec.Serialize(record with { Scope = newScope }));
		}
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
