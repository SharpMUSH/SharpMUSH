using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What each part of a RhostMUSH flatfile becomes in the import's model, and that every decision is
/// reported: flags, toggles, powers and depowers, attribute names and flags, locks, zones, passwords,
/// softcode rewrites and mail.
/// </summary>
public class RhostDatabaseTranslatorTests
{
	private static async Task<PennMUSHDatabase> TranslateAsync()
		=> await RhostDatabaseTranslator.ReadAsync(RhostFlatfileReaderTests.FlatfilePath, RhostFlatfileReaderTests.MailPath,
			RhostFlatfileReaderTests.FolderPath);

	private static PennMUSHObject Object(PennMUSHDatabase database, int dbref) => database.Objects.First(o => o.DBRef == dbref);

	[Test]
	public async Task GarbageIsLeftBehindAndEverythingElseKeepsItsNumberAndType()
	{
		var database = await TranslateAsync();

		await Assert.That(database.Dialect).IsEqualTo(DatabaseDialect.RhostMUSH);
		await Assert.That(database.Objects.Select(o => o.DBRef)).IsEquivalentTo(Enumerable.Range(0, 11));
		await Assert.That(Object(database, 6).Type).IsEqualTo(PennMUSHObjectType.Room);
		await Assert.That(Object(database, 9).Type).IsEqualTo(PennMUSHObjectType.Thing);
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Destroyed objects (garbage) not imported") && n.Contains("#11")))
			.IsTrue();
	}

	/// <summary>An exit's location field is where it leads, its exits field where it is.</summary>
	[Test]
	public async Task ExitsAreSortedOutOfTheOverloadedFields()
	{
		var database = await TranslateAsync();

		var north = Object(database, 7);
		await Assert.That(north.Location).IsEqualTo(0);
		await Assert.That(north.Link).IsEqualTo(6);

		var home = Object(database, 8);
		await Assert.That(home.Location).IsEqualTo(6);
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Exits that lead HOME left unlinked") && n.Contains("#8")))
			.IsTrue();
	}

	/// <summary>RhostMUSH's staff levels: royalty (its wizards), immortal, councilor, architect, guildmaster.</summary>
	[Test]
	public async Task StaffLevelsBecomeRolesAndFlagsOfTheirOwn()
	{
		var database = await TranslateAsync();

		await Assert.That(Object(database, 1).Flags).Contains("WIZARD");
		await Assert.That(Object(database, 3).Flags).Contains("ROYALTY").And.Contains("COUNCILOR");
		await Assert.That(Object(database, 4).Powers).Contains("Builder");
		await Assert.That(Object(database, 5).Flags).Contains("WIZARD").And.Contains("IMMORTAL").And.Contains("GUILDMASTER");

		var immortal = database.FlagDefinitions.First(f => f.Name == "IMMORTAL");
		await Assert.That(immortal.SetPermissions).IsEquivalentTo(["wizard"]);
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Flag COUNCILOR: imported as the ROYALTY flag and role and a flag of its own"))).IsTrue();
	}

	[Test]
	public async Task FlagsMapToSharpMUSHsOwnOrBecomeFlagsOfTheirOwnOrAreDropped()
	{
		var database = await TranslateAsync();

		var gadget = Object(database, 10);
		await Assert.That(gadget.Flags).Contains("TRUST").And.Contains("DEBUG").And.Contains("FUBAR").And.Contains("IC");
		await Assert.That(database.FlagDefinitions.Select(f => f.Name)).Contains("IC").And.Contains("FUBAR");
		await Assert.That(database.FlagDefinitions.First(f => f.Name == "IC").SetPermissions).IsEmpty();

		var widget = Object(database, 9);
		await Assert.That(widget.Flags).Contains("SIDEFX").And.DoesNotContain("ZONEMASTER");
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Flag ZONEMASTER: dropped"))).IsTrue();

		await Assert.That(Object(database, 1).Flags).Contains("XTERM256").And.Contains("COLOR").And.DoesNotContain("CONNECTED");
	}

	[Test]
	public async Task TogglesCarryOverWhereSharpMUSHHasTheSettingAndAreReportedOtherwise()
	{
		var database = await TranslateAsync();
		var alice = Object(database, 3);

		await Assert.That(alice.Flags).Contains("HEAR_CONNECT").And.Contains("KEEPALIVE");
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Toggle VPAGE: dropped") && n.Contains("#3") && n.Contains("#4")))
			.IsTrue();
	}

	/// <summary>A power at any level is held; one SharpMUSH lacks is created; a depower is a deny.</summary>
	[Test]
	public async Task PowersAndDepowers()
	{
		var database = await TranslateAsync();

		await Assert.That(Object(database, 3).Powers).Contains("Boot");
		await Assert.That(Object(database, 4).Powers).Contains("See_Queue");
		await Assert.That(Object(database, 4).DeniedPowers).IsEquivalentTo(["See_All"]);
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Depower NUKE: dropped"))).IsTrue();
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Power levels:"))).IsTrue();
	}

	[Test]
	public async Task AttributesTakeSharpMUSHsNamesAndFlags()
	{
		var database = await TranslateAsync();
		var widget = Object(database, 9);
		var names = widget.Attributes.Select(a => a.Name).ToList();

		await Assert.That(names).Contains("DESCRIBE").And.Contains("SUCCESS").And.Contains("CMD_HELLO").And.Contains("DATA");
		await Assert.That(names).DoesNotContain("__OBJID_INTERNAL").And.DoesNotContain("CREATEDSTAMP");
		await Assert.That(widget.Attributes.First(a => a.Name == "CMD_HELLO").Flags).IsEquivalentTo(["no_inherit"]);
		await Assert.That(widget.Attributes.First(a => a.Name == "DATA").Flags).IsEquivalentTo(["wizard"]);
		// The +A header's flags apply to every instance.
		await Assert.That(widget.Attributes.First(a => a.Name == "SECRETS").Flags).IsEquivalentTo(["no_inherit"]);
		await Assert.That(widget.CreationTime).IsGreaterThan(0);
		await Assert.That(widget.ModificationTime).IsGreaterThan(widget.CreationTime);
	}

	[Test]
	public async Task LockAttributesBecomeLocks()
	{
		var database = await TranslateAsync();
		var widget = Object(database, 9);

		await Assert.That(widget.Locks["Basic"].Expression).IsEqualTo("(#3)|(#1)");
		await Assert.That(widget.Locks["Enter"].Expression).IsEqualTo("DATA:Line*");
		await Assert.That(widget.Locks["Teleport"].Expression).IsEqualTo("(#1)");
		await Assert.That(widget.Locks["Zone"].Expression).IsEqualTo("(#4)");
		await Assert.That(Object(database, 10).Locks["Control"].Expression).IsEqualTo("(#3)");
		await Assert.That(Object(database, 6).Locks["Speech"].Expression).IsEqualTo("(#4)");
		// UserLock has no SharpMUSH lock: kept as an attribute, and reported.
		await Assert.That(widget.Attributes.Select(a => a.Name)).Contains("USERLOCK");
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Lock UserLock"))).IsTrue();
	}

	/// <summary>Without a lock attribute, the header's lock is read, its bare numbers made dbrefs.</summary>
	[Test]
	public async Task TheHeaderLockStandsInForAMissingLockAttribute()
	{
		var flatfile = await RhostFlatfileReader.ReadFileAsync(RhostFlatfileReaderTests.FlatfilePath);
		var widget = flatfile.Objects.First(o => o.DBRef == 9);
		var stripped = flatfile with
		{
			Objects = [widget with { Attributes = [.. widget.Attributes.Where(a => a.Number != 42)] }]
		};

		var database = RhostDatabaseTranslator.Translate(stripped);

		await Assert.That(database.Objects[0].Locks["Basic"].Expression).IsEqualTo("(#3|#1)");
	}

	[Test]
	public async Task ZonesComeFromTheZoneMastersAndTheirListsOfMembersAreNotZones()
	{
		var database = await TranslateAsync();

		await Assert.That(Object(database, 10).Zone).IsEqualTo(9);
		await Assert.That(Object(database, 6).Zone).IsEqualTo(9);
		await Assert.That(Object(database, 9).Zone).IsEqualTo(-1);
	}

	[Test]
	public async Task PlayersKeepTheirPasswordsAndAliasesAndEachFormIsReported()
	{
		var database = await TranslateAsync();

		await Assert.That(Object(database, 3).Password).IsEqualTo("$1$abc$9dV135Rc3U8Xbm2DxadgF0");
		await Assert.That(Object(database, 3).Aliases).IsEquivalentTo(["Al"]);
		await Assert.That(Object(database, 3).Attributes.Select(a => a.Name)).DoesNotContain("PASSWORD");
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Passwords stored as bcrypt") && n.Contains("#5"))).IsTrue();
		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("Passwords stored as DES crypt") && n.Contains("#4"))).IsTrue();
	}

	/// <summary>Colour, character codes and literal timefmt() formats are rewritten, each attribute reported.</summary>
	[Test]
	public async Task SoftcodeIsRewrittenAndEachRewriteReported()
	{
		var database = await TranslateAsync();
		var widget = Object(database, 9);
		var gadget = Object(database, 10);

		await Assert.That(widget.Attributes.First(a => a.Name == "DESCRIBE").Value)
			.IsEqualTo("A shiny widget%r[ansi(b,Blue text)]%band A.");
		await Assert.That(widget.Attributes.First(a => a.Name == "CMD_HELLO").Value)
			.IsEqualTo("$+hello *:@pemit %#=ansi(hr,Hello)%b%0, today is [timefmt($I:$M $d,secs())].");
		await Assert.That(gadget.Attributes.First(a => a.Name == "DATA").Value)
			.IsEqualTo("[timefmt($I:$m $y,secs())]|[timefmt(%0,secs())]");
		// list() is not rewritten: the mux_list option reads it the RhostMUSH way.
		await Assert.That(gadget.Attributes.First(a => a.Name == "CMD_HELLO").Value).IsEqualTo("[list(a b,##)]");

		await Assert.That(database.ImportNotes).Contains(n => n.StartsWith("#9/DESCRIBE: %c/%x colour written as ansi()") && n.Contains("%<065> written as A"));
		await Assert.That(database.ImportNotes).Contains(n => n.StartsWith("#10/DATA:") && n.Contains("timefmt() format %0 left as written"));
		await Assert.That(database.ImportNotes).Contains(n => n.StartsWith("#10/DESCRIBE:") && n.Contains("check by hand: colour-code-in-register"));
	}

	[Test]
	public async Task MailArrivesWithItsStateAndFolders()
	{
		var database = await TranslateAsync();
		var messages = database.Mail.Messages;

		await Assert.That(messages.Count).IsEqualTo(3);
		var toAlice = messages.First(m => m.To == 3);
		await Assert.That(toAlice.From).IsEqualTo(1);
		await Assert.That(toAlice.Body).IsEqualTo("Greetings");
		await Assert.That(toAlice.IsRead).IsFalse();
		await Assert.That(PennMUSHDatabaseConverter.TryParsePennTime(toAlice.Time, out var sent)).IsTrue();
		await Assert.That(sent.ToUnixTimeSeconds()).IsEqualTo(1792036688);

		var read = messages.First(m => m.To == 4 && m.Subject == "Hello there");
		await Assert.That(read.IsRead).IsTrue();
		await Assert.That(read.Folder).IsEqualTo(0);

		var archived = messages.First(m => m.To == 4 && m.From == 3);
		await Assert.That(archived.IsTagged).IsTrue();
		await Assert.That(archived.IsForwarded).IsTrue();
		await Assert.That(archived.Folder).IsEqualTo(1);
		await Assert.That(archived.Body).IsEqualTo("A second message for Bob.\nWith two lines.");
		await Assert.That(Object(database, 4).Attributes.First(a => a.Name == "MAILFOLDERS").Value).IsEqualTo("1:ARCHIVE:1");
		await Assert.That(database.ImportNotes.Any(n => n.Contains("sent anonymously"))).IsTrue();
	}

	[Test]
	public async Task TheComsysIsReported()
	{
		var database = await TranslateAsync();

		await Assert.That(database.ImportNotes.Any(n => n.StartsWith("RhostMUSH comsys channels are not imported"))).IsTrue();
	}
}
