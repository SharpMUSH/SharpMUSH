using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IMailAliasStore"/>: global mail aliases in <see cref="Tables.MailAlias"/>, keyed by the
/// upper-cased name. A game has a handful of them, so ordered reads are a scan sorted by
/// <see cref="MailAliasRecord.Sequence"/>. Each write is one job on the single writer thread, so a
/// name check followed by a put is atomic with respect to every other write.
/// </summary>
public partial class LightningDatabase
{
	private static byte[] MailAliasKey(string name) => Keys.Upper(name);

	private static SharpMailAlias MapRecordToMailAlias(MailAliasRecord record)
		=> new(record.Name, record.Description, record.Owner, record.Members,
			(MailAliasPrivileges)record.UsePrivileges, (MailAliasPrivileges)record.SeePrivileges);

	private static MailAliasRecord MapMailAliasToRecord(SharpMailAlias alias, long sequence)
		=> new()
		{
			Sequence = sequence,
			Name = alias.Name,
			Description = alias.Description,
			Owner = alias.Owner,
			Members = alias.Members,
			UsePrivileges = (int)alias.UsePrivileges,
			SeePrivileges = (int)alias.SeePrivileges
		};

	private static List<MailAliasRecord> ReadMailAliasRecords(ITx tx)
		=> [.. tx.Range(Tables.MailAlias, []).Select(entry => Codec.Deserialize<MailAliasRecord>(entry.Value))];

	public async IAsyncEnumerable<SharpMailAlias> GetMailAliasesAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var records = Store.Read(ReadMailAliasRecords);
		foreach (var record in records.OrderBy(record => record.Sequence))
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return MapRecordToMailAlias(record);
		}

		await ValueTask.CompletedTask;
	}

	public async ValueTask<Result<SharpMailAlias>> CreateMailAliasAsync(SharpMailAlias alias,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var key = MailAliasKey(alias.Name);
			if (tx.TryGet(Tables.MailAlias, key, out _))
			{
				return (Result<SharpMailAlias>)new Error<string>($"Mail alias '{alias.Name}' already exists.");
			}

			var sequence = ReadMailAliasRecords(tx).Select(record => record.Sequence).DefaultIfEmpty(0).Max() + 1;
			tx.Put(Tables.MailAlias, key, Codec.Serialize(MapMailAliasToRecord(alias, sequence)));
			return alias;
		}, cancellationToken);

	public async ValueTask<Result<SharpMailAlias>> UpdateMailAliasAsync(string name, SharpMailAlias alias,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var key = MailAliasKey(name);
			if (!tx.TryGet(Tables.MailAlias, key, out var existing))
			{
				return (Result<SharpMailAlias>)new Error<string>($"No mail alias '{name}'.");
			}

			var newKey = MailAliasKey(alias.Name);
			var renamed = !key.AsSpan().SequenceEqual(newKey);
			if (renamed && tx.TryGet(Tables.MailAlias, newKey, out _))
			{
				return new Error<string>($"Mail alias '{alias.Name}' already exists.");
			}

			if (renamed)
			{
				tx.Delete(Tables.MailAlias, key);
			}

			var sequence = Codec.Deserialize<MailAliasRecord>(existing).Sequence;
			tx.Put(Tables.MailAlias, newKey, Codec.Serialize(MapMailAliasToRecord(alias, sequence)));
			return alias;
		}, cancellationToken);

	public async ValueTask<Found<None>> DeleteMailAliasAsync(string name, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => tx.Delete(Tables.MailAlias, MailAliasKey(name))
			? (Found<None>)new None()
			: new NotFound(), cancellationToken);

	public async ValueTask DeleteAllMailAliasesAsync(CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx => { tx.DeletePrefix(Tables.MailAlias, []); }, cancellationToken);

	public async ValueTask ReleaseMailAliasesAsync(int player, int newOwner, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			foreach (var (key, value) in tx.Range(Tables.MailAlias, []).ToList())
			{
				var record = Codec.Deserialize<MailAliasRecord>(value);
				if (record.Owner != player && !record.Members.Contains(player))
				{
					continue;
				}

				tx.Put(Tables.MailAlias, key, Codec.Serialize(record with
				{
					Owner = record.Owner == player ? newOwner : record.Owner,
					Members = [.. record.Members.Where(member => member != player)]
				}));
			}
		}, cancellationToken);
}
