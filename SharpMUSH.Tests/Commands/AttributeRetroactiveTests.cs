using DotNext.Threading;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@attribute/access/retroactive</c>. PennMUSH's <c>do_attribute_access</c>
/// (<c>src/atr_tab.c:758</c>) writes the new permissions into the attribute table and then, when
/// retroactive, walks every object and gives each one's own copy of the attribute exactly those
/// permissions and the executor as its creator (<c>src/atr_tab.c:816-826</c>). Without
/// <c>/retroactive</c> only the table changes: copies already set keep what they had.
/// </summary>
public class AttributeRetroactiveTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static string AttributeName() => $"RETRO_{Guid.NewGuid():N}"[..20].ToUpperInvariant();

	private async Task<List<string>> AsGod(string command)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = WebAppFactoryArg.Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(god).Skip(before)];
	}

	private async Task<SharpAttribute> CopyOn(DBRef obj, string name)
		=> await Mediator.CreateStream(new GetAttributeQuery(obj, [name])).LastAsync();

	private async Task<string[]> FlagsOn(DBRef obj, string name)
		=> [.. (await CopyOn(obj, name)).Flags.Select(f => f.Name.ToUpperInvariant()).Order()];

	private async Task<DBRef> ThingWith(string name, string prefix)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name} {thing}=value"));
		return thing;
	}

	[Test]
	public async ValueTask Retroactive_GivesEveryExistingCopyTheNewPermissions()
	{
		var name = AttributeName();
		var first = await ThingWith(name, "RetroA");
		var second = await ThingWith(name, "RetroB");

		var messages = await AsGod($"@attribute/access/retroactive {name}=no_command");

		await Assert.That(await FlagsOn(first, name)).IsEquivalentTo(["NO_COMMAND"]);
		await Assert.That(await FlagsOn(second, name)).IsEquivalentTo(["NO_COMMAND"]);
		await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandRetroactiveUpdatedFormat, 2, name));
	}

	/// <summary><c>AL_FLAGS(ap2) = flags</c>: the copy's permissions are replaced, not added to.</summary>
	[Test]
	public async ValueTask Retroactive_ReplacesTheCopysPermissionsRatherThanAddingToThem()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroReplace");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}/{name}=visual"));
		await Assert.That(await FlagsOn(thing, name)).Contains("VISUAL").Because("precondition");

		await AsGod($"@attribute/access/retroactive {name}=no_command");

		await Assert.That(await FlagsOn(thing, name)).IsEquivalentTo(["NO_COMMAND"]);
	}

	/// <summary><c>none</c> is no permissions at all (<c>strcasecmp(perms, "none")</c>), not a flag name.</summary>
	[Test]
	public async ValueTask Retroactive_NoneClearsEveryCopysPermissions()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroNone");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}/{name}=visual"));

		await AsGod($"@attribute/access/retroactive {name}=none");

		await Assert.That(await FlagsOn(thing, name)).IsEmpty();
	}

	/// <summary><c>if (AF_Root(ap2)) AL_FLAGS(ap2) = flags | AF_ROOT</c>: a copy with branches keeps its <c>branch</c> flag.</summary>
	[Test]
	public async ValueTask Retroactive_KeepsTheBranchFlagOfACopyWithBranches()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroBranch");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name}`LEAF {thing}=leaf"));
		await Assert.That(await FlagsOn(thing, name)).Contains("BRANCH").Because("precondition");

		await AsGod($"@attribute/access/retroactive {name}=no_command");

		await Assert.That(await FlagsOn(thing, name)).IsEquivalentTo(["BRANCH", "NO_COMMAND"]);
	}

	/// <summary><c>AL_CREATOR(ap2) = player</c>: each copy is now the executor's.</summary>
	[Test]
	public async ValueTask Retroactive_MakesTheExecutorEachCopysCreator()
	{
		var name = AttributeName();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RetroOwner");
		var created = await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@create RetroOwned"));
		var thing = DBRef.Parse(created.Message!.ToPlainText());
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&{name} {thing}=value"));
		var ownerBefore = await (await CopyOn(thing, name)).Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore!.Object.DBRef.Number).IsEqualTo(mortal.DbRef.Number).Because("precondition");

		await AsGod($"@attribute/access/retroactive {name}=no_command");

		var ownerAfter = await (await CopyOn(thing, name)).Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter!.Object.DBRef.Number).IsEqualTo(WebAppFactoryArg.ExecutorDBRef.Number);
	}

	/// <summary>Without <c>/retroactive</c> only the table changes; the copies already set keep their permissions.</summary>
	[Test]
	public async ValueTask WithoutRetroactive_ExistingCopiesKeepTheirPermissions()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroFuture");

		await AsGod($"@attribute/access {name}=no_command");

		await Assert.That(await FlagsOn(thing, name)).IsEmpty();
		await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))!.DefaultFlags).IsEquivalentTo(["NO_COMMAND"]);
	}

	/// <summary><c>@attribute/access</c> is for wizards; a mortal's retroactive request changes nothing.</summary>
	[Test]
	public async ValueTask Mortal_CannotRewriteCopies()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroMortal");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RetroMortal");

		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@attribute/access/retroactive {name}=no_command"));

		await Assert.That(await FlagsOn(thing, name)).IsEmpty();
		await Assert.That(await Mediator.Send(new GetAttributeEntryQuery(name))).IsNull();
	}

	/// <summary>
	/// The two partial exits (<c>DefinitionRegistryCommands.cs:1153-1164</c>) report
	/// <c>scanned</c>/<c>updated</c>/<c>failed</c>, and nothing pinned those counts (#1236). Both are
	/// driven against a substitute <see cref="IMediator"/>: the cancellation has to land between two
	/// specific objects and the write failure has to hit one specific object, neither of which a real
	/// world can be asked for without a wall-clock race.
	/// </summary>
	private sealed class RetroactiveHarness
	{
		public required AnySharpObject Executor { get; init; }
		public required IMediator Mediator { get; init; }
		public required SharpAttributeFlag Flag { get; init; }
		public required string Name { get; init; }
		public required List<DBRef> FlagWrites { get; init; }
		public required List<DBRef> OwnerWrites { get; init; }
	}

	/// <summary>
	/// A wizard executor, <paramref name="objects"/> as the whole world, and each of them carrying its
	/// own unflagged copy of the attribute. The stream of objects is supplied by the caller so it can
	/// decide what happens between two of them.
	/// </summary>
	private RetroactiveHarness Harness(IAsyncEnumerable<SharpObject> objects, params DBRef[] withCopies)
	{
		var name = AttributeName();
		var factory = new TestObjectFactory();
		var executor = factory.CreatePlayer(7100 + Random.Shared.Next(100_000), "RetroWiz");
		executor.Object().Flags = new(() => new[]
		{
			new SharpObjectFlag
			{
				Name = "WIZARD",
				Symbol = "W",
				SetPermissions = [],
				UnsetPermissions = [],
				TypeRestrictions = ["PLAYER"],
				System = true
			}
		}.ToAsyncEnumerable());

		var flag = new SharpAttributeFlag { Name = "NO_COMMAND", Symbol = "$", System = true, Inheritable = false };
		var entry = new SharpAttributeEntry { Name = name, DefaultFlags = ["NO_COMMAND"] };
		var copies = withCopies.ToDictionary(
			dbref => dbref,
			dbref => new SharpAttribute(
				Id: $"attr-{dbref.Number}",
				Key: name,
				Name: name,
				Flags: [],
				CommandListIndex: null,
				LongName: name,
				Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
				Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
				SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(entry))));

		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new AnyOptionalSharpObject(executor.Expect<SharpPlayer>()));
		mediator.CreateStream(Arg.Any<GetAttributeFlagsQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => new[] { flag }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<GetAttributeEntryQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpAttributeEntry?>(entry));
		mediator.Send(Arg.Any<CreateAttributeEntryCommand>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpAttributeEntry?>(entry));
		mediator.CreateStream(Arg.Any<GetAllObjectsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => objects);
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => copies.TryGetValue(call.Arg<GetAttributeQuery>().DBRef, out var copy)
				? new[] { copy }.ToAsyncEnumerable()
				: AsyncEnumerable.Empty<SharpAttribute>());

		var flagWrites = new List<DBRef>();
		var ownerWrites = new List<DBRef>();
		mediator.Send(Arg.Any<SetAttributeFlagCommand>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				flagWrites.Add(call.Arg<SetAttributeFlagCommand>().DBRef);
				return new ValueTask<bool>(true);
			});

		// The owner write is the last thing a copy gets and the line `updated++` sits behind, so an
		// object counted as updated must have had one. A test that overrides this stub has to keep
		// recording, or it stops answering for the objects it did not break.
		mediator.Send(Arg.Any<SetAttributeOwnerCommand>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				ownerWrites.Add(call.Arg<SetAttributeOwnerCommand>().DBRef);
				return new ValueTask<bool>(true);
			});

		return new RetroactiveHarness
		{
			Executor = executor,
			Mediator = mediator,
			Flag = flag,
			Name = name,
			FlagWrites = flagWrites,
			OwnerWrites = ownerWrites
		};
	}

	private async Task<List<string>> RunRetroactive(RetroactiveHarness harness, CancellationTokenSource cancellation)
	{
		var state = ParserState.RootFor(harness.Executor.Object().DBRef) with
		{
			Switches = ["ACCESS", "RETROACTIVE"],
			Arguments = new() { ["0"] = new CallState(harness.Name), ["1"] = new CallState("no_command") }
		};
		await state.KnownExecutorObject(harness.Mediator);

		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);

		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, harness.Mediator);

		var before = WebAppFactoryArg.Notifications.CountFor(harness.Executor.Object().DBRef);

		// MUSHCodeParser reuses an already-entered budget rather than minting its own, so entering one
		// here is what puts the pass's deadline under the test's control. Infinite time, cancelled by
		// hand: a short wall-clock budget would be a race, not a test.
		using var budget = new ExecutionBudget(System.Threading.Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		await commands.Attribute(parser, new SharpCommandAttribute { Name = "@ATTRIBUTE" });

		return [.. WebAppFactoryArg.Notifications.For(harness.Executor.Object().DBRef).Skip(before)];
	}

	/// <summary>
	/// The <c>catch (OperationCanceledException)</c> exit (<c>:1153-1157</c>): the budget is cancelled
	/// part-way through the world, and the report names how far the pass got instead of claiming every
	/// copy was reached. What it did reach keeps its new permissions.
	/// </summary>
	[Test]
	public async Task Retroactive_CancelledMidPass_ReportsWhatItReached()
	{
		var factory = new TestObjectFactory();
		var first = factory.CreatePlayer(7201, "RetroCancelA").Object();
		var second = factory.CreatePlayer(7202, "RetroCancelB").Object();
		var third = factory.CreatePlayer(7203, "RetroCancelC").Object();
		using var cancellation = new CancellationTokenSource();

		async IAsyncEnumerable<SharpObject> World([EnumeratorCancellation] CancellationToken ct = default)
		{
			yield return first;
			yield return second;
			await cancellation.CancelAsync();
			ct.ThrowIfCancellationRequested();
			yield return third;
		}

		var harness = Harness(World(), first.DBRef, second.DBRef, third.DBRef);

		var messages = await RunRetroactive(harness, cancellation);

		await Assert.That(messages).Contains(string.Format(
				ErrorMessages.Notifications.AttributeCommandRetroactivePartialFormat, 2, 2, harness.Name, 0))
			.Because("the partial report has to name what the pass actually reached");
		await Assert.That(harness.FlagWrites).IsEquivalentTo([first.DBRef, second.DBRef])
			.Because("the copies the pass did reach keep their new permissions, and the one past the cancellation is untouched");
		await Assert.That(harness.OwnerWrites).IsEquivalentTo([first.DBRef, second.DBRef])
			.Because("`updated` counts objects that got the executor as creator, which is the write after the flags");
	}

	/// <summary>
	/// The <c>failed &gt; 0</c> exit (<c>:1159-1164</c>): one object's write throws, is counted and
	/// logged (<c>:1145-1150</c>), and the pass carries on - so the report is the partial one even
	/// though the whole world was scanned.
	/// </summary>
	[Test]
	public async Task Retroactive_OneObjectThatCannotBeWritten_IsCountedAndTheRestStillChange()
	{
		var factory = new TestObjectFactory();
		var broken = factory.CreatePlayer(7211, "RetroFailA").Object();
		var intact = factory.CreatePlayer(7212, "RetroFailB").Object();
		using var cancellation = new CancellationTokenSource();

		var harness = Harness(new[] { broken, intact }.ToAsyncEnumerable(), broken.DBRef, intact.DBRef);
		harness.Mediator.Send(Arg.Any<SetAttributeOwnerCommand>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var dbref = call.Arg<SetAttributeOwnerCommand>().DBRef;
				if (dbref == broken.DBRef)
				{
					throw new InvalidOperationException("the store refused this one");
				}

				harness.OwnerWrites.Add(dbref);
				return new ValueTask<bool>(true);
			});

		var messages = await RunRetroactive(harness, cancellation);

		await Assert.That(messages).Contains(string.Format(
				ErrorMessages.Notifications.AttributeCommandRetroactivePartialFormat, 2, 1, harness.Name, 1))
			.Because("a write that threw is reported as one that could not be updated, not swallowed");
		await Assert.That(harness.FlagWrites).Contains(intact.DBRef)
			.Because("a failure on one object must not stop the rest of the pass");
		await Assert.That(harness.OwnerWrites).IsEquivalentTo([intact.DBRef])
			.Because("only the object that was actually updated got the executor as its creator");
	}
}
