using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

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

		// WIZARD and ROYALTY are roles here, held rather than stored as flags.
		var aliceFlags = await alice.Object().Flags.Value.Select(f => f.Name).ToListAsync();
		await Assert.That(aliceFlags).Contains("COUNCILOR").And.Contains("KEEPALIVE");
		await Assert.That((await alice.Object().Grants.WithCancellation(CancellationToken.None)).HoldsRole(BuiltInRoles.RoyaltySlug))
			.IsTrue();
		await Assert.That((await alice.Object().ReadPowersAsync(CancellationToken.None)).Select(p => p.Name)).Contains("Boot");

		var carolFlags = await carol.Object().Flags.Value.Select(f => f.Name).ToListAsync();
		await Assert.That(carolFlags).Contains("IMMORTAL");
		await Assert.That((await carol.Object().Grants.WithCancellation(CancellationToken.None)).HoldsRole(BuiltInRoles.WizardSlug))
			.IsTrue();

		var bobGrants = await bob.Object().Grants.WithCancellation(CancellationToken.None);
		await Assert.That(bobGrants.HoldsRole(BuiltInRoles.BuilderSlug)).IsTrue();
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
			RhostList = true
		});
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("Imported from a RhostMUSH flatfile") && w.Contains("rhost_list"));
		// The reader's decisions lead the warnings.
		await Assert.That(result.Warnings[0]).StartsWith("Read a RhostMUSH flatfile");
		// %m is TinyMUX's and RhostMUSH's last command, as it is here: no PennMUSH warning about it.
		await Assert.That(result.Warnings).DoesNotContain(w => w.Contains("use %m or %=<"));
		// Rewritten softcode is a list of its own, with one warning pointing at it.
		await Assert.That(result.SoftcodeRewrites).IsNotEmpty();
		await Assert.That(result.SoftcodeRewrites).DoesNotContain(line => result.Warnings.Contains(line));
		await Assert.That(result.Warnings).Contains(w =>
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
