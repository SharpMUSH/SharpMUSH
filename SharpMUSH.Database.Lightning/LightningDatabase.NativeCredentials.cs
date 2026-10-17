using SharpMUSH.Database;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private static NativeCharacterAuthority? ReadNativeCharacterAuthority(ITx tx, string accountId, DBRef character)
	{
		if (character.CreationMilliseconds is not { } creation) return null;
		var accountKey = ParseAccountId(accountId);
		if (ReadAccountByKey(tx, accountKey) is not { IsActive: true, MustChangePassword: false } account
			|| !tx.TryGet(Tables.AccountChar.Reverse, Keys.Dbref(character.Number), out var heldBy)
			|| Keys.ReadStr(heldBy) != accountKey
			|| !tx.TryGet(Tables.Obj, Keys.Dbref(character.Number), out var bytes)) return null;
		var player = Codec.Deserialize<ObjectRecord>(bytes);
		return player.Type == DatabaseConstants.TypePlayer && player.CreationTime == creation
			? new(account.Id!, character, account.CredentialEpoch, player.PasswordCredentialEpoch) : null;
	}

	public ValueTask<Found<NativeCharacterAuthority>> GetNativeCharacterAuthorityAsync(string accountId, DBRef character,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<NativeCharacterAuthority>>(tx =>
			ReadNativeCharacterAuthority(tx, accountId, character) is { } authority ? authority : new NotFound()));

	public async ValueTask<Result<Success>> SetOwnedPlayerPasswordAsync(string accountId, DBRef character, string passwordHash,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(passwordHash)) return new Error<string>("A nonempty character password hash is required.");
		return await WriteCredentialsAsync<Result<Success>>(tx =>
		{
			if (ReadNativeCharacterAuthority(tx, accountId, character) is null)
				return new Error<string>("The active account no longer owns this exact character.");
			tx.TryGet(Tables.Obj, Keys.Dbref(character.Number), out var bytes);
			var record = Codec.Deserialize<ObjectRecord>(bytes);
			tx.Put(Tables.Obj, Keys.Dbref(character.Number), Codec.Serialize(record with
			{
				PasswordHash = passwordHash,
				PasswordSalt = "",
				PasswordCredentialEpoch = checked(record.PasswordCredentialEpoch + 1)
			}));
			return new Success();
		}, cancellationToken, accountId: accountId, character: character);
	}
}
