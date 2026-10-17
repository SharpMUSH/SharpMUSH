using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private sealed record CredentialState(long? AccountEpoch, long? CharacterEpoch, long? CharacterCreation);

	private CredentialState ReadCredentialState(string? accountId, DBRef? character) => Store.Read(tx =>
	{
		long? accountEpoch = accountId is not null && tx.TryGet(Tables.Account, Keys.Str(ParseAccountId(accountId)), out var account)
			? Codec.Deserialize<AccountRecord>(account).CredentialEpoch : null;
		if (character is not { } player || !tx.TryGet(Tables.Obj, Keys.Dbref(player.Number), out var bytes))
			return new CredentialState(accountEpoch, null, null);
		var record = Codec.Deserialize<ObjectRecord>(bytes);
		return new CredentialState(accountEpoch, record.PasswordCredentialEpoch, record.CreationTime);
	});

	private void RevokeCredentials(Action<SharpMUSH.Library.Services.Interfaces.IAccountCredentialRevocation> revoke)
	{
		foreach (var revoker in _credentialRevokers.Value)
		{
			try { revoke(revoker); }
			catch (Exception error) { _logger.LogWarning("Credential revocation notification failed: {ErrorType}", error.GetType().Name); }
		}
	}

	private async ValueTask<T> WriteCredentialsAsync<T>(Func<ITx, T> write, CancellationToken cancellationToken = default,
		string? accountId = null, DBRef? character = null, Action? committed = null)
		=> await _credentialGate.RunAsync(async () =>
		{
			var before = accountId is not null || character is not null ? ReadCredentialState(accountId, character) : null;
			var result = await Store.WriteAsync(write, cancellationToken);
			if (before is not null)
			{
				var after = ReadCredentialState(accountId, character);
				if (accountId is not null && before.AccountEpoch != after.AccountEpoch)
					RevokeCredentials(revoker => revoker.RevokeAccount(accountId));
				if (character is { } player && (before.CharacterEpoch != after.CharacterEpoch || before.CharacterCreation != after.CharacterCreation))
					RevokeCredentials(revoker => revoker.RevokeCharacter(new DBRef(player.Number, before.CharacterCreation ?? player.CreationMilliseconds)));
			}
			committed?.Invoke();
			return result;
		}, cancellationToken);

	private ValueTask WriteCredentialsAsync(Action<ITx> write, CancellationToken cancellationToken = default,
		string? accountId = null, DBRef? character = null, Action? committed = null)
		=> new(WriteCredentialsAsync(tx => { write(tx); return true; }, cancellationToken, accountId, character, committed).AsTask());
}
