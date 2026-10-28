using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A RhostMUSH flatfile and its mail dumps, imported whole through the same entry point as a PennMUSH
/// database, the format told from the file.
/// </summary>
public class RhostImportTests
{
	[Test]
	[Arguments("Wizard", "potrzebie")]
	[Arguments("Alice", "hunter2")]
	[Arguments("Bob", "hunter2")]
	public async Task PlayersLogInWithTheirRhostMUSHPasswords(string name, string password)
	{
		await using var world = await ImportAsync();
		var player = (await FindAsync(world, name)).Expect<SharpPlayer>();

		await Assert.That(world.Passwords.PasswordIsValid(password, player.PasswordHash)).IsTrue();
		await Assert.That(world.Passwords.PasswordIsValid("wrongpass", player.PasswordHash)).IsFalse();
	}

	[Test]
	public async Task StaffLevelsArriveAsRolesFlagsAndPowers()
	{
		await using var world = await ImportAsync();

		var alice = await FindAsync(world, "Alice");
		var bob = await FindAsync(world, "Bob");
		var carol = await FindAsync(world, "Carol");

		// The staff levels are roles here, held rather than stored as flags.
		var aliceFlags = await alice.Object().Flags.Value.Select(f => f.Name).ToListAsync();
		await Assert.That(aliceFlags).Contains("COUNCILOR").And.Contains("KEEPALIVE");
		var aliceGrants = await alice.Object().Grants.WithCancellation(CancellationToken.None);
		await Assert.That(aliceGrants.HoldsRole("councilor")).IsTrue();
		await Assert.That(aliceGrants.Shows(PortalPermission.GameRoyalty)).IsTrue();
		await Assert.That((await alice.Object().ReadPowersAsync(CancellationToken.None)).Select(p => p.Name)).Contains("Boot");

		var carolFlags = await carol.Object().Flags.Value.Select(f => f.Name).ToListAsync();
		await Assert.That(carolFlags).Contains("IMMORTAL");
		var carolGrants = await carol.Object().Grants.WithCancellation(CancellationToken.None);
		await Assert.That(carolGrants.HoldsRole("immortal")).IsTrue();
		await Assert.That(carolGrants.HoldsRole("guildmaster")).IsTrue();
		await Assert.That(carolGrants.Has(PortalPermission.GameWizard)).IsTrue();

		var bobGrants = await bob.Object().Grants.WithCancellation(CancellationToken.None);
		await Assert.That(bobGrants.HoldsRole("architect")).IsTrue();
		await Assert.That(bobGrants.Has(PortalPermission.GamePower("Builder"))).IsTrue();
		await Assert.That(bobGrants.Context.ObjectOverrides[GamePowers.Find("See_All")!.Scope]).IsEqualTo(PermissionState.Deny);
	}

	[Test]
	public async Task ThingsArriveWithTheirZoneParentLocksAndRewrittenSoftcode()
	{
		await using var world = await ImportAsync();

		var widget = await FindAsync(world, "Widget");
		var gadget = await FindAsync(world, "Gadget");
		var room = await FindAsync(world, "Testing Room");

		await Assert.That((await gadget.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key)
			.IsEqualTo(widget.Object().Key);
		await Assert.That((await room.Object().Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key)
			.IsEqualTo(widget.Object().Key);
		await Assert.That((await gadget.Object().Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().Key)
			.IsEqualTo(widget.Object().Key);
		await Assert.That(widget.Object().Locks["Basic"].LockString).IsEqualTo("(#3)|(#1)");
		await Assert.That(widget.Object().Locks.Keys).Contains("Zone").And.Contains("Enter").And.Contains("Teleport");

		var widgetFlags = await widget.Object().Flags.Value.Select(f => f.Name).ToListAsync();
		await Assert.That(widgetFlags).Contains("SIDEFX");

		var describe = await world.Database.GetAttributeAsync(widget.Object().DBRef, ["DESCRIBE"]).ToListAsync();
		await Assert.That(describe.Last().Value.ToPlainText()).Contains("[ansi(b,Blue text)]");
	}

	[Test]
	public async Task MailArrivesInItsFolders()
	{
		await using var world = await ImportAsync();
		var bob = (await FindAsync(world, "Bob")).Expect<SharpPlayer>();

		var mailbox = await world.Mediator.CreateStream(new GetAllMailListQuery(bob)).ToListAsync();

		await Assert.That(mailbox.Count).IsEqualTo(2);
		await Assert.That(mailbox.Select(m => m.Folder)).Contains("ARCHIVE").And.Contains("INBOX");
	}

	/// <summary>The options RhostMUSH softcode is written for are on, and the import says so.</summary>
	[Test]
	public async Task TurnsOnTheRhostMUSHCompatibilityOptions()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Files(), new Progress<ConversionProgress>());

		var stored = (await world.ExpandedData.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)))!;
		await Assert.That(stored.Compatibility).IsEqualTo(stored.Compatibility with
		{
			ParenGroups = true,
			TinyTrimFun = true,
			RhostEdit = true,
			RhostLattr = true,
			RhostPrintf = true,
			RhostMapArgs = true,
			SidefxFlag = true,
			RhostList = true,
			Dialect = "rhost"
		});
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("Imported from a RhostMUSH flatfile") && w.Contains("rhost_list"));
		// RhostMUSH's mail command, added beside the aliases @mail already had.
		await Assert.That(stored.Alias.CommandAliases["@MAIL"]).Contains("mail");
		await Assert.That(stored.Alias.CommandAliases["@EMIT"]).IsEquivalentTo(["emit"]);
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("mail is now another name for @mail"));
		// The reader's decisions lead the warnings.
		await Assert.That(result.Warnings[0].Text).StartsWith("Read a RhostMUSH flatfile");
		// %m is TinyMUX's and RhostMUSH's last command, as it is here: no PennMUSH warning about it.
		await Assert.That(result.Warnings.Texts()).DoesNotContain(w => w.Contains("use %m or %=<"));
		// Rewritten softcode is a list of its own, with one warning pointing at it.
		await Assert.That(result.SoftcodeRewrites).IsNotEmpty();
		await Assert.That(result.SoftcodeRewrites).DoesNotContain(rewrite => result.Warnings.Texts().Contains(rewrite.ToString()));
		await Assert.That(result.SourceName).IsEqualTo("RhostMUSH");
		await Assert.That(result.Warnings.Texts()).Contains(w =>
			w.StartsWith($"Softcode was rewritten in {result.SoftcodeRewrites.Count} attribute(s) so SharpMUSH reads it as RhostMUSH did"));
	}

	/// <summary>A format chosen by the caller is the one the file is read in.</summary>
	[Test]
	public async Task AnExplicitFormatIsHonoured()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(
			Files() with { Format = DatabaseImportFormat.RhostMUSH }, new Progress<ConversionProgress>());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.PlayersConverted).IsGreaterThanOrEqualTo(4);
	}

	/// <summary>
	/// With its <c>netrhost.conf</c>, the game's reality levels are layers: the first bit is <c>normal</c>, each other
	/// named bit a layer of its name, and each object on the layers its masks have bits of. A room transmits on every
	/// layer by RhostMUSH's default, and a level's own description attribute describes an object on that layer.
	/// </summary>
	[Test]
	public async Task RealityLevelsArriveAsLayers()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await RhostDatabaseTranslator.ReadAsync(RhostFlatfileReaderTests.FlatfilePath, null, null);
		database.Reality = new RealityDirectives(
			[new RealityLevelDirective("Real", 1, "DESC"), new RealityLevelDirective("Umbra", 2, "DESC"), new RealityLevelDirective("Spirit World", 4, "DATA")],
			new Dictionary<string, uint>());
		var result = await world.Converter.ConvertDatabaseAsync(database);
		await Assert.That(result.Errors).IsEmpty();

		var config = await world.Services.GetRequiredService<RealityPolicy>().ConfigurationAsync();
		await Assert.That(config.Enabled).IsTrue();
		await Assert.That(config.Layers).IsEquivalentTo(["normal", "umbra", "spirit_world"]);

		var alice = await LayersAsync(world, "Alice");
		await Assert.That(alice.Receive).IsEquivalentTo(["normal", "umbra"]);
		await Assert.That(alice.Transmit).IsEquivalentTo(["umbra"]);

		var widget = await LayersAsync(world, "Widget");
		await Assert.That(widget.Transmit).IsEquivalentTo(["normal", "spirit_world"]);
		await Assert.That(widget.Descriptions["spirit_world"]).IsEqualTo("DATA");

		var room = await LayersAsync(world, "Testing Room");
		await Assert.That(room.Receive).IsEquivalentTo(["normal"]);
		await Assert.That(room.Transmit).IsEquivalentTo(["normal", "umbra", "spirit_world"]);

		var bob = await LayersAsync(world, "Bob");
		await Assert.That(bob.Receive).IsEquivalentTo(["normal"]);
		await Assert.That(bob.Transmit).IsEquivalentTo(["normal"]);
	}

	/// <summary>
	/// Two bits never become one layer: a level named like another bit's layer gets its bit's number, and a bit only
	/// a level of several bits names is a layer of its own.
	/// </summary>
	[Test]
	public async Task EachBitIsALayerOfItsOwn()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await RhostDatabaseTranslator.ReadAsync(RhostFlatfileReaderTests.FlatfilePath, null, null);
		database.Reality = new RealityDirectives(
			[new RealityLevelDirective("Real", 1, "DESC"), new RealityLevelDirective("Normal", 2, "DESC"), new RealityLevelDirective("Spirit", 12, "DESC")],
			new Dictionary<string, uint>());
		var result = await world.Converter.ConvertDatabaseAsync(database);
		await Assert.That(result.Errors).IsEmpty();

		var config = await world.Services.GetRequiredService<RealityPolicy>().ConfigurationAsync();
		await Assert.That(config.Layers).IsEquivalentTo(["normal", "normal-1", "spirit", "spirit-3"]);

		var alice = await LayersAsync(world, "Alice");
		await Assert.That(alice.Receive).IsEquivalentTo(["normal", "normal-1"]);
		await Assert.That(alice.Transmit).IsEquivalentTo(["normal-1"]);

		var widget = await LayersAsync(world, "Widget");
		await Assert.That(widget.Transmit).IsEquivalentTo(["normal", "spirit"]);
	}

	/// <summary>
	/// A world that uses only the normal layer still turns reality on when an object keeps settings of its own:
	/// Alice transmits only on a bit no level names, so no one sees her.
	/// </summary>
	[Test]
	public async Task SettingsOnTheNormalLayerAloneTurnRealityOn()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await RhostDatabaseTranslator.ReadAsync(RhostFlatfileReaderTests.FlatfilePath, null, null);
		database.Reality = new RealityDirectives([new RealityLevelDirective("Real", 1, "DESC")], new Dictionary<string, uint>());
		var result = await world.Converter.ConvertDatabaseAsync(database);
		await Assert.That(result.Errors).IsEmpty();

		await Assert.That((await world.Services.GetRequiredService<RealityPolicy>().ConfigurationAsync()).Enabled).IsTrue();
		await Assert.That((await LayersAsync(world, "Alice")).Transmit).IsEmpty();
	}

	/// <summary>Without the configuration that names them, the levels cannot be layers, and the report says so.</summary>
	[Test]
	public async Task RealityLevelsWithoutTheirNamesAreReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Files(), new Progress<ConversionProgress>());

		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("Reality levels on 2 object(s) not imported"));
		await Assert.That((await world.Services.GetRequiredService<RealityPolicy>().ConfigurationAsync()).Enabled).IsFalse();
	}

	private static async Task<ObjectReality> LayersAsync(IsolatedImportWorld world, string name)
		=> await world.Services.GetRequiredService<RealityPolicy>().ReadObjectAsync((await FindAsync(world, name)).Object().DBRef)
			?? throw new InvalidOperationException($"{name} has no reality profile");

	private static DatabaseImportFiles Files()
		=> new(RhostFlatfileReaderTests.FlatfilePath, RhostFlatfileReaderTests.MailPath, null, RhostFlatfileReaderTests.FolderPath);

	private static async Task<IsolatedImportWorld> ImportAsync()
	{
		var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Files(), new Progress<ConversionProgress>());
		await Assert.That(result.Errors).IsEmpty();
		return world;
	}

	private static async Task<AnySharpObject> FindAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().FirstAsync(o => o.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}
}
