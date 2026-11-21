using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountHolderStore"/>: an account's holder is built from the account, never read from
/// <see cref="Tables.Obj"/>. Its attributes are ordinary attribute rows under the holder's reserved number
/// (<see cref="AccountHolders"/>), which sort after every object's and which no object can take.
/// </summary>
public partial class LightningDatabase : IAccountHolderStore
{
	public ValueTask<Found<SharpAccountHolder>> GetAccountHolderAsync(DBRef holder, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<SharpAccountHolder>>(tx =>
			holder.IsAccountHolder() && ReadAccountRecord(tx, holder.AccountKey()) is { } account
				? BuildAccountHolder(holder, account)
				: new NotFound()));

	private static AccountRecord? ReadAccountRecord(ITx tx, int accountKey)
		=> tx.TryGet(Tables.Account, Keys.Str(accountKey.ToString(System.Globalization.CultureInfo.InvariantCulture)), out var bytes)
			? Codec.Deserialize<AccountRecord>(bytes)
			: null;

	/// <summary>The type an attribute write on <paramref name="n"/> files its index entries under: the object's, or a thing's for a holder whose account exists.</summary>
	private static string? AttributeHolderType(ITx tx, long n, (long Dbref, ObjectRecord Record)? found)
		=> found is { } stored
			? stored.Record.Type
			: n is >= int.MinValue and < AccountHolders.Ceiling && ReadAccountRecord(tx, new DBRef((int)n).AccountKey()) is not null
				? DatabaseConstants.TypeThing
				: null;

	private SharpAccountHolder BuildAccountHolder(DBRef holder, AccountRecord account)
	{
		var n = (long)holder.Number;
		var record = new ObjectRecord
		{
			Name = $"Account {holder.AccountKey()}",
			Type = DatabaseConstants.TypeThing,
			CreationTime = account.CreatedAt,
			ModifiedTime = account.UpdatedAt
		};

		var sharpObj = MapToSharpObject(n, record);
		sharpObj.Owner = new(_ => Task.FromResult(God()));
		sharpObj.Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None()));
		sharpObj.Zone = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None()));
		var accountId = holder.AccountId();
		var key = holder.AccountKey().ToString(System.Globalization.CultureInfo.InvariantCulture);

		return new SharpAccountHolder
		{
			Id = holder.ToString(),
			Object = sharpObj,
			AccountId = accountId,
			Location = new(_ => Task.FromResult(RoomZero())),
			Home = new(_ => Task.FromResult(RoomZero())),
			Characters = new(_ => Task.FromResult<IReadOnlySet<int>>(Store.Read(tx => tx.Dups(Tables.AccountChar.Forward, Keys.Str(key))
				.Select(value => (int)Keys.ReadDbref(value))
				.ToHashSet())))
		};
	}

	private SharpPlayer God() => Store.Read(tx => ReadObject(tx, 1) is { Record.Type: DatabaseConstants.TypePlayer } found
		? HydratePlayer(found.Dbref, found.Record)
		: throw new InvalidOperationException("#1 is not a player."));

	private AnySharpContainer RoomZero() => Store.Read(tx => ReadObject(tx, 0) is { } found
		? RelatedContainer(Hydrate(found.Dbref, found.Record))
		: throw new InvalidOperationException("#0 does not exist."));
}
