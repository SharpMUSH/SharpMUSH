using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The recover list (<see cref="Tables.Recover"/>). Retiring an object runs the same teardown a delete does, except
/// that its attributes and expanded data stay where they are: the bulk of an object is never copied, and the number
/// is held by the entry so nothing else can write under it. Only the row moves, beside the relations it had,
/// recorded as objids.
/// <para>
/// Restoring writes the row back under the same number and creation time, so everything that stored the objid
/// (locks, mail, page logs, package records) names it again, and rebuilds what the teardown dropped: the name and
/// type indexes, the attribute indexes, flags, powers, roles, permission overrides, the account link, and whichever
/// relations the engine found still live.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	private const int RecoverListPage = 256;

	public async ValueTask<bool> RetireObjectAsync(DBRef dbref, DBRef? destroyedBy, CancellationToken cancellationToken = default)
		=> await WriteCredentialsAsync(tx =>
		{
			var n = (long)dbref.Number;
			if (ReadObject(tx, n) is not { } found
				|| (dbref.CreationMilliseconds is { } created && found.Record.CreationTime != created))
			{
				return false;
			}

			var key = Keys.Dbref(n);
			var entry = new RecoverRecord
			{
				Object = found.Record,
				Owner = EdgeObjid(tx, Tables.Owner.Forward, n),
				Location = EdgeObjid(tx, Tables.Location.Forward, n),
				Home = EdgeObjid(tx, Tables.Home.Forward, n),
				Parent = EdgeObjid(tx, Tables.Parent.Forward, n),
				Zone = EdgeObjid(tx, Tables.Zone.Forward, n),
				Flags = tx.Dups(Tables.ObjFlag.Forward, key).Select(value => Keys.ReadStr(value))
					.Where(flag => flag is not ("GOING" or "GOING_TWICE")).ToArray(),
				Powers = tx.Dups(Tables.ObjPower.Forward, key).Select(value => Keys.ReadStr(value)).ToArray(),
				Roles = tx.Dups(Tables.ObjRole.Forward, key).Select(value => Keys.ReadStr(value)).ToArray(),
				Permissions = tx.TryGet(Tables.ObjPermission, key, out var permissions) ? permissions : null,
				Account = tx.TryGet(Tables.AccountChar.Reverse, key, out var account) ? Keys.ReadStr(account) : null,
				DestroyedBy = destroyedBy?.ToString(),
				DestroyedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				Bytes = ContentBytes(tx, n)
			};

			TearDownObject(tx, n, found.Record, keepContent: true);
			tx.Put(Tables.Recover, key, Codec.Serialize(entry));

			_logger.LogInformation("Moved object #{DbRef} ({Name}) to the recover list", n, found.Record.Name);
			return true;
		}, cancellationToken, character: dbref);

	public ValueTask<Found<RecoverEntry>> GetRecoverEntryAsync(int number, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<RecoverEntry>>(tx => ReadRecover(tx, number) is { } record
			? MapRecoverEntry(number, record)
			: new NotFound()));

	public async IAsyncEnumerable<RecoverEntry> GetRecoverEntriesAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// A page per read: a long-lived reader keeps LMDB from reusing the pages a purge frees.
		var after = Array.Empty<byte>();
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var resume = after;
			var page = Store.Read(tx => tx.Range(Tables.Recover, [])
				.SkipWhile(row => resume.Length > 0 && row.Key.AsSpan().SequenceCompareTo(resume) <= 0)
				.Take(RecoverListPage)
				.Select(row => (row.Key, Record: Codec.Deserialize<RecoverRecord>(row.Value)))
				.ToList());

			foreach (var (key, record) in page)
			{
				yield return MapRecoverEntry(Keys.ReadDbref(key), record);
			}

			if (page.Count < RecoverListPage) yield break;
			after = page[^1].Key;
			await Task.Yield();
		}
	}

	public async ValueTask<Result<DBRef>> RestoreRecoverEntryAsync(RecoverRestore restore,
		CancellationToken cancellationToken = default)
		=> await WriteCredentialsAsync<Result<DBRef>>(tx =>
		{
			var n = (long)restore.Objid.Number;
			var key = Keys.Dbref(n);
			if (ReadRecover(tx, n) is not { } entry || entry.Object.CreationTime != restore.Objid.CreationMilliseconds)
			{
				return new Error<string>($"#{n} is not on the recover list.");
			}

			if (tx.TryGet(Tables.Obj, key, out _))
			{
				return new Error<string>($"#{n} already holds an object.");
			}

			var record = entry.Object;
			if (record.Type == DatabaseConstants.TypePlayer
				&& NamedPlayers(tx, [record.Name, .. record.Aliases]).Any(other => other != n))
			{
				return new Error<string>($"A player named {record.Name} exists.");
			}

			tx.Put(Tables.Obj, key, Codec.Serialize(record));
			tx.Put(Tables.ObjName, Keys.Lower(record.Name), key);
			foreach (var alias in record.Aliases)
			{
				tx.Put(Tables.ObjName, Keys.Lower(alias), key);
			}

			IndexObjectType(tx, record.Type, n);
			ReindexObjectAttributes(tx, n, record.Type);

			foreach (var flag in entry.Flags) PutMembershipEdge(Tables.ObjFlag, tx, n, flag);
			foreach (var power in entry.Powers) PutMembershipEdge(Tables.ObjPower, tx, n, power);
			foreach (var role in entry.Roles) AddObjectRole(tx, n, role);
			if (entry.Permissions is { } permissions) tx.Put(Tables.ObjPermission, key, permissions);
			if (restore.RelinkAccount && entry.Account is { } account && tx.TryGet(Tables.Account, Keys.Str(account), out _))
			{
				tx.Put(Tables.AccountChar.Forward, Keys.Str(account), key);
				tx.Put(Tables.AccountChar.Reverse, key, Keys.Str(account));
			}

			SetSingleEdge(tx, Tables.Owner, n, restore.Owner.Number);
			SetSingleEdge(tx, Tables.Home, n, restore.Home?.Number);
			SetSingleEdge(tx, Tables.Parent, n, restore.Parent?.Number);
			SetSingleEdge(tx, Tables.Zone, n, restore.Zone?.Number);
			if (restore.Location is { } location)
			{
				SetLocation(tx, n, location.Number);
				if (record.Type == DatabaseConstants.TypeExit) PutEdge(tx, Tables.Exit, location.Number, n);
			}

			tx.Delete(Tables.Recover, key);
			_logger.LogInformation("Recovered object #{DbRef} ({Name}) from the recover list", n, record.Name);
			return new DBRef((int)n, record.CreationTime);
		}, cancellationToken, character: restore.Objid);

	public async ValueTask<bool> PurgeRecoverEntryAsync(DBRef objid, bool evenIfKept, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var n = (long)objid.Number;
			if (ReadRecover(tx, n) is not { } entry
				|| (objid.CreationMilliseconds is { } created && entry.Object.CreationTime != created)
				|| (!evenIfKept && entry.KeptBy is not null)
				|| tx.TryGet(Tables.Obj, Keys.Dbref(n), out _))
			{
				return false;
			}

			DeleteObjectContent(tx, n);
			tx.Delete(Tables.Recover, Keys.Dbref(n));
			FreeNumberAt(tx, n, Never);
			return true;
		}, cancellationToken);

	public async ValueTask<bool> SetRecoverKeepAsync(DBRef objid, RecoverKeep? keep, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var n = (long)objid.Number;
			if (ReadRecover(tx, n) is not { } entry
				|| (objid.CreationMilliseconds is { } created && entry.Object.CreationTime != created))
			{
				return false;
			}

			tx.Put(Tables.Recover, Keys.Dbref(n), Codec.Serialize(entry with
			{
				KeptBy = keep?.By.ToString(),
				KeepReason = keep?.Reason,
				KeptAt = keep?.At.ToUnixTimeMilliseconds()
			}));
			return true;
		}, cancellationToken);

	public ValueTask<RecoverListUsage> MeasureRecoverListAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx =>
		{
			long entries = 0, bytes = 0, kept = 0, keptBytes = 0;
			foreach (var (_, value) in tx.Range(Tables.Recover, []))
			{
				var record = Codec.Deserialize<RecoverRecord>(value);
				entries++;
				bytes += record.Bytes;
				if (record.KeptBy is null) continue;
				kept++;
				keptBytes += record.Bytes;
			}

			return new RecoverListUsage(entries, bytes, kept, keptBytes);
		}));

	/// <summary>Whether <paramref name="number"/> is held by a recover entry, which the allocator never hands out.</summary>
	internal static bool IsHeldByRecoverEntry(ITx tx, long number) => tx.TryGet(Tables.Recover, Keys.Dbref(number), out _);

	private static RecoverRecord? ReadRecover(ITx tx, long number)
		=> tx.TryGet(Tables.Recover, Keys.Dbref(number), out var bytes) ? Codec.Deserialize<RecoverRecord>(bytes) : null;

	/// <summary>The objid an object's single-valued relation names, or null when it has none or the target is gone.</summary>
	private static string? EdgeObjid(ITx tx, TableDef forward, long from)
		=> GetSingleEdge(tx, forward, from) is { } to && HeaderRef(tx, to) is { } objid ? objid.ToString() : null;

	/// <summary>What an object's attribute and expanded-data rows occupy, keys and values.</summary>
	private static long ContentBytes(ITx tx, long n)
		=> new[] { (Tables.AttrMeta, Keys.AttrPrefix(n)), (Tables.AttrVal, Keys.AttrPrefix(n)), (Tables.ExpandedObj, Keys.Composite(n, "")) }
			.Sum(part => tx.Range(part.Item1, part.Item2).Sum(row => (long)row.Key.Length + row.Value.Length));

	/// <summary>The players holding any of <paramref name="names"/>, by name or alias.</summary>
	private static IEnumerable<long> NamedPlayers(ITx tx, IEnumerable<string> names)
		=> names.SelectMany(name => tx.Dups(Tables.ObjName, Keys.Lower(name)).Select(value => Keys.ReadDbref(value)))
			.Where(number => ReadObjectHeader(tx, number)?.Type == DatabaseConstants.TypePlayer);

	/// <summary>Lists a restored object's copies of every indexed attribute, as building the index would have.</summary>
	private static void ReindexObjectAttributes(ITx tx, long n, string type)
	{
		foreach (var (_, value) in tx.Range(Tables.AttrEntry, []).ToList())
		{
			var entry = Codec.Deserialize<AttributeEntryRecord>(value);
			if (entry.Index is not { } index) continue;

			var longName = entry.Name.ToUpperInvariant();
			var attrKey = Keys.Attr(n, longName);
			if (!tx.TryGet(Tables.AttrMeta, attrKey, out var meta)) continue;

			var stored = tx.TryGet(Tables.AttrVal, attrKey, out var bytes) ? bytes : null;
			ReplaceParts(tx, n, longName,
				InScope(index, type) && !IsInternal(Codec.Deserialize<AttrMetaRecord>(meta).Flags)
					? PartsOf(DeserializeValue(stored).ToPlainText(), index)
					: []);
		}
	}

	private static RecoverEntry MapRecoverEntry(long number, RecoverRecord record)
		=> new(
			new DBRef((int)number, record.Object.CreationTime),
			record.Object.Name,
			record.Object.Type,
			ParseObjid(record.Owner),
			ParseObjid(record.Location),
			ParseObjid(record.Home),
			ParseObjid(record.Parent),
			ParseObjid(record.Zone),
			record.Flags,
			record.Powers,
			record.Roles,
			record.Account,
			ParseObjid(record.DestroyedBy),
			DateTimeOffset.FromUnixTimeMilliseconds(record.DestroyedAt),
			record is { KeptBy: { } by, KeptAt: { } at }
				? new RecoverKeep(ParseObjid(by) ?? new DBRef(-1), record.KeepReason ?? string.Empty,
					DateTimeOffset.FromUnixTimeMilliseconds(at))
				: null,
			record.Bytes);

	private static DBRef? ParseObjid(string? objid) => DBRef.TryParse(objid, out var parsed) ? parsed : null;
}
