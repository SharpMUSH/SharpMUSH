using NSubstitute;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.CodeAnalysis;

public class LiveAnalysisCatalogTests
{
	internal static SharpPlayer Actor(int key)
	{
		var actor = ActorRaw(key);
		actor.Object.Owner = new(_ => Task.FromResult(actor));
		return actor;
	}

	private static SharpPlayer ActorRaw(int key) => new()
	{
		Object = new SharpObject
		{
			Key = key, CreationTime = 100, Name = "fixture", Type = "Player", Locks = SharpObject.EmptyLocks,
			Owner = new(_ => Task.FromResult<SharpPlayer>(null!)),
			Grants = new(_ => Task.FromResult(SharpMUSH.Library.Authorization.ObjectGrants.None)),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
			Zone = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>())
		},
		Quota = 20, Aliases = [], PasswordHash = string.Empty, PasswordSalt = null,
		Location = new(_ => Task.FromResult<AnySharpContainer>(null!)),
		Home = new(_ => Task.FromResult<AnySharpContainer>(null!))
	};

	[Test]
	public async Task CompiledAliases_ExcludeCollidingGlobalNamesBeforeProjection()
	{
		var functions = new FunctionLibraryService();
		var definition = new FunctionDefinition(new SharpFunctionAttribute { Name = "public", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 1 }, _ => throw new NotSupportedException());
		functions.Add("public", (definition, true)); functions.Add("alias", (definition, true));
		var users = new UserDefinedFunctionService();
		users.Define(new UserDefinedFunction("ALIAS", new DBRef(9, 100), "FN", 1, 2, true, null));
		var snapshot = await new LiveAnalysisCatalog(functions, new CommandLibraryService(), users, Substitute.For<ILockService>()).GetSnapshotAsync(Actor(2));
		await Assert.That(snapshot.Entries.Count).IsEqualTo(1);
		await Assert.That(snapshot.Entries[0].Aliases.Contains("alias")).IsTrue();
		var analyzer = AnalysisCatalogAnalyzer.Create(snapshot, SharpMUSH.Configuration.Options.SharpMUSHOptions.Default());
		await Assert.That(analyzer.Hover("alias(1)", 0, 2)).IsNotNull();
	}

	[Test]
	public async Task LocalScope_UsesRuntimeOwnerInsteadOfExecutorObjectIdentity()
	{
		var owner = Actor(2);
		var executor = Actor(9);
		executor.Object.Owner = new(_ => Task.FromResult(owner));
		var users = new UserDefinedFunctionService();
		users.DefineLocal(new UserDefinedFunction("ours", new DBRef(10, 100), "FN", 1, 2, true, null) { Owner = new DBRef(2, 100) });
		users.DefineLocal(new UserDefinedFunction("wrongscope", new DBRef(10, 100), "FN", 1, 2, true, null) { Owner = new DBRef(9, 100) });
		var catalog = new LiveAnalysisCatalog(new FunctionLibraryService(), new CommandLibraryService(), users, Substitute.For<ILockService>());
		var snapshot = await catalog.GetSnapshotAsync(executor);
		await Assert.That(snapshot.Entries.Select(entry => entry.Name).SequenceEqual(new[] { "ours" })).IsTrue();
	}

	[Test]
	public async Task Visibility_UsesRuntimePermissionGatesAndDoesNotLeakOtherLocalScopes()
	{
		var functions = new FunctionLibraryService();
		var commands = new CommandLibraryService();
		functions.Add("public", (new FunctionDefinition(new SharpFunctionAttribute { Name = "public", Flags = FunctionFlags.Regular }, _ => throw new NotSupportedException()), true));
		functions.Add("god", (new FunctionDefinition(new SharpFunctionAttribute { Name = "god", Flags = FunctionFlags.GodOnly }, _ => throw new NotSupportedException()), true));
		commands.Add("@god", (new CommandDefinition(new SharpCommandAttribute { Name = "@god", Behavior = CommandBehavior.God }, _ => throw new NotSupportedException()), true));
		var users = new UserDefinedFunctionService();
		users.Define(new UserDefinedFunction("global", new DBRef(9, 100), "FN", 1, 2, true, null));
		users.DefineLocal(new UserDefinedFunction("mine", new DBRef(9, 100), "FN", 1, 2, true, null) { Owner = new DBRef(2, 100) });
		users.DefineLocal(new UserDefinedFunction("secret", new DBRef(9, 100), "FN", 1, 2, true, null) { Owner = new DBRef(3, 100) });
		users.Define(new UserDefinedFunction("hidden", new DBRef(9, 100), "FN", 1, 2, true, null, "nobody"));
		var catalog = new LiveAnalysisCatalog(functions, commands, users, Substitute.For<ILockService>());
		var ordinary = await catalog.GetSnapshotAsync(Actor(2));
		await Assert.That(ordinary.Entries.Select(entry => entry.Name).Order().SequenceEqual(new[] { "global", "mine", "public" })).IsTrue();
		await Assert.That(ordinary.Entries.Single(entry => entry.Name == "mine").Origin).IsEqualTo("owner-local");
		var god = await catalog.GetSnapshotAsync(Actor(1));
		await Assert.That(god.Entries.Any(entry => entry.Name == "god")).IsTrue();
		await Assert.That(god.Entries.Any(entry => entry.Name == "@god")).IsTrue();
		await Assert.That(god.Entries.Any(entry => entry.Name == "mine")).IsFalse();
	}

	[Test]
	public async Task MetadataMutation_ChangesRevisionWhileOldSnapshotStaysDetached()
	{
		var functions = new FunctionLibraryService();
		var commands = new CommandLibraryService();
		var attribute = new SharpCommandAttribute { Name = "@public", ParameterNames = ["old"] };
		commands.Add("@public", (new CommandDefinition(attribute, _ => throw new NotSupportedException()), true));
		var catalog = new LiveAnalysisCatalog(functions, commands, new UserDefinedFunctionService(), Substitute.For<ILockService>());
		var first = await catalog.GetSnapshotAsync(Actor(2));
		var version = commands.Version;
		commands.MutateMetadata(() => attribute.ParameterNames = ["new"]);
		var second = await catalog.GetSnapshotAsync(Actor(2));
		await Assert.That(commands.Version > version).IsTrue();
		await Assert.That(first.Revision == second.Revision).IsFalse();
		await Assert.That(first.Entries.Single().ParameterNames.Single()).IsEqualTo("old");
		await Assert.That(second.Entries.Single().ParameterNames.Single()).IsEqualTo("new");
	}

	[Test]
	public async Task Snapshot_BlocksMetadataMutationUntilProjectionIsDetached()
	{
		var library = new CommandLibraryService();
		var attribute = new SharpCommandAttribute { Name = "@test" };
		library.Add("@test", (new CommandDefinition(attribute, _ => throw new NotSupportedException()), true));
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var snapshot = Task.Run(() => library.Capture(entries =>
		{
			entered.Set();
			if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
			return entries.Single().Value.LibraryInformation.Attribute.CommandLock;
		}));
		if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
		using var attempted = new ManualResetEventSlim();
		var mutation = Task.Run(() => { attempted.Set(); library.MutateMetadata(() => attribute.CommandLock = "new"); });
		if (!attempted.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
		var finishedEarly = mutation.Wait(TimeSpan.FromMilliseconds(100));
		release.Set();
		await Assert.That(finishedEarly).IsFalse();
		await Assert.That(await snapshot.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(string.Empty);
		await mutation.WaitAsync(TimeSpan.FromSeconds(5));
	}
	[Test]
	public async Task FunctionAliasVisibility_UsesEachInvokedNamesRestrictionOverlay()
	{
		var functions = new FunctionLibraryService();
		var attribute = new SharpFunctionAttribute { Name = "public", Flags = FunctionFlags.Regular };
		var definition = new FunctionDefinition(attribute, _ => throw new NotSupportedException());
		functions.Add("public", (definition, true));
		functions.Add("alias", (definition, true));
		var users = new UserDefinedFunctionService();
		users.SetBuiltinRestriction("alias", "nobody");
		var catalog = new LiveAnalysisCatalog(functions, new CommandLibraryService(), users, Substitute.For<ILockService>());
		var first = await catalog.GetSnapshotAsync(Actor(2));
		await Assert.That(first.Entries.Single().Aliases.Count).IsEqualTo(0);
		users.SetBuiltinRestriction("alias", null);
		users.SetBuiltinRestriction("public", "nobody");
		var second = await catalog.GetSnapshotAsync(Actor(2));
		await Assert.That(second.Entries.Single().Name).IsEqualTo("alias");
		await Assert.That(second.Entries.Single().Aliases.Count).IsEqualTo(0);
	}

	[Test]
	public async Task CommandLocks_UseTheActualExecutorAndCancelledCaptureDoesNoPermissionWork()
	{
		var commands = new CommandLibraryService();
		commands.Add("@locked", (new CommandDefinition(new SharpCommandAttribute { Name = "@locked", CommandLock = "fixture-lock" }, _ => throw new NotSupportedException()), true));
		var locks = Substitute.For<ILockService>();
		locks.Evaluate("fixture-lock", Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(call => call.ArgAt<AnySharpObject>(2).Object().Key == 2);
		var catalog = new LiveAnalysisCatalog(new FunctionLibraryService(), commands, new UserDefinedFunctionService(), locks);
		await Assert.That((await catalog.GetSnapshotAsync(Actor(2))).Entries.Count).IsEqualTo(1);
		await Assert.That((await catalog.GetSnapshotAsync(Actor(3))).Entries.Count).IsEqualTo(0);
		locks.ClearReceivedCalls();
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		await Assert.That(async () => await catalog.GetSnapshotAsync(Actor(2), cancelled.Token)).Throws<OperationCanceledException>();
		await Assert.That(locks.ReceivedCalls().Any()).IsFalse();
	}

}
