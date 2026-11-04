using System.Security.Cryptography;
using DotNext.Threading;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Attribute values and mail compressed with zstd dictionaries of their own, as feed lines are: the bundled ones from
/// the first value, short values left as they are, values written before compression rewritten by the upkeep, a
/// world's own dictionary once it holds enough, and a value that cannot be read.
/// </summary>
public class ValueCompressionTests : LightningDatabaseFixture
{
	private SharpPlayer _god = null!;

	[Before(Test)]
	public async Task FindGod() => _god = (await Db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private static readonly DateTimeOffset At = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	private static int DictionaryOf(byte[] value)
		=> value[0] == LightningDatabase.CompressedValue ? (value[1] << 8) | value[2] : -1;

	private byte[] Stored(TableDef table, byte[] key) => Db.Store.Read(tx => tx.TryGet(table, key, out var value) ? value : [])!;

	private async Task<string> ValueOf(int dbref, string name)
		=> (await Db.GetAttributeAsync(new DBRef(dbref), [name]).LastAsync()).Value.ToPlainText();

	/// <summary>Softcode in a vocabulary of its own, so a dictionary trained on it beats the bundled one.</summary>
	private static string Softcode(int index)
		=> $"$+zorblax{index % 7} *:@pemit %#=[u(#{index % 50}/FN_QUILLWORT,%0,frobnicate({index},glimmerstone))] "
			+ $"[iter(lattr(%!/ZORBLAX_{index % 11}_*),[name(##)] wibbles the {(index % 2 == 0 ? "snarfle" : "quonk")} at #@.,,%r)]";

	[Test]
	[Arguments("attribute-values-0.zdict", "54CFA45DE59B074AF031B957D02FADE44F8369FDEAD148A50B0B20CF5F71A037")]
	[Arguments("mail-0.zdict", "F260C9B3FB591EAF539C880F5C23F3BF89C1BA2B7139197A4041560DB7774C8F")]
	public async Task TheBundledDictionaries_NeverChange(string resource, string hash)
	{
		// Values in every world are compressed with these; a better default is a new file under a new id.
		var table = new[] { LightningDatabase.AttributeValues, LightningDatabase.MailValues }.Single(table => table.Resource == resource);
		await Assert.That(Convert.ToHexString(SHA256.HashData(table.Bundled))).IsEqualTo(hash);
	}

	[Test]
	public async Task ALongAttributeValue_IsCompressed_AndAShortOneStaysAsItIs()
	{
		var code = Softcode(1);
		await Db.SetAttributeAsync(new DBRef(0), ["LONG"], MarkupText.Plain(code), _god);
		await Db.SetAttributeAsync(new DBRef(0), ["SHORT"], MarkupText.Plain("42"), _god);

		var stored = Stored(Tables.AttrVal, Keys.Attr(0, "LONG"));
		await Assert.That(DictionaryOf(stored)).IsEqualTo(LightningDatabase.BundledDictionaryId);
		await Assert.That(stored.Length).IsLessThan(LightningDatabase.AttributeValueJson(MarkupText.Plain(code)).Length);
		await Assert.That(Stored(Tables.AttrVal, Keys.Attr(0, "SHORT"))[0]).IsEqualTo((byte)'{');

		await ReopenAsync();
		await Assert.That(await ValueOf(0, "LONG")).IsEqualTo(code);
		await Assert.That(await ValueOf(0, "SHORT")).IsEqualTo("42");
	}

	/// <summary>A world's attribute values from before compression are rewritten by the upkeep and read as they did.</summary>
	[Test]
	public async Task AttributesStoredAsJson_AreCompressedByTheUpkeep()
	{
		for (var index = 0; index < 20; index++)
			await Db.SetAttributeAsync(new DBRef(0), [$"CODE_{index}"], MarkupText.Plain(Softcode(index)), _god);
		await Db.Store.WriteAsync(tx =>
		{
			for (var index = 0; index < 20; index++)
				tx.Put(Tables.AttrVal, Keys.Attr(0, $"CODE_{index}"), LightningDatabase.AttributeValueJson(MarkupText.Plain(Softcode(index))));
			return true;
		});

		var compaction = await Db.CompactAsync(CompressedValues.Attributes, At);

		await Assert.That(compaction.Rewritten).IsEqualTo(20);
		for (var index = 0; index < 20; index++)
		{
			await Assert.That(DictionaryOf(Stored(Tables.AttrVal, Keys.Attr(0, $"CODE_{index}")))).IsEqualTo(LightningDatabase.BundledDictionaryId);
			await Assert.That(await ValueOf(0, $"CODE_{index}")).IsEqualTo(Softcode(index));
		}

		await Assert.That((await Db.CompactAsync(CompressedValues.Attributes, At)).Rewritten).IsEqualTo(0).Because("the pass is done");
	}

	/// <summary>A world with enough softcode trains an attribute dictionary of its own and every long value moves to it.</summary>
	[Test]
	public async Task AWorldWithEnoughAttributes_TrainsItsOwnDictionary()
	{
		await Db.SetAttributesAsync(new DBRef(0), [.. Enumerable.Range(0, 600)
			.Select(index => new AttributeWrite([$"CODE_{index}"], MarkupText.Plain(Softcode(index)), _god, []))]);

		var compaction = await Db.CompactAsync(CompressedValues.Attributes, At);

		await Assert.That(compaction.Trained).IsTrue();
		var dictionaries = await Db.GetDictionariesAsync(CompressedValues.Attributes);
		await Assert.That(dictionaries.Generations.Single(generation => generation.Current).Id).IsEqualTo(1);
		await Assert.That(dictionaries.Generations.Single(generation => generation.Id == 1).Values).IsGreaterThanOrEqualTo(600).Because("the seeded world holds long values of its own");
		await Assert.That(dictionaries.Runs.Single().Kept).IsEqualTo(1);
		foreach (var index in new[] { 0, 299, 599 })
			await Assert.That(await ValueOf(0, $"CODE_{index}")).IsEqualTo(Softcode(index));

		// A value written now is made with the world's dictionary.
		await Db.SetAttributeAsync(new DBRef(0), ["LATER"], MarkupText.Plain(Softcode(1000)), _god);
		await Assert.That(DictionaryOf(Stored(Tables.AttrVal, Keys.Attr(0, "LATER")))).IsEqualTo(1);
	}

	/// <summary>A value whose dictionary the world doesn't have reads as unreadable, keeping its access metadata.</summary>
	[Test]
	public async Task AnAttributeValueThatCannotBeDecompressed_ReadsAsUnreadable()
	{
		await Db.SetAttributeAsync(new DBRef(0), ["LOST"], MarkupText.Plain(Softcode(3)), _god);
		await Db.Store.WriteAsync(tx =>
		{
			tx.Put(Tables.AttrVal, Keys.Attr(0, "LOST"), [LightningDatabase.CompressedValue, 0, 9, 1, 2, 3, 4]);
			return true;
		});

		var lost = await Db.GetAttributeAsync(new DBRef(0), ["LOST"]).LastAsync();

		await Assert.That(lost.IsUnreadable).IsTrue();
		await Assert.That(lost.Value.ToPlainText()).IsEqualTo("");
	}

	private static SharpMail NewMail(string subject, string content) => new()
	{
		DateSent = At,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Cleared = false,
		Forwarded = false,
		Folder = "INBOX",
		Content = MarkupText.Plain(content),
		Subject = MarkupText.Plain(subject),
		From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};

	private static string Letter(int index)
		=> $"Dear Morwenna, the caravan from Thessaly reached the {(index % 2 == 0 ? "salt flats" : "glass dunes")} on day {index}. "
			+ "The quartermaster counts forty barrels of lamp oil and asks whether the council will pay in silver or in favours.";

	[Test]
	public async Task Mail_IsCompressed_AndReadsBackEveryWay()
	{
		var sender = (await Db.GetObjectNodeAsync(await Db.CreatePlayerAsync("Sender", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		var recipient = (await Db.GetObjectNodeAsync(await Db.CreatePlayerAsync("Recipient", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();

		await Db.SendMailAsync(sender.Object, recipient, NewMail("Caravan", Letter(1)));

		var stored = Db.Store.Read(tx => tx.Range(Tables.Mail, []).Single().Value);
		await Assert.That(DictionaryOf(stored)).IsEqualTo(LightningDatabase.BundledDictionaryId);
		var inbox = await Db.GetIncomingMailsAsync(recipient, "INBOX").ToListAsync();
		await Assert.That(inbox.Single().Content.ToPlainText()).IsEqualTo(Letter(1));
		await Assert.That(inbox.Single().Subject.ToPlainText()).IsEqualTo("Caravan");
		await Assert.That((await Db.GetAllSystemMailAsync().SingleAsync()).Content.ToPlainText()).IsEqualTo(Letter(1));
		await Assert.That(await Db.GetAllMailRecipientsAsync().CountAsync()).IsEqualTo(1);
		await Assert.That((await Db.GetIncomingMailSummariesAsync(recipient, "INBOX").SingleAsync()).Subject.ToPlainText()).IsEqualTo("Caravan");
	}

	[Test]
	public async Task AWorldWithEnoughMail_TrainsItsOwnDictionary()
	{
		var sender = (await Db.GetObjectNodeAsync(await Db.CreatePlayerAsync("Sender", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		var recipient = (await Db.GetObjectNodeAsync(await Db.CreatePlayerAsync("Recipient", "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();
		for (var index = 0; index < 500; index++)
			await Db.SendMailAsync(sender.Object, recipient, NewMail($"Caravan {index}", Letter(index)));

		var compaction = await Db.CompactAsync(CompressedValues.Mail, At);

		await Assert.That(compaction).IsEqualTo(new DictionaryCompaction(true, 500));
		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.Mail, []).All(entry => DictionaryOf(entry.Value) == 1))).IsTrue();
		var inbox = await Db.GetIncomingMailsAsync(recipient, "INBOX").ToListAsync();
		await Assert.That(inbox.Select(mail => mail.Content.ToPlainText())).IsEquivalentTo(Enumerable.Range(0, 500).Select(Letter));

		var rolled = await Db.UseDictionaryAsync(CompressedValues.Mail, LightningDatabase.BundledDictionaryId);
		await Assert.That(rolled.Expect<long>()).IsEqualTo(500);
		await Assert.That(Db.Store.Read(tx => tx.Range(Tables.Mail, []).All(entry => DictionaryOf(entry.Value) == 0))).IsTrue();
	}

	/// <summary>Each kind keeps its own dictionaries: training one leaves the others' state alone.</summary>
	[Test]
	public async Task EachKindHasDictionariesOfItsOwn()
	{
		await Db.SetAttributesAsync(new DBRef(0), [.. Enumerable.Range(0, 600)
			.Select(index => new AttributeWrite([$"CODE_{index}"], MarkupText.Plain(Softcode(index)), _god, []))]);
		await Db.CompactAsync(CompressedValues.Attributes, At);

		await Assert.That((await Db.GetDictionariesAsync(CompressedValues.Attributes)).Generations.Count).IsEqualTo(2);
		await Assert.That((await Db.GetDictionariesAsync(CompressedValues.Mail)).Generations.Count).IsEqualTo(1);
		await Assert.That((await Db.GetDictionariesAsync(CompressedValues.FeedLines)).Generations.Count).IsEqualTo(1);
		await Assert.That(await Db.UseDictionaryAsync(CompressedValues.Mail, 1) is NotFound).IsTrue();
	}
}
