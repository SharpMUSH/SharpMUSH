using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>The sole cross-world character mapping. Creation times prevent recycled dbrefs gaining authority.</summary>
public sealed record ImportIdentityMapping(string AccountKey, long OriginalCreationTime, long ImportedCreationTime);

public partial class LightningDatabase
{
	/// <summary>
	/// Reads the explicit #1 mapping while both worlds are closed. The restart journal persists this
	/// before transferring identity data; no credentials enter the journal or preview response.
	/// </summary>
	public static ImportIdentityMapping? ReadImportIdentityMapping(string sourcePath, string targetPath)
	{
		if (!Directory.Exists(sourcePath)) return null;
		using var source = OpenIdentityWorld(sourcePath);
		using var target = OpenIdentityWorld(targetPath);
		return source.Read(tx =>
		{
			if (!tx.TryGet(Tables.AccountChar.Reverse, Keys.Dbref(1), out var account)) return null;
			var accountKey = Keys.ReadStr(account);
			RequireAccount(tx, accountKey);
			if (!tx.Dups(Tables.AccountChar.Forward, account).Any(value => value.SequenceEqual(Keys.Dbref(1)))
				|| tx.Dups(Tables.AccountChar.Reverse, Keys.Dbref(1)).Count() != 1)
				throw new InvalidDataException("The original player ownership indices disagree.");
			var original = IdentityPlayer(tx);
			var imported = target.Read(IdentityPlayer);
			return new ImportIdentityMapping(accountKey, original.CreationTime, imported.CreationTime);
		});
	}

	/// <summary>
	/// Offline provider migration, called with the engine lease held and both providers disposed.
	/// Replaces identity and its indices in one durable destination transaction. Repeating a transfer
	/// before either world is moved is safe; the outgoing world is the authoritative current snapshot.
	/// Session authority is deliberately never carried across a world boundary. OTTs and proof grants
	/// live only in the stopped host's memory and therefore need no provider records removed here.
	/// </summary>
	public static void TransferImportIdentity(string sourcePath, string targetPath, ImportIdentityMapping? mapping, bool rollback)
	{
		if (!rollback && !Directory.Exists(sourcePath)) return;
		if (Path.GetFullPath(sourcePath) == Path.GetFullPath(targetPath))
			throw new InvalidOperationException("Identity transfer requires two different closed worlds.");
		using var source = OpenIdentityWorld(sourcePath);
		using var target = OpenIdentityWorld(targetPath);
		source.Read(sourceTx =>
		{
			target.WriteAsync(targetTx => TransferIdentity(sourceTx, targetTx, mapping, rollback)).AsTask().GetAwaiter().GetResult();
			return true;
		});
	}

	private static LightningStore OpenIdentityWorld(string path)
	{
		if (!File.Exists(Path.Combine(path, "data.mdb"))) throw new IOException("An identity transfer world is missing.");
		return new LightningStore(new LightningStoreOptions { Path = path });
	}

	private static ObjectHeaderRecord IdentityPlayer(ITx tx)
	{
		if (!tx.TryGet(Tables.Obj, Keys.Dbref(1), out var bytes)) throw new InvalidDataException("Identity mapping requires player #1.");
		var player = Codec.Deserialize<ObjectHeaderRecord>(bytes);
		if (player.Type != DatabaseConstants.TypePlayer) throw new InvalidDataException("Identity mapping requires player #1.");
		return player;
	}

	private static void RequireAccount(ITx tx, string key)
	{
		if (!tx.TryGet(Tables.Account, Keys.Str(key), out _)) throw new InvalidDataException("Portal identity references an unknown account.");
	}

	private static void PutUniqueIdentityIndex(ITx tx, TableDef table, byte[] key, byte[] account)
	{
		if (tx.TryGet(table, key, out _)) throw new InvalidDataException("Portal identity has a duplicate unique index.");
		tx.Put(table, key, account);
	}

	private static void TransferIdentity(ITx source, ITx target, ImportIdentityMapping? mapping, bool rollback)
	{
		if (mapping is not null
			&& (IdentityPlayer(source).CreationTime != (rollback ? mapping.ImportedCreationTime : mapping.OriginalCreationTime)
				|| IdentityPlayer(target).CreationTime != (rollback ? mapping.OriginalCreationTime : mapping.ImportedCreationTime)))
			throw new InvalidDataException("The explicit import character mapping no longer identifies the same players.");

		TableDef[] retained = [Tables.Account, Tables.AccountBan, Tables.AccountPermission, Tables.AccountRole,
			Tables.AccountPasskey, Tables.ExternalIdentity, Tables.OnboardingProgress];
		TableDef[] cleared = [Tables.AccountEmail, Tables.AccountUser, Tables.AccountPasskeyByAccount,
			Tables.ExternalIdentityByAccount, Tables.AccountChar.Forward, Tables.AccountChar.Reverse,
			Tables.Session, Tables.SessionDigest, Tables.SessionAccount, Tables.SessionIp, Tables.SessionExpiry,
			Tables.OAuthGrant, Tables.OAuthGrantByAccount, Tables.OAuthGrantByCharacter, Tables.OAuthGrantByClient,
			Tables.OAuthToken, Tables.OAuthTokenByGrant, Tables.OAuthTokenExpiry,
			Tables.PushSubscription, Tables.PushSubscriptionByAccount];
		foreach (var table in retained.Concat(cleared)) target.DeletePrefix(table, []);
		foreach (var table in retained)
			foreach (var row in source.Range(table, [])) target.Put(table, row.Key, row.Value);

		var nextId = source.TryGet(Tables.Meta, Keys.Str("next_account_id"), out var next) ? Keys.ReadDbref(next) : 0;
		foreach (var row in target.Range(Tables.Account, []))
		{
			if (!long.TryParse(Keys.ReadStr(row.Key), out var id) || id < 0 || id == long.MaxValue)
				throw new InvalidDataException("Portal account id is invalid.");
			nextId = Math.Max(nextId, checked(id + 1));
			var account = Codec.Deserialize<AccountRecord>(row.Value);
			if (string.IsNullOrWhiteSpace(account.Username)) throw new InvalidDataException("Portal account username is missing.");
			PutUniqueIdentityIndex(target, Tables.AccountUser, Keys.Lower(account.Username), row.Key);
			if (account.Email is not null) PutUniqueIdentityIndex(target, Tables.AccountEmail, Keys.Lower(account.Email), row.Key);
		}
		target.Put(Tables.Meta, Keys.Str("next_account_id"), Keys.Dbref(nextId));

		foreach (var table in new[] { Tables.AccountBan, Tables.AccountPermission, Tables.AccountRole })
			foreach (var row in target.Range(table, [])) RequireAccount(target, Keys.ReadStr(row.Key));
		foreach (var row in target.Range(Tables.AccountRole, []))
		{
			if (!source.TryGet(Tables.Role, row.Value, out var bytes)) throw new InvalidDataException("An account role definition is missing.");
			var role = Codec.Deserialize<RoleRecord>(bytes);
			if (role.Slug != Keys.ReadStr(row.Value)) throw new InvalidDataException("An account role definition does not match its key.");
			if (target.TryGet(Tables.Role, row.Value, out var existingBytes)
				&& target.Dups(Tables.ObjRole.Reverse, row.Value).Any())
			{
				var existing = Codec.Deserialize<RoleRecord>(existingBytes);
				if (existing.Priority != role.Priority || existing.IsSystem != role.IsSystem
					|| existing.Permissions.Count != role.Permissions.Count
					|| role.Permissions.Any(permission => !existing.Permissions.TryGetValue(permission.Key, out var state) || state != permission.Value))
					throw new InvalidDataException("An account role conflicts with authority assigned to imported objects.");
			}
			foreach (var scope in role.Permissions.Keys) CopyIdentityPermission(source, target, scope);
			target.Put(Tables.Role, row.Value, bytes);
			if (!string.IsNullOrEmpty(role.Category))
			{
				var categoryKey = Keys.Lower(role.Category);
				if (!source.TryGet(Tables.RoleCategory, categoryKey, out var category)) throw new InvalidDataException("An account role category is missing.");
				target.Put(Tables.RoleCategory, categoryKey, category);
			}
		}

		foreach (var row in target.Range(Tables.AccountPermission, []))
			foreach (var scope in Codec.Deserialize<Dictionary<string, int>>(row.Value).Keys)
				CopyIdentityPermission(source, target, scope);

		foreach (var row in target.Range(Tables.AccountPasskey, []))
		{
			var passkey = Codec.Deserialize<AccountPasskeyRecord>(row.Value);
			RequireAccount(target, passkey.AccountKey);
			if (!row.Key.SequenceEqual(PasskeyKey(passkey.CredentialId))) throw new InvalidDataException("A passkey index does not match its credential.");
			target.Put(Tables.AccountPasskeyByAccount, Keys.Str(passkey.AccountKey), row.Key);
		}
		foreach (var row in target.Range(Tables.ExternalIdentity, []))
		{
			var identity = Codec.Deserialize<ExternalIdentityRecord>(row.Value);
			RequireAccount(target, identity.AccountKey);
			var key = new ExternalIdentityKey(identity.Registration, identity.Issuer, identity.Subject);
			if (!ValidExternalIdentity(key, identity.Label) || !row.Key.SequenceEqual(ExternalIdentityDigest(key)))
				throw new InvalidDataException("An external identity index does not match its issuer and subject.");
			target.Put(Tables.ExternalIdentityByAccount, Keys.Str(identity.AccountKey), row.Key);
		}

		// A removed/reassigned link is not resurrected on rollback. Only this exact account's current
		// ownership of the mapped source player can authorize the corresponding target player.
		if (mapping is not null && source.TryGet(Tables.AccountChar.Reverse, Keys.Dbref(1), out var owner)
			&& Keys.ReadStr(owner) == mapping.AccountKey)
		{
			RequireAccount(target, mapping.AccountKey);
			target.Put(Tables.AccountChar.Forward, owner, Keys.Dbref(1));
			target.Put(Tables.AccountChar.Reverse, Keys.Dbref(1), owner);
		}
		CopyIdentityDocument(source, target, Tables.State, Keys.Str("state"));
		CopyIdentityDocument(source, target, Tables.ExpandedSrv, Keys.Str("SetupWizardState"));
	}

	private static void CopyIdentityPermission(ITx source, ITx target, string scope)
	{
		if (PortalPermission.IsKnown(scope)) return;
		var key = Keys.Str(scope);
		if (!source.TryGet(Tables.CustomPermission, key, out var bytes))
			throw new InvalidDataException("An account permission definition is missing.");
		var permission = Codec.Deserialize<CustomPermissionRecord>(bytes);
		if (permission.Scope != scope) throw new InvalidDataException("An account permission definition does not match its key.");
		target.Put(Tables.CustomPermission, key, bytes);
		if (string.IsNullOrEmpty(permission.Category)) return;
		var categoryKey = Keys.Lower(permission.Category);
		if (!source.TryGet(Tables.PermissionCategory, categoryKey, out var category))
			throw new InvalidDataException("An account permission category is missing.");
		target.Put(Tables.PermissionCategory, categoryKey, category);
	}

	private static void CopyIdentityDocument(ITx source, ITx target, TableDef table, byte[] key)
	{
		if (source.TryGet(table, key, out var value)) target.Put(table, key, value);
		else target.Delete(table, key);
	}
}
