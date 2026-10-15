using System.Text;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The TinyMUX flatfile reader: the file format, and what each TinyMUX number becomes. The fixtures are
/// hand-written in TinyMUX's formats (<c>Services/TestData/tinymux-*</c>).
/// </summary>
public class TinyMuxDatabaseReaderTests
{
	private static string Fixture(string name) => Path.Join(AppContext.BaseDirectory, "Services", "TestData", name);

	private static Task<PennMUSHDatabase> ReadV5() => TinyMuxDatabaseReader.ReadFileAsync(Fixture("tinymux-v5.flat"));

	private static PennMUSHObject Object(PennMUSHDatabase database, int dbref) => database.Objects.First(o => o.DBRef == dbref);

	[Test]
	public async Task SniffsTheHeader()
	{
		await Assert.That(TinyMuxDatabaseReader.IsTinyMuxFlatfile(Fixture("tinymux-v5.flat"))).IsTrue();
		await Assert.That(TinyMuxDatabaseReader.IsTinyMuxFlatfile(Fixture("tinymux-v2.flat"))).IsTrue();
		await Assert.That(TinyMuxDatabaseReader.IsTinyMuxFlatfile(Fixture("pennmush-1.8.8p0.outdb"))).IsFalse();
	}

	[Test]
	public async Task ReadsEveryObjectButGarbage()
	{
		var database = await ReadV5();

		await Assert.That(database.SourceName).IsEqualTo("TinyMUX");
		await Assert.That(database.Objects.Select(o => o.DBRef)).IsEquivalentTo([0, 1, 2, 3, 4, 5, 6, 7, 9, 10]);
		await Assert.That(Object(database, 1).Type).IsEqualTo(PennMUSHObjectType.Player);
		await Assert.That(Object(database, 6).Type).IsEqualTo(PennMUSHObjectType.Exit);
	}

	/// <summary>An exit's location field is its destination and its exits field its source; a thing's link is its home.</summary>
	[Test]
	public async Task SortsTheOverloadedFieldsByType()
	{
		var database = await ReadV5();
		var exit = Object(database, 6);
		var widget = Object(database, 5);

		await Assert.That(exit.Location).IsEqualTo(0);
		await Assert.That(exit.Link).IsEqualTo(2);
		await Assert.That(exit.Name).IsEqualTo("North;n");
		await Assert.That(widget.Location).IsEqualTo(0);
		await Assert.That(widget.Link).IsEqualTo(0);
		await Assert.That(widget.Zone).IsEqualTo(7);
		await Assert.That(Object(database, 0).Exits).IsEqualTo(6);
	}

	[Test]
	public async Task NamesFlagsAndPowers()
	{
		var database = await ReadV5();

		await Assert.That(Object(database, 1).Flags).Contains("WIZARD").And.Contains("ANSI").And.Contains("COLOR");
		await Assert.That(Object(database, 5).Flags).Contains("TRANSPARENT").And.Contains("DESTROY_OK")
			.And.Contains("KEY").And.Contains("MARKER0");
		await Assert.That(Object(database, 4).Flags).Contains("XTERM256").And.Contains("STAFF");
		await Assert.That(Object(database, 9).Flags).Contains("TERSE");
		await Assert.That(Object(database, 4).Powers).Contains("See_All").And.Contains("chown_anything").And.Contains("Builder");
		await Assert.That(Object(database, 4).Permissions).IsEquivalentTo(["control.all"]);
		await Assert.That(Object(database, 10).Permissions).IsEquivalentTo(["server.operate"]);
	}

	/// <summary>Only the custom definitions some object uses are created.</summary>
	[Test]
	public async Task CreatesTheCustomDefinitionsInUse()
	{
		var database = await ReadV5();

		await Assert.That(database.FlagDefinitions.Select(f => f.Name)).IsEquivalentTo(["KEY", "STAFF", "MARKER0"]);
		await Assert.That(database.PowerDefinitions.Select(p => p.Name)).IsEquivalentTo(["chown_anything"]);
	}

	[Test]
	public async Task NamesAttributesAndTheirFlags()
	{
		var database = await ReadV5();
		var widget = Object(database, 5);

		var hello = widget.Attributes.First(a => a.Name == "CMD_HELLO");
		await Assert.That(hello.Value).IsEqualTo("$hello:@pemit %#=Hi");
		await Assert.That(hello.Owner).IsEqualTo(1);
		await Assert.That(hello.Flags).IsEquivalentTo(["visual"]);

		var secret = widget.Attributes.First(a => a.Name == "SECRET");
		await Assert.That(secret.Flags).IsEquivalentTo(["wizard", "mortal_dark"]);

		await Assert.That(widget.Attributes.Select(a => a.Name)).DoesNotContain("GONE");
		await Assert.That(widget.Attributes.Select(a => a.Name)).Contains("DESCRIBE");
		await Assert.That(Object(database, 1).Attributes.Select(a => a.Name)).Contains("AWAY");
		await Assert.That(Object(database, 6).Attributes.Select(a => a.Name)).Contains("SUCCESS");
	}

	[Test]
	public async Task LiftsPasswordsAliasesAndTimes()
	{
		var database = await ReadV5();
		var wizard = Object(database, 1);
		var widget = Object(database, 5);

		await Assert.That(wizard.Password).StartsWith("$SHA1$");
		await Assert.That(Object(database, 10).Password).IsEqualTo("1:sha1:" +
			Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData("pennpass"u8)) + ":0");
		await Assert.That(wizard.Aliases).IsEquivalentTo(["Wiz"]);
		await Assert.That(wizard.Attributes.Select(a => a.Name)).DoesNotContain("PASSWORD");
		await Assert.That(widget.CreationTime).IsEqualTo(new DateTimeOffset(new DateTime(2026, 9, 4, 12, 1, 45, DateTimeKind.Local)).ToUnixTimeSeconds());
		await Assert.That(widget.ModificationTime).IsEqualTo(new DateTimeOffset(new DateTime(2026, 9, 5, 8, 30, 0, DateTimeKind.Local)).ToUnixTimeSeconds());
		await Assert.That(widget.Attributes.Select(a => a.Name)).DoesNotContain("MONEY");
	}

	[Test]
	public async Task TurnsLockAttributesIntoLocks()
	{
		var database = await ReadV5();
		var widget = Object(database, 5);

		await Assert.That(widget.Locks["Basic"].Expression).IsEqualTo("#4|DESCRIBE:*widget*");
		await Assert.That(widget.Locks["Enter"].Expression).IsEqualTo("@#7");
		await Assert.That(widget.Locks["Telout"].Expression).IsEqualTo("!#4");
		// Not AUDITORIUM, where TinyMUX ignores a SpeechLock: kept, but where nothing checks it.
		await Assert.That(widget.Locks.ContainsKey("Speech")).IsFalse();
		await Assert.That(widget.Locks["SpeechLock"].Expression).IsEqualTo("#1");
		await Assert.That(widget.Attributes.Select(a => a.Name)).DoesNotContain("DEFAULTLOCK");
	}

	/// <summary>TinyMUX grants control of a zone through the zone object's EnterLock; SharpMUSH through its Zone lock.</summary>
	[Test]
	public async Task CopiesAZoneObjectsEnterLockToItsZoneLock()
	{
		var database = await ReadV5();

		await Assert.That(Object(database, 7).Locks["Zone"].Expression).IsEqualTo("#4");
	}

	[Test]
	public async Task DecodesColour()
	{
		var database = await ReadV5();

		await Assert.That(Object(database, 0).Attributes.First(a => a.Name == "DESCRIBE").Value)
			.IsEqualTo("Welcome to \u001b[1m\u001b[31mLimbo\u001b[0m.");
		await Assert.That(Object(database, 5).Attributes.First(a => a.Name == "DESCRIBE").Value)
			.IsEqualTo("A \u001b[1m\u001b[38;2;200;135;100mwidget\u001b[0m sits here.");
	}

	[Test]
	public async Task SetsTheTinyMuxOptions()
	{
		var database = await ReadV5();

		await Assert.That(database.SourceOptions["mux_logic"] is true).IsTrue();
		await Assert.That(database.SourceOptions["rhost_list"] is true).IsTrue();
		await Assert.That(database.SourceOptions["innermost_tokens"] is true).IsTrue();
		await Assert.That(database.SourceOptions["function_recursion_limit"]).IsEqualTo(500u);
		await Assert.That(database.SourceOptions.ContainsKey("http_qregisters")).IsFalse();
		await Assert.That(database.SharesPipingSubstitutions).IsTrue();
	}

	/// <summary>Every lossy choice is in the notes the import reports.</summary>
	[Test]
	public async Task ReportsWhatItChanged()
	{
		var notes = (await ReadV5()).ImportNotes;

		await Assert.That(notes).Contains(note => note.StartsWith("Flag SEETHRU imported as TRANSPARENT"));
		await Assert.That(notes).Contains(note => note.StartsWith("Power control_all granted as the control.all permission"));
		await Assert.That(notes).Contains(note => note.StartsWith("Flag KEY imported as a custom flag"));
		await Assert.That(notes).Contains(note => note.Contains("SpeechLock on an object that is not AUDITORIUM"));
		await Assert.That(notes).Contains(note => note.StartsWith("Zone object's EnterLock copied"));
		await Assert.That(notes).Contains(note => note.StartsWith("Imported from a TinyMUX flatfile") && note.Contains("rhost_list"));
		await Assert.That(notes).Contains(note => note.StartsWith("Attribute number 25 not imported"));
	}

	/// <summary>Version 2: Latin-1 text, raw ANSI colour, and the default lock in each object's header.</summary>
	[Test]
	public async Task ReadsAVersionTwoFlatfile()
	{
		var database = await TinyMuxDatabaseReader.ReadFileAsync(Fixture("tinymux-v2.flat"));

		await Assert.That(Object(database, 0).Name).IsEqualTo("Café");
		await Assert.That(Object(database, 0).Attributes.First(a => a.Name == "DESCRIBE").Value)
			.IsEqualTo("\u001b[1;31mred\u001b[0m room");
		await Assert.That(Object(database, 0).Locks).IsEmpty();
		await Assert.That(Object(database, 1).Locks["Basic"].Expression).IsEqualTo("#1|!#2");
		await Assert.That(Object(database, 2).Locks["Basic"].Expression).IsEqualTo("SEX:m*|#1");
		await Assert.That(Object(database, 1).Password).IsEqualTo("XXtxz3x8n56Ig");
	}

	[Test]
	[Arguments("+X996101\n!0\n\"x\"\n", "ends")]
	[Arguments("+X996102\n***END OF DUMP***\n", "version 6")]
	[Arguments("+X997125\n***END OF DUMP***\n", "separate database")]
	[Arguments("+V2\n", "not a TinyMUX flatfile")]
	public async Task RefusesWhatItCannotRead(string text, string expected)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

		await Assert.That(() => TinyMuxDatabaseReader.Read(stream)).Throws<FormatException>()
			.WithMessageContaining(expected);
	}

	[Test]
	public async Task ReadsQuotedStringsAcrossLines()
	{
		var reader = new TinyMuxFlatfileReader(new StringReader("\"a\\\"b\\\\c\\e\\n\\td\nsecond\" trailing\nnext\n"), "test");

		await Assert.That(reader.ReadString()).IsEqualTo("a\"b\\c\u001b\n\td\nsecond");
		await Assert.That(reader.ReadLine()).IsEqualTo("next");
	}
}
