using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Reads a real PennMUSH dump: TestData/pennmush-1.8.8p0.outdb, written by the PennMUSH oracle
/// (1.8.8p0, rev 80a1d5b; tools/oracle) from a minimal world built with:
/// <code>
/// @pcreate Alice=alicepass1          (#3)
/// @pcreate Bob=bobpass22             (#4)
/// @dig Oracle Hall=North;n,South;s   (#5 room, #6 North in Room Zero, #7 South in the hall)
/// @create Widget / @create Gadget    (#8, #9)
/// @parent #8=#9 / @lock/zone #9=#1 / @chzone #8=#9
/// @lock #8=#3|#4 / @lock/use #8=#1 / @set #8=DARK / @power #4=See_All
/// &amp;NOTE #8=He said "hi" \\ back / @set #8/NOTE=visual / @set #8/NOTE=no_command
/// think attrib_set(#8/MULTI,first line%rsecond line)
/// &amp;DESCRIBE #5=A hall for the oracle. / @link #5=#0 / @link #8=#5 / @tel #8=#5
/// @warnings #8=exit-unlinked / give *Alice=25 / @dump
/// </code>
/// </summary>
public class PennMUSHDatabaseParserTests
{
	private static readonly string FixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0.outdb");

	private static PennMUSHDatabaseParser Parser => new(NullLogger<PennMUSHDatabaseParser>.Instance);

	private static Task<PennMUSHDatabase> Fixture() => Parser.ParseFileAsync(FixturePath);

	private static async Task<PennMUSHObject> Object(int dbref)
		=> (await Fixture()).GetObject(dbref) ?? throw new InvalidOperationException($"No #{dbref} in the fixture.");

	[Test]
	public async Task EveryObjectInTheDumpIsReadWithItsNameAndType()
	{
		var database = await Fixture();

		await Assert.That(database.Objects.Select(o => (o.DBRef, o.Name, o.Type))).IsEquivalentTo(
		[
			(0, "Room Zero", PennMUSHObjectType.Room),
			(1, "One", PennMUSHObjectType.Player),
			(2, "Master Room", PennMUSHObjectType.Room),
			(3, "Alice", PennMUSHObjectType.Player),
			(4, "Bob", PennMUSHObjectType.Player),
			(5, "Oracle Hall", PennMUSHObjectType.Room),
			(6, "North", PennMUSHObjectType.Exit),
			(7, "South", PennMUSHObjectType.Exit),
			(8, "Widget", PennMUSHObjectType.Thing),
			(9, "Gadget", PennMUSHObjectType.Thing)
		]);
	}

	/// <summary>
	/// PennMUSH overloads two fields by type (hdrs/dbdefs.h): <c>exits</c> is a thing's or player's
	/// home and an exit's source; <c>location</c> is an exit's destination and a room's drop-to. The
	/// parser hands the converter what they mean.
	/// </summary>
	[Test]
	public async Task LocationAndLinkMeanWhatTheyMeanForEachType()
	{
		var widget = await Object(8);
		await Assert.That((widget.Location, widget.Link)).IsEqualTo((5, 5));

		var north = await Object(6);
		await Assert.That((north.Location, north.Link)).IsEqualTo((0, 5));

		var south = await Object(7);
		await Assert.That((south.Location, south.Link)).IsEqualTo((5, 0));

		var hall = await Object(5);
		await Assert.That((hall.Location, hall.Link)).IsEqualTo((-1, 0));

		var alice = await Object(3);
		await Assert.That((alice.Location, alice.Link)).IsEqualTo((0, 0));
	}

	[Test]
	public async Task OwnersParentsAndZonesAreRead()
	{
		var widget = await Object(8);
		await Assert.That((widget.Owner, widget.Parent, widget.Zone)).IsEqualTo((1, 9, 9));

		var alice = await Object(3);
		await Assert.That((alice.Owner, alice.Parent, alice.Zone)).IsEqualTo((3, -1, -1));
	}

	[Test]
	public async Task FlagsPowersWarningsAndPenniesAreRead()
	{
		var widget = await Object(8);
		await Assert.That(widget.Flags).IsEquivalentTo(["DARK", "NO_COMMAND"]);
		await Assert.That(widget.Warnings).IsEquivalentTo(["exit-unlinked"]);

		var bob = await Object(4);
		await Assert.That(bob.Powers).IsEquivalentTo(["See_All"]);

		var alice = await Object(3);
		await Assert.That(alice.Pennies).IsEqualTo(175);
	}

	[Test]
	public async Task LocksAreReadByType()
	{
		var widget = await Object(8);
		await Assert.That(widget.Locks).IsEquivalentTo(new Dictionary<string, string>
		{
			["Basic"] = "#3|#4",
			["Use"] = "#1"
		});
	}

	/// <summary>Values are quoted with <c>\"</c> and <c>\\</c> escaped (putstring), and may span lines.</summary>
	[Test]
	public async Task AttributesAreReadWithTheirFlagsAndEscapedValues()
	{
		var widget = await Object(8);
		var note = widget.Attributes.Single(a => a.Name == "NOTE");
		var multi = widget.Attributes.Single(a => a.Name == "MULTI");

		await Assert.That(note.Value).IsEqualTo("He said \"hi\" \\ back");
		await Assert.That(note.Flags).IsEquivalentTo(["no_command", "visual"]);
		await Assert.That(note.DerefCount).IsEqualTo(2);
		await Assert.That(multi.Value).IsEqualTo("first line\nsecond line");
		await Assert.That(multi.Flags).IsEmpty();

		var hall = await Object(5);
		await Assert.That(hall.Attributes.Single(a => a.Name == "DESCRIBE").Value).IsEqualTo("A hall for the oracle.");
	}

	/// <summary>
	/// PennMUSH 1.8 keeps an exit's and a player's aliases in its ALIAS attribute, not its name; SharpMUSH
	/// matches them through the object's own alias list, so the parser fills that too.
	/// </summary>
	[Test]
	public async Task AliasesComeFromTheAliasAttribute()
	{
		var north = await Object(6);

		await Assert.That(north.Aliases).IsEquivalentTo(["n"]);
		await Assert.That(north.Attributes.Single(a => a.Name == "ALIAS").Value).IsEqualTo("n");
	}

	[Test]
	public async Task TimestampsAreRead()
	{
		var widget = await Object(8);

		await Assert.That((widget.CreationTime, widget.ModificationTime)).IsEqualTo((1789145417L, 1789145442L));
	}

	/// <summary>
	/// A player's password is its XYXXY attribute. It becomes <see cref="PennMUSHObject.Password"/>
	/// and never an ordinary attribute, which would put the hash where softcode can read it.
	/// </summary>
	[Test]
	public async Task ThePasswordComesFromXyxxyAndIsNotAnAttribute()
	{
		var database = await Fixture();
		var alice = database.GetObject(3)!;

		await Assert.That(alice.Password).IsEqualTo(
			"2:sha512:1W86ad9dffc0c4a964cd70a3d5385e3669af9bc2d877f2e254a12be0e348e28a8866ef8e7995eec2d9f258345554a9cb0670d178753bc203121002ab96600737ec:1789145415");
		await Assert.That(database.Objects.SelectMany(o => o.Attributes).Any(a => a.Name == "XYXXY")).IsFalse();
	}

	[Test]
	public async Task AnEmptyDumpHasNoObjects()
	{
		var database = await ParseText("+V-4199422\ndbversion 6\nsavedtime \"Fri Sep 11 16:50:50 2026\"\n~0\n***END OF DUMP***\n");

		await Assert.That(database.Objects).IsEmpty();
	}

	/// <summary>Anything else is refused by name rather than read as a garbled world.</summary>
	[Test]
	public async Task SomethingThatIsNotAPennMUSHDumpIsRefused()
	{
		await Assert.That(async () => await ParseText("!0\nRoom Zero\n-1\n")).Throws<FormatException>();
	}

	private static async Task<PennMUSHDatabase> ParseText(string text)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
		return await Parser.ParseAsync(stream);
	}
}
