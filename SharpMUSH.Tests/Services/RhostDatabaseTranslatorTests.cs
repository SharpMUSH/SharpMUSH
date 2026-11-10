using SharpMUSH.Configuration;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services;
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
	/// <summary>Existing plugins retain the single-power constructor, property, and deconstruction.</summary>
	[Test]
	public async Task LegacyPowerMappingRemainsUsable()
	{
		var mapping = new RhostPowerMapping(2, 4, "BOOT", "Boot");
		var (word, shift, name, power) = mapping;
		await Assert.That((word, shift, name, power)).IsEqualTo((2, 4, "BOOT", "Boot"));
		await Assert.That(mapping.Scopes).IsEquivalentTo(new[] { GamePowers.Find("Boot")!.Scope });
		await Assert.That((mapping with { Power = "Search" }).Power).IsEqualTo("Search");
		await Assert.That(new RhostPowerMapping(2, 4, "CUSTOM", null).Power).IsNull();
		await Assert.That(Enumerable.Range(0, 5).Select(level => RhostNameTables.PowerLevel((uint)level)))
			.IsEquivalentTo(new[] { "none", "guildmaster", "architect", "councilor", "none" });
	}

	private static async Task<PennMUSHDatabase> TranslateAsync()
		=> await RhostDatabaseTranslator.ReadAsync(RhostFlatfileReaderTests.FlatfilePath, RhostFlatfileReaderTests.MailPath,
			RhostFlatfileReaderTests.FolderPath);

	/// <summary><paramref name="text"/> with <paramref name="lines"/> added to the end of object <paramref name="dbref"/>.</summary>
	private static string WithAttributes(string text, int dbref, params string[] lines)
	{
		var all = text.Split('\n').ToList();
		var end = all.FindIndex(all.IndexOf($"!{dbref}"), line => line == "<");
		all.InsertRange(end, lines);
		return string.Join('\n', all);
	}

	private static PennMUSHObject Object(PennMUSHDatabase database, int dbref) => database.Objects.First(o => o.DBRef == dbref);

	[Test]
	public async Task GarbageIsLeftBehindAndEverythingElseKeepsItsNumberAndType()
	{
		var database = await TranslateAsync();

		await Assert.That(database.Dialect).IsEqualTo(DatabaseDialect.RhostMUSH);
		await Assert.That(database.Objects.Select(o => o.DBRef)).IsEquivalentTo(Enumerable.Range(0, 11));
		await Assert.That(Object(database, 6).Type).IsEqualTo(PennMUSHObjectType.Room);
		await Assert.That(Object(database, 9).Type).IsEqualTo(PennMUSHObjectType.Thing);
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Destroyed objects (garbage) not imported") && n.Contains("#11")))
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
		// HOME carries over as the pseudo-dbref, which the converter records as _LINKTYPE.
		await Assert.That(home.Link).IsEqualTo(-3);
		await Assert.That(database.ImportNotes.Texts().Any(n => n.Contains("left unlinked") && n.Contains("#8")))
			.IsFalse();
	}

	/// <summary>
	/// RhostMUSH's staff levels: royalty (its wizards) is the wizard role, and immortal, councilor, architect and
	/// guildmaster are roles of their own, lined up by priority among the built-in ones.
	/// </summary>
	[Test]
	public async Task StaffLevelsBecomeRolesAndFlagsOfTheirOwn()
	{
		var database = await TranslateAsync();

		await Assert.That(Object(database, 1).Flags).Contains("WIZARD");
		await Assert.That(Object(database, 3).Flags).Contains("COUNCILOR").And.DoesNotContain("ROYALTY");
		await Assert.That(Object(database, 3).Roles).IsEquivalentTo(["councilor"]);
		await Assert.That(Object(database, 4).Roles).IsEquivalentTo(["architect"]);
		await Assert.That(Object(database, 4).Powers).DoesNotContain("Builder");
		await Assert.That(Object(database, 5).Flags).Contains("IMMORTAL").And.Contains("GUILDMASTER").And.DoesNotContain("WIZARD");
		await Assert.That(Object(database, 5).Roles).IsEquivalentTo(["immortal", "guildmaster"]);

		var immortal = database.FlagDefinitions.First(f => f.Name == "IMMORTAL");
		await Assert.That(immortal.SetPermissions).IsEquivalentTo(["wizard"]);
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Flag COUNCILOR: imported as the councilor role and a flag of its own"))).IsTrue();
	}

	/// <summary>Every level gets a role, each above the built-in role whose permissions it has.</summary>
	[Test]
	public async Task LevelRolesLineUpWithTheBuiltInRoles()
	{
		var database = await TranslateAsync();
		var priority = BuiltInRoles.All.ToDictionary(role => role.Slug, role => role.Priority);
		var roles = database.Roles.ToDictionary(role => role.Slug);

		await Assert.That(roles.Keys).IsEquivalentTo(["guildmaster", "architect", "councilor", "immortal"]);
		await Assert.That(roles["guildmaster"].Priority).IsGreaterThan(priority[BuiltInRoles.PlayerSlug]).And.IsLessThan(roles["architect"].Priority);
		await Assert.That(roles["architect"].Priority).IsGreaterThan(priority[BuiltInRoles.BuilderSlug]).And.IsLessThan(priority[BuiltInRoles.RoyaltySlug]);
		await Assert.That(roles["councilor"].Priority).IsGreaterThan(priority[BuiltInRoles.RoyaltySlug]).And.IsLessThan(priority[BuiltInRoles.WizardSlug]);
		await Assert.That(roles["immortal"].Priority).IsGreaterThan(priority[BuiltInRoles.WizardSlug]).And.IsLessThan(priority[BuiltInRoles.GodSlug]);

		await Assert.That(roles["architect"].Permissions).ContainsKey(PortalPermission.GamePower("Builder"));
		await Assert.That(roles["councilor"].Permissions).ContainsKey(PortalPermission.GameRoyalty);
		await Assert.That(roles["immortal"].Permissions).ContainsKey(PortalPermission.GameWizard).And.ContainsKey(PortalPermission.ServerAdmin);
		await Assert.That(roles["immortal"].Permissions).DoesNotContainKey(PortalPermission.Administrator);
	}

	/// <summary>Each object's reality masks, eight hex digits received and eight transmitted, ride along unnamed.</summary>
	[Test]
	public async Task RealityMasksAreCarried()
	{
		var database = await TranslateAsync();

		await Assert.That(Object(database, 3).Reality).IsEqualTo(new PennMUSHReality(3, 2));
		await Assert.That(Object(database, 9).Reality).IsEqualTo(new PennMUSHReality(1, 5));
		await Assert.That(Object(database, 4).Reality).IsNull();
		await Assert.That(Object(database, 9).Attributes.Select(a => a.Name)).DoesNotContain("RLEVEL");
	}

	/// <summary>
	/// RhostMUSH's own totems are flags at once; one a game's <c>netrhost.conf</c> defines is a flag once the
	/// configuration is read, set on each object holding its bit.
	/// </summary>
	[Test]
	public async Task TotemsBecomeFlags()
	{
		var database = await TranslateAsync();
		var gadget = Object(database, 10);

		await Assert.That(gadget.Flags).Contains("MARKER0").And.Contains("SETQLABEL").And.DoesNotContain("MARKER1");
		await Assert.That(database.FlagDefinitions.First(f => f.Name == "MARKER0").SetPermissions).IsEquivalentTo(["wizard"]);
		await Assert.That(database.FlagDefinitions.First(f => f.Name == "SETQLABEL").SetPermissions).IsEmpty();
		await Assert.That(gadget.Attributes.Select(a => a.Name)).DoesNotContain("*TOTEMS");

		RhostDatabaseTranslator.MapTotems(database, [new TotemDirective("Shiny", 2, 4), new TotemDirective("Dull", 2, 8)]);

		await Assert.That(gadget.Flags).Contains("SHINY").And.DoesNotContain("DULL");
		await Assert.That(database.FlagDefinitions.Select(f => f.Name)).Contains("SHINY").And.DoesNotContain("DULL");
		await Assert.That(database.ImportNotes.Texts()).Contains(n => n.StartsWith("Totem SHINY: imported as a flag of its own") && n.Contains("#10"));
	}

	/// <summary>
	/// A cluster is one object here: its first object takes the others' attributes, and the others are not imported.
	/// A name more than one of them holds keeps the first one's value.
	/// </summary>
	[Test]
	public async Task ClustersAreMergedIntoTheirFirstObject()
	{
		var text = (await File.ReadAllTextAsync(RhostFlatfileReaderTests.FlatfilePath))
			.Replace("+N260\n", "+N262\n+A260\n1:_CLUSTER\n+A261\n1:EXTRA\n");
		text = WithAttributes(text, 9, ">260", "#9 #10");
		text = WithAttributes(text, 10, ">260", "#9 #10", ">256", "duplicate", ">261", "merged");

		var database = RhostDatabaseTranslator.Translate(RhostFlatfileReader.Read(text));
		var widget = Object(database, 9);

		await Assert.That(database.Objects.Select(o => o.DBRef)).DoesNotContain(10);
		await Assert.That(widget.Attributes.First(a => a.Name == "EXTRA").Value).IsEqualTo("merged");
		await Assert.That(widget.Attributes.Count(a => a.Name == "CMD_HELLO")).IsEqualTo(1);
		await Assert.That(widget.Attributes.Select(a => a.Name)).DoesNotContain("_CLUSTER");
		await Assert.That(database.ImportNotes.Texts()).Contains(n => n.StartsWith("Cluster #9: its 2 object(s) merged into #9"));
		await Assert.That(database.ImportNotes.Texts()).Contains(n => n.StartsWith("Cluster attribute CMD_HELLO: held by more than one"));
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
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Flag ZONEMASTER: dropped"))).IsTrue();

		await Assert.That(Object(database, 1).Flags).Contains("XTERM256").And.Contains("COLOR").And.DoesNotContain("CONNECTED");
	}

	[Test]
	public async Task TogglesCarryOverWhereSharpMUSHHasTheSettingAndAreReportedOtherwise()
	{
		var database = await TranslateAsync();
		var alice = Object(database, 3);

		await Assert.That(alice.Flags).Contains("HEAR_CONNECT").And.Contains("KEEPALIVE");
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Toggle VPAGE: dropped") && n.Contains("#3") && n.Contains("#4")))
			.IsTrue();
	}

	/// <summary>An object's overrides as <c>@permission</c> lists them: <c>+scope/reach</c> or <c>-scope</c>, in order.</summary>
	private static string Overrides(IEnumerable<PennMUSHOverride> overrides)
		=> string.Join(" ", overrides.Select(o => $"{(o.State == PermissionState.Allow ? "+" : "-")}{o.Scope}{(o.Reach is null ? "" : "/" + o.Reach)}"));

	/// <summary>A power at any level is held; one SharpMUSH lacks is created; a depower is a deny.</summary>
	[Test]
	public async Task PowersAndDepowers()
	{
		var database = await TranslateAsync();

		await Assert.That(Overrides(Object(database, 3).Overrides)).IsEqualTo("+game.boot");
		await Assert.That(Overrides(Object(database, 4).Overrides)).IsEqualTo("+game.see_queue -player.examine -game.see_all -game.nuke");
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Depower NUKE: imported as a deny on game.nuke, at every rank")))
			.IsTrue();
	}

	/// <summary>A player with these power (toggle words 3-5) and depower (6-8) fields, set as (word, shift, stored value).</summary>
	private static string Mapped(bool shifted, params (int Word, int Shift, uint Value)[] fields) => Mapped(0, shifted, fields);

	private static string Mapped(uint flags2, bool shifted, params (int Word, int Shift, uint Value)[] fields)
	{
		var toggles = new uint[8];
		foreach (var (word, shift, value) in fields)
		{
			toggles[word] |= value << shift;
		}

		var source = new RhostFlatfileObject { DBRef = 5, Name = "Powered", Flags = [(uint)RhostObjectType.Player, flags2, shifted ? RhostNameTables.DepowerShift : 0, 0], Toggles = toggles };
		return Overrides(RhostDatabaseTranslator.MapPowers(source, new RhostDatabaseTranslator.Report()));
	}

	[Test]
	public async Task APowersLevelIsItsReach()
	{
		// CHOWN_OTHER at councilor; EXAMINE_ALL at architect, on a permission that takes no reach.
		await Assert.That(Mapped(false, (2, 6, 3), (2, 10, 2))).IsEqualTo("+game.chown.any/councilor +game.see_all");
		// CHOWN_ME at guildmaster and CHOWN_ANYWHERE, which has no level, land on one permission: the wider stays.
		await Assert.That(Mapped(false, (2, 2, 1), (2, 4, 1))).IsEqualTo("+game.chown.self");
	}

	[Test]
	public async Task ADepowersLevelIsReadThroughDpshift()
	{
		// PAGE stored as 2: guildmaster, or architect on a player whose DPSHIFT is set.
		await Assert.That(Mapped(false, (5, 14, 2))).IsEqualTo("-player.page/guildmaster");
		await Assert.That(Mapped(true, (5, 14, 2))).IsEqualTo("-player.page/architect");
		// Stored as 1 it is RhostMUSH's lowest setting, which takes paging away entirely.
		await Assert.That(Mapped(false, (5, 14, 1))).IsEqualTo("-player.page");
		// GIVE keeps no reach: RhostMUSH's runs toward lower ranks, which a deny cannot.
		await Assert.That(Mapped(false, (6, 24, 3))).IsEqualTo("-player.give");
	}

	[Test]
	public async Task SlaveDeniesWhatChangesTheDatabase()
		=> await Assert.That(Mapped(0x80000000, false, (2, 14, 2)))
			.IsEqualTo("+game.see_queue -player.create -player.modify -player.chown -player.force -player.password");

	[Test]
	public async Task ADepowerWithAReachStillReplacesAPowerOnItsPermission()
		// FULLTEL, and TEL_ANYWHERE taken away at architect.
		=> await Assert.That(Mapped(false, (4, 6, 1), (6, 2, 3)))
			.IsEqualTo("-game.tport_anywhere/architect -player.teleport/architect");

	[Test]
	public async Task ADepowerBeatsThePowerItTakes()
		=> await Assert.That(Mapped(false, (3, 2, 1), (6, 2, 1))).IsEqualTo("-game.tport_anywhere -player.teleport");

	/// <summary>Two allows retain the higher rank; two denies retain the lower, wider rank.</summary>
	[Test]
	public async Task SameStateOverridesKeepTheWiderReach()
	{
		await Assert.That(Mapped(false, (3, 2, 1), (2, 20, 3))).IsEqualTo("+game.tport_anywhere/councilor");
		await Assert.That(Mapped(false, (6, 2, 2), (6, 4, 3))).Contains("-player.teleport/guildmaster");
	}

	/// <summary>FUBAR denies every everyday permission without a reach.</summary>
	[Test]
	public async Task FubarDeniesEverydayPermissions()
	{
		var actual = Mapped(0x10000, false).Split(' ');
		await Assert.That(actual).IsEquivalentTo(PortalPermission.AllowedUnlessDenied.Select(scope => $"-{scope}"));
	}

	/// <summary>A ranked allow widened to all targets needs staff action; a widened deny remains informational.</summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DroppedReachRequiresActionOnlyForAllows(bool deny)
	{
		var toggles = new uint[8];
		// EXAMINE_ALL and EXAMINE both map to game.see_all, which cannot take a reach.
		var mapping = (deny ? RhostNameTables.Depowers : RhostNameTables.Powers).Single(m => m.Name == (deny ? "EXAMINE" : "EXAMINE_ALL"));
		toggles[mapping.Word] = 2u << mapping.Shift;
		var source = new RhostFlatfileObject { DBRef = 5, Name = "Powered", Flags = [(uint)RhostObjectType.Player, 0, 0, 0], Toggles = toggles };
		var report = new RhostDatabaseTranslator.Report();
		report.Line(ImportWarningKind.Powers, "Test import");
		RhostDatabaseTranslator.MapPowers(source, report);
		var note = report.Finish().Single(n => n.Text.Contains("takes no reach") || n.Text.Contains("no reach can bound it"));
		await Assert.That(note.NeedsAction).IsEqualTo(!deny);
	}

	/// <summary>Dropping a depower loses a source restriction and requires review; dropping a power does not.</summary>
	[Test]
	[Arguments("MASTER", true)]
	[Arguments("ABUSE", true)]
	[Arguments("PERSONAL_COMMAND", true)]
	[Arguments("QUOTA", true)]
	[Arguments("EXECSCRIPT", false)]
	public async Task DroppedDepowersRequireAction(string name, bool deny)
	{
		var mapping = (deny ? RhostNameTables.Depowers : RhostNameTables.Powers).Single(m => m.Name == name);
		var toggles = new uint[8];
		toggles[mapping.Word] = 1u << mapping.Shift;
		var source = new RhostFlatfileObject { DBRef = 5, Name = "Restricted", Flags = [(uint)RhostObjectType.Player, 0, 0, 0], Toggles = toggles };
		var report = new RhostDatabaseTranslator.Report();
		report.Line(ImportWarningKind.Powers, "Test import");
		RhostDatabaseTranslator.MapPowers(source, report);
		var note = report.Finish().Single(n => n.Text.Contains($"{name}: dropped"));
		await Assert.That(note.NeedsAction).IsEqualTo(deny);
	}

	[Test]
	public async Task AttributesTakeSharpMUSHsNamesAndFlags()
	{
		var database = await TranslateAsync();
		var widget = Object(database, 9);
		var names = widget.Attributes.Select(a => a.Name).ToList();

		await Assert.That(names).Contains("DESCRIBE").And.Contains("SUCCESS").And.Contains("CMD_HELLO").And.Contains("DATA");
		await Assert.That(names).DoesNotContain("__OBJID_INTERNAL").And.DoesNotContain("CREATEDSTAMP");
		await Assert.That(widget.Attributes.First(a => a.Name == "CMD_HELLO").Flags).IsEquivalentTo(["rhost", "no_inherit"]);
		await Assert.That(widget.Attributes.First(a => a.Name == "DATA").Flags).IsEquivalentTo(["rhost", "wizard"]);
		// The +A header's flags apply to every instance.
		await Assert.That(widget.Attributes.First(a => a.Name == "SECRETS").Flags).IsEquivalentTo(["rhost", "no_inherit"]);
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
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Lock UserLock"))).IsTrue();
	}

	/// <summary>RhostMUSH writes an object in a lock as <c>(#n)</c>, after a prefix too: <c>$(#1)</c> is <c>$#1</c>.</summary>
	[Test]
	public async Task APrefixedObjectInALockLosesItsParentheses()
	{
		var flatfile = RhostFlatfileReader.ReadStrict("+V7\n+S2\n+N256\n"
			+ "!0\nLimbo\n-1\n-1\n-1\n-1\n\n1\n0\n0\n<\n"
			+ "!1\nWizard\n0\n-1\n0\n-1\n\n1\n0\n3\n>42\n=(#0)|@(#1)\n>63\n$(#1)\n>61\n!(#0)&+(#1)\n>59\nTAG:=(#1)|=(#1)\n<\n"
			+ RhostFlatfileReader.EndOfDump + "\n");

		var wizard = RhostDatabaseTranslator.Translate(flatfile).Objects.First(o => o.DBRef == 1);

		await Assert.That(wizard.Locks["Basic"].Expression).IsEqualTo("=#0|@#1");
		await Assert.That(wizard.Locks["Give"].Expression).IsEqualTo("$#1");
		await Assert.That(wizard.Locks["Page"].Expression).IsEqualTo("!(#0)&+#1");
		await Assert.That(wizard.Locks["Enter"].Expression).IsEqualTo("TAG:=(#1)|=#1");
	}

	/// <summary>The built-in attributes numbered from 2100000000: server state is left behind, the rest imported.</summary>
	[Test]
	public async Task BuiltInAttributesAboveTheUserAttributesAreNamed()
	{
		var flatfile = RhostFlatfileReader.ReadStrict("+V7\n+S1\n+N256\n"
			+ "!0\nWizard\n0\n-1\n0\n-1\n\n0\n0\n3\n>2100000000\n1 0 0\n>2100000005\n@pemit me=Mail!\n<\n"
			+ RhostFlatfileReader.EndOfDump + "\n");

		var database = RhostDatabaseTranslator.Translate(flatfile);

		await Assert.That(database.Objects[0].Attributes.Select(a => a.Name)).IsEquivalentTo(["AMAIL"]);
		await Assert.That(database.RoutineNotes.Any(n => n.StartsWith("Attribute ConnInfo: not imported"))).IsTrue();
	}

	/// <summary><c>@livewire/funceval</c>'s <c>*WireFuncEval</c> comes across as the internal attribute SharpMUSH reads it from.</summary>
	[Test]
	public async Task ALiveWiredInvocationLimitIsImported()
	{
		var flatfile = RhostFlatfileReader.ReadStrict("+V7\n+S1\n+N256\n"
			+ "!0\nWizard\n0\n-1\n0\n-1\n\n0\n0\n3\n>2100000003\n100000 1\n<\n"
			+ RhostFlatfileReader.EndOfDump + "\n");

		var attribute = RhostDatabaseTranslator.Translate(flatfile).Objects[0].Attributes.Single();

		await Assert.That(attribute.Name).IsEqualTo(LiveWire.AttributeName);
		await Assert.That(attribute.Flags).IsEquivalentTo(LiveWire.AttributeFlags);
		await Assert.That(LiveWire.Parse(attribute.Value)).IsEqualTo(new LiveWire(100000, true));
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
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Passwords stored as bcrypt") && n.Contains("#5"))).IsTrue();
		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("Passwords stored as DES crypt") && n.Contains("#4"))).IsTrue();
	}

	[Test]
	public async Task PasswordsSharpMUSHChecksAndRehashesNeedNoAction()
	{
		var database = await TranslateAsync();

		var bcrypt = database.ImportNotes.Single(n => n.Text.StartsWith("Passwords stored as bcrypt"));
		var des = database.ImportNotes.Single(n => n.Text.StartsWith("Passwords stored as DES crypt"));
		await Assert.That(bcrypt.Kind).IsEqualTo(ImportWarningKind.Players);
		await Assert.That(bcrypt.NeedsAction).IsFalse();
		await Assert.That(bcrypt.Text).Contains("rehashed at its next login");
		await Assert.That(des.Kind).IsEqualTo(ImportWarningKind.Players);
		await Assert.That(des.NeedsAction).IsFalse();
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
		// list() is not rewritten: the rhost_list option reads it the RhostMUSH way.
		await Assert.That(gadget.Attributes.First(a => a.Name == "CMD_HELLO").Value).IsEqualTo("[list(a b,##)]");

		var lines = database.SoftcodeRewrites.Select(rewrite => rewrite.ToString()).ToList();
		await Assert.That(lines).Contains(n => n.StartsWith("#9/DESCRIBE: %c/%x colour written as ansi()") && n.Contains("%<065> written as A"));
		await Assert.That(lines).Contains(n => n.StartsWith("#10/DATA:") && n.Contains("timefmt() format %0 left as written"));
		await Assert.That(lines).Contains(n => n.StartsWith("#10/DESCRIBE:") && n.Contains("check by hand: colour-code-in-register"));

		// Each rewrite carries the attribute's text as stored and as imported.
		var describe = database.SoftcodeRewrites.First(rewrite => rewrite.Attribute == "#9/DESCRIBE");
		await Assert.That(describe.Before).IsEqualTo("A shiny widget%r%xbBlue text%xn and %<065>.");
		await Assert.That(describe.After).IsEqualTo("A shiny widget%r[ansi(b,Blue text)]%band A.");
		await Assert.That(describe.ByHand).IsFalse();
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
		await Assert.That(database.ImportNotes.Texts().Any(n => n.Contains("sent anonymously"))).IsTrue();
	}

	[Test]
	public async Task TheComsysIsReported()
	{
		var database = await TranslateAsync();

		await Assert.That(database.ImportNotes.Texts().Any(n => n.StartsWith("RhostMUSH comsys channels are not imported"))).IsTrue();
	}
}
