using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// <see cref="PackageWriteTransaction"/> on its own: whatever it wrote, and however often, a revert
/// returns to the state before its first write, a commit keeps everything, and an exception that
/// unwinds past an uncommitted transaction reverts it.
/// </summary>
public class PackageWriteTransactionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Database;

	private async Task<SharpPlayer> PackageManagerAsync() =>
		(await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();

	private async Task<PackageWriteTransaction> BeginAsync() => new(
		Mediator, Database, Database, Database, Registry, (IApplicationRegistryService)Database, await PackageManagerAsync());

	/// <summary>A thing with a name, one attribute, no DARK flag and no use lock, and a baseline row for it.</summary>
	private async Task<(AnySharpObject Node, DBRef DBRef, string Objid)> SubjectAsync(string name, string package)
	{
		var pm = await PackageManagerAsync();
		AnySharpContainer home = pm;
		var dbref = await Database.CreateThingAsync(name, home, pm, home);
		await Database.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("original"), pm);
		var node = (await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();
		var objid = node.Object().DBRef.ToString();
		await Registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(package, objid, "TX_VALUE", "original", "h", "1.0.0"));
		return (node, node.Object().DBRef, objid);
	}

	private async Task WriteEverythingAsync(PackageWriteTransaction writes, AnySharpObject node, DBRef dbref, string objid, string package)
	{
		var pm = await PackageManagerAsync();
		var dark = (await Database.GetObjectFlagAsync("DARK"))!;
		await writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("first"), pm, CancellationToken.None);
		await writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("second"), pm, CancellationToken.None);
		await writes.SetAttributeAsync(dbref, ["TX_ADDED"], MarkupText.Plain("added"), pm, CancellationToken.None);
		await writes.SetNameAsync(node, "Tx Renamed", CancellationToken.None);
		await writes.SetFlagAsync(node, dark, CancellationToken.None);
		await Assert.That((await writes.SetLockAsync(node.Object(), "Use", "#FALSE", CancellationToken.None)).Value).IsTypeOf<Success>();
		await writes.UpsertManagedAttributeAsync(new ManagedAttributeRecord(package, objid, "TX_VALUE", "second", "h", "2.0.0"));
		await writes.UpsertManagedAttributeAsync(new ManagedAttributeRecord(package, objid, "TX_ADDED", "added", "h", "2.0.0"));
	}

	private async Task<SharpObject> LiveAsync(DBRef dbref) =>
		(await Database.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>().Object();

	private async Task<string?> AttributeAsync(DBRef dbref, string attribute) =>
		(await Database.GetAttributeAsync(dbref, [attribute]).LastOrDefaultAsync())?.Value.ToPlainText();

	private async Task AssertOriginalAsync(DBRef dbref, string objid, string name, string package)
	{
		var live = await LiveAsync(dbref);
		await Assert.That(live.Name).IsEqualTo(name);
		await Assert.That(await live.Flags.Value.AnyAsync(f => f.Name == "DARK")).IsFalse();
		await Assert.That(live.Locks.Keys.Any(k => LockNames.Canonical(k) == "Use")).IsFalse();
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original");
		await Assert.That(await AttributeAsync(dbref, "TX_ADDED")).IsNull();
		var baselines = await Registry.GetManagedAttributesAsync(package);
		await Assert.That(baselines.Select(b => $"{b.Objid}/{b.Attribute}={b.BaselineValue}"))
			.IsEquivalentTo([$"{objid}/TX_VALUE=original"]);
	}

	[Test, NotInParallel]
	public async Task Revert_ReturnsEveryWriteToItsStateBeforeTheFirst()
	{
		const string package = "tx-revert";
		var (node, dbref, objid) = await SubjectAsync("Tx Revert Subject", package);
		var writes = await BeginAsync();

		await WriteEverythingAsync(writes, node, dbref, objid, package);
		var created = await writes.CreateAsync(
			new CreateThingCommand("Tx Revert Created", node.AsContainer, await PackageManagerAsync(), node.AsContainer), CancellationToken.None);

		var error = await writes.RevertAsync(new Error<string>("Something failed."));

		await Assert.That(error.Value).IsEqualTo("Something failed. Its changes were undone.");
		await AssertOriginalAsync(dbref, objid, "Tx Revert Subject", package);
		await Assert.That(await (await LiveAsync(created!.Object().DBRef)).Flags.Value.AnyAsync(f => f.Name == "GOING")).IsTrue();
	}

	[Test, NotInParallel]
	public async Task Dispose_WithoutCommit_RevertsWhenAnExceptionUnwinds()
	{
		const string package = "tx-dispose";
		var (node, dbref, objid) = await SubjectAsync("Tx Dispose Subject", package);

		async Task FailingOperationAsync()
		{
			await using var writes = await BeginAsync();
			await WriteEverythingAsync(writes, node, dbref, objid, package);
			throw new InvalidOperationException("mid-operation");
		}

		await Assert.That(FailingOperationAsync).Throws<InvalidOperationException>();
		await AssertOriginalAsync(dbref, objid, "Tx Dispose Subject", package);
	}

	[Test, NotInParallel]
	public async Task Commit_KeepsEveryWrite()
	{
		const string package = "tx-commit";
		var (node, dbref, objid) = await SubjectAsync("Tx Commit Subject", package);

		await using (var writes = await BeginAsync())
		{
			await WriteEverythingAsync(writes, node, dbref, objid, package);
			writes.Commit();
		}

		var live = await LiveAsync(dbref);
		await Assert.That(live.Name).IsEqualTo("Tx Renamed");
		await Assert.That(await live.Flags.Value.AnyAsync(f => f.Name == "DARK")).IsTrue();
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("second");
		await Assert.That(await AttributeAsync(dbref, "TX_ADDED")).IsEqualTo("added");
		await Assert.That((await Registry.GetManagedAttributesAsync(package)).Count).IsEqualTo(2);
	}

	[Test, NotInParallel]
	public async Task LinkingAnExitItDidNotCreate_IsRefused()
	{
		var writes = await BeginAsync();
		var pm = await PackageManagerAsync();
		AnySharpContainer home = pm;
		var room = await Database.CreateRoomAsync("Tx Link Room", pm);
		var roomNode = (await Database.GetObjectNodeAsync(room)).Expect<AnySharpObject>();
		var exitRef = await Database.CreateExitAsync("Tx Link Exit", [], roomNode.AsContainer, pm);
		var exit = (await Database.GetObjectNodeAsync(exitRef)).Expect<AnySharpObject>().Expect<SharpExit>();

		await Assert.That(() => writes.LinkCreatedExitAsync(exit, home, CancellationToken.None))
			.Throws<InvalidOperationException>();
	}
}
