using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// A cached object node does not outlive the write that stamps its modification time: the node read
/// before an attribute or lock write is not what a read after it returns.
/// </summary>
public class ModificationTimeCacheTests
{
	/// <summary>Far older than any stamp the host's clock can give.</summary>
	private const long Old = 1_000;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<(SharpPlayer God, DBRef Thing)> OldThingAsync(string name)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var room = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();
		var thing = await Mediator.Send(new CreateThingCommand(
			TestIsolationHelpers.GenerateUniqueName(name), room, god, room, ModifiedTime: Old));
		return (god, thing);
	}

	private async Task<long> ModifiedAsync(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>().Object().ModifiedTime;

	[Test]
	public async Task AnAttributeWriteReachesACachedNode()
	{
		var (god, thing) = await OldThingAsync("MtimeCacheAttr");
		await Assert.That(await ModifiedAsync(thing)).IsEqualTo(Old);

		await Mediator.Send(new SetAttributeCommand(thing, ["COLOUR"], MarkupText.Plain("red"), god));

		await Assert.That(await ModifiedAsync(thing)).IsGreaterThan(Old);
	}

	[Test]
	public async Task ALockWriteReachesACachedNode()
	{
		var (god, thing) = await OldThingAsync("MtimeCacheLock");
		var node = (await Mediator.Send(new GetObjectNodeQuery(thing))).Expect<AnySharpObject>();
		await Assert.That(node.Object().ModifiedTime).IsEqualTo(Old);

		await WebAppFactoryArg.Services.GetRequiredService<ILockService>().SetAsync(god, node, "Basic", "#1");

		await Assert.That(await ModifiedAsync(thing)).IsGreaterThan(Old);
	}
}

/// <summary>
/// <c>clone()</c> gives a cloned thing its original's modification time even though copying the attributes
/// writes them (<c>create.c:653-655</c>), and a cloned exit the time of its own opening (<c>do_real_open</c>).
/// </summary>
public class CloneModificationTimeTests
{
	/// <summary>Far older than any stamp the host's clock can give.</summary>
	private const long Old = 1_000;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Eval(string code)
		=> (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(code))).ToPlainText();

	[Test]
	public async Task AClonedThingKeepsItsOriginalsTime()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var room = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();
		var thing = await Mediator.Send(new CreateThingCommand(
			TestIsolationHelpers.GenerateUniqueName("MtimeCloneThing"), room, god, room, ModifiedTime: Old));
		// The importer's write, which leaves the time alone, so the original has something to copy.
		await Mediator.Send(new SetAttributesCommand(thing, [new AttributeWrite(["COLOUR"], MarkupText.Plain("red"), god, [])]));

		await Assert.That(await Eval($"[msecs(clone(#{thing.Number}),ms)]")).IsEqualTo(Old.ToString());
	}

	[Test]
	public async Task AClonedExitTakesItsOwnTime()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var room = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();
		var exit = await Mediator.Send(new CreateExitCommand(
			TestIsolationHelpers.GenerateUniqueName("MtimeCloneExit"), [], room, god, ModifiedTime: Old));

		await Assert.That(await Eval($"[gt(msecs(clone(#{exit.Number}),ms),{Old})]")).IsEqualTo("1");
	}
}
