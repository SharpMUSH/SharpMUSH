using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The RhostMUSH flatfile reader, against a hand-made dump laid out as <c>@dump/flat</c> writes one
/// (<c>TestData/rhost-sample.flat</c>), and against broken ones.
/// </summary>
public class RhostFlatfileReaderTests
{
	internal static readonly string FlatfilePath = Path.Join(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.flat");
	internal static readonly string MailPath = Path.Join(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.mail");
	internal static readonly string FolderPath = Path.Join(AppContext.BaseDirectory, "Services", "TestData", "rhost-sample.folder");

	[Test]
	public async Task ReadsTheHeaders()
	{
		var flatfile = await RhostFlatfileReader.ReadFileAsync(FlatfilePath);

		await Assert.That(flatfile.FormatVersion).IsEqualTo(7);
		await Assert.That(flatfile.Has(RhostFlatfileReader.ExtendedFlagsFlag)).IsTrue();
		await Assert.That(flatfile.DatabaseTop).IsEqualTo(12);
		await Assert.That(flatfile.NextAttribute).IsEqualTo(260);
		await Assert.That(flatfile.UserAttributes[256].Name).IsEqualTo("CMD_HELLO");
		await Assert.That(flatfile.UserAttributes[259].Flags).IsEqualTo(0x8000u);
		await Assert.That(flatfile.Objects.Count).IsEqualTo(12);
	}

	[Test]
	public async Task ReadsEveryField()
	{
		var flatfile = await RhostFlatfileReader.ReadFileAsync(FlatfilePath);
		var exit = flatfile.Objects.First(o => o.DBRef == 7);
		var bob = flatfile.Objects.First(o => o.DBRef == 4);
		var widget = flatfile.Objects.First(o => o.DBRef == 9);

		await Assert.That(exit.Name).IsEqualTo("North;n");
		await Assert.That(exit.Type).IsEqualTo(RhostObjectType.Exit);
		await Assert.That(exit.Location).IsEqualTo(6);
		await Assert.That(exit.Exits).IsEqualTo(0);
		await Assert.That(bob.Type).IsEqualTo(RhostObjectType.Player);
		// Written as a signed int: the top bit reads back as the unsigned word.
		await Assert.That(bob.Toggles[2]).IsEqualTo(0x80000000u);
		await Assert.That(widget.Lock).IsEqualTo("(3|1)");
		await Assert.That(widget.Zones).IsEquivalentTo([10, 6]);
		await Assert.That(widget.Pennies).IsEqualTo(1);
	}

	[Test]
	public async Task SplitsTheOwnerAndFlagsPrefixAndJoinsAValueOverSeveralLines()
	{
		var flatfile = await RhostFlatfileReader.ReadFileAsync(FlatfilePath);
		var widget = flatfile.Objects.First(o => o.DBRef == 9);

		var hello = widget.Attributes.First(a => a.Number == 256);
		await Assert.That(hello.Owner).IsEqualTo(1);
		await Assert.That(hello.Flags).IsEqualTo(0x8000u);
		await Assert.That(hello.Value).StartsWith("$+hello *:");

		var secrets = widget.Attributes.First(a => a.Number == 259);
		await Assert.That(secrets.Owner).IsNull();
		await Assert.That(secrets.Value).IsEqualTo("first line\nsecond line");
	}

	[Test]
	[Arguments("+V74247\n+S1\n+N256\n!0\nLimbo\n", "the file ends")]
	[Arguments("!0\nLimbo\n***END OF DUMP***\n", "+V header")]
	[Arguments("+V75271\n+S1\n+N256\n***END OF DUMP***\n", "@dump/flat")]
	[Arguments("+V74247\r\n+S1\r\n+N256\r\n***END OF DUMP***\r\n", "Windows line endings")]
	[Arguments("+V74247\n+S1\n+N256\n!0\nLimbo\nnowhere\n", "location is not a number")]
	public async Task RefusesWhatItCannotRead(string text, string reason)
	{
		var refusal = Assert.Throws<FormatException>(() => RhostFlatfileReader.Read(text));

		await Assert.That(refusal.Message).Contains(reason);
	}

	[Test]
	[Arguments("+V74247\n+S84\n+N1355\n", DatabaseDialect.RhostMUSH)]
	[Arguments("+V74247\n!0\n", DatabaseDialect.RhostMUSH)]
	[Arguments("+V-1\ndbversion 5\n", DatabaseDialect.PennMUSH)]
	[Arguments("+V-1\nsavedtime \"Mon Jan 1 00:00:00 2024\"\n", DatabaseDialect.PennMUSH)]
	[Arguments("garbage\n", DatabaseDialect.PennMUSH)]
	public async Task TellsTheFormatsApart(string start, DatabaseDialect expected)
	{
		await Assert.That(DatabaseFormatSniffer.Sniff(new StringReader(start))).IsEqualTo(expected);
	}
}
