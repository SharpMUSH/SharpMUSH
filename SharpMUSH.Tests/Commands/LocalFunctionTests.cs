using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Attributes;
using Mediator;
using SharpMUSH.Library.Definitions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class LocalFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private Task Cmd(string command) => Factory.CommandParser.CommandParse(1,
		Factory.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command)).AsTask();
	private async Task<string> Eval(string expression) =>
		(await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private sealed class RegistryOverride(IServiceProvider services, IUserDefinedFunctionService registry) : IServiceProvider
	{
		public object? GetService(Type type) => type == typeof(IUserDefinedFunctionService) ? registry : services.GetService(type);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LegacyScopedLookupReturnsTheNormalNotFoundError(bool supportsScopedGet)
	{
		var registry = SharpMUSH.Tests.Services.UserFunctionRegistryCompatibilityTests.CreateLegacyRegistry(supportsScopedGet);
		var parser = (MUSHCodeParser)Factory.FunctionParser with { ServiceProvider = new RegistryOverride(Factory.Services, registry) };
		var result = await parser.FunctionParse(MarkupText.Plain("localfun(unavailable)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(string.Format(ErrorMessages.Returns.NoSuchFunction, "UNAVAILABLE"));
	}

	[Test]
	public async Task LegacyRegistryCannotTurnLocalCreationIntoGlobalDefinition()
	{
		var name = "legacy" + Guid.NewGuid().ToString("N");
		await Cmd($"&{name} me=local");
		var registry = SharpMUSH.Tests.Services.UserFunctionRegistryCompatibilityTests.CreateLegacyRegistry();
		var parser = (MUSHCodeParser)Factory.CommandParser with { ServiceProvider = new RegistryOverride(Factory.Services, registry) };
		try { await parser.CommandParse(1, Connections, MarkupText.Plain($"@function/local {name}=me,{name}")); }
		catch (NotSupportedException) { }
		await Assert.That((int)registry.GetType().GetField("DefinitionCalls")!.GetValue(registry)!).IsEqualTo(0);
	}

	[Test]
	public async Task LocalRegistrationRequiresExplicitCallAndLeavesGlobalLookupUnchanged()
	{
		var name = "local" + Guid.NewGuid().ToString("N");
		await Cmd($"&{name} me=local-%0");
		await Cmd($"@function/local {name}=me,{name}");
		await Assert.That(await Eval($"localfun({name},value)")).IsEqualTo("local-value");
		await Assert.That(await Eval($"[{name}(value)]")).Contains("NOT FOUND");
		await Assert.That(await Eval("add(2,3)")).IsEqualTo("5");
	}
	[Test]
	public async Task LocalAliasesEnableBoundsAndBuiltinNamesPreserveGlobalBehavior()
	{
		var name = "operations" + Guid.NewGuid().ToString("N");
		await Cmd($"&{name} me=local-%0");
		await Cmd($"&{name}global me=global");
		await Cmd($"@function {name}=me,{name}global");
		await Cmd($"@function/local {name}=me,{name},1,1");
		await Assert.That(await Eval($"{name}()")).IsEqualTo("global");
		await Assert.That(await Eval($"localfun({name},argument)")).IsEqualTo("local-argument");
		await Assert.That(await Eval($"localfun({name})")).Contains("EXPECTS AT LEAST");
		await Cmd($"@function/local/alias {name}alias={name}");
		await Cmd($"@function/local/disable {name}");
		await Assert.That(await Eval($"localfun({name}alias,argument)")).Contains("NOT FOUND");
		await Cmd($"@function/local/enable {name}");
		await Assert.That(await Eval($"localfun({name}alias,argument)")).IsEqualTo("local-argument");
		await Cmd($"@function/local add=me,{name}");
		await Assert.That(await Eval("add(2,3)")).IsEqualTo("5");
		await Assert.That(await Eval("localfun(add,2,3)")).Contains("NOT FOUND");
		await Cmd($"@function/local/delete {name}");
		await Assert.That(await Eval($"localfun({name}alias,argument)")).Contains("NOT FOUND");
	}

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private async Task<string> EvalAs(DBRef executor, string expression) =>
		(await Factory.FunctionParser.FromState(ParserState.RootFor(executor)).FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	[Test]
	public async Task OwnersAndReownedExecutorsResolveOnlyTheirOwnDefinitions()
	{
		var first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "LocalOwnerA");
		var second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "LocalOwnerB");
		var name = "scoped" + Guid.NewGuid().ToString("N");
		foreach (var player in new[] { first, second })
		{
			var parser = Factory.CommandParserFor(player.DbRef, player.Handle);
			await parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"&CODE me={player.DbRef}"));
			await parser.CommandParse(player.Handle, Connections, MarkupText.Plain($"@function/local {name}=me,CODE"));
		}
		await Assert.That(await EvalAs(first.DbRef, $"localfun({name})")).IsEqualTo(first.DbRef.ToString());
		await Assert.That(await EvalAs(second.DbRef, $"localfun({name})")).IsEqualTo(second.DbRef.ToString());
		await Assert.That(await EvalAs(first.DbRef, $"fn(localfun,{name})")).IsEqualTo(first.DbRef.ToString());
		await Assert.That(await Eval($"localfun({name})")).Contains("NOT FOUND");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LocalExecutor");
		var obj = (await Mediator.Send(new GetObjectNodeQuery(thing))).Known;
		var firstPlayer = (await Mediator.Send(new GetObjectNodeQuery(first.DbRef))).AsPlayer;
		var secondPlayer = (await Mediator.Send(new GetObjectNodeQuery(second.DbRef))).AsPlayer;
		await Mediator.Send(new SetObjectOwnerCommand(obj, firstPlayer));
		await Assert.That(await EvalAs(thing, $"localfun({name})")).Contains("NO PERMISSION TO GET ATTRIBUTE");
		await Cmd($"@set {first.DbRef}/CODE=VISUAL");
		await Assert.That(await EvalAs(thing, $"localfun({name})")).IsEqualTo(first.DbRef.ToString());
		await Mediator.Send(new SetObjectOwnerCommand(obj, secondPlayer));
		await Assert.That(await EvalAs(thing, $"localfun({name})")).Contains("NO PERMISSION TO GET ATTRIBUTE");
		await Cmd($"@set {second.DbRef}/CODE=VISUAL");
		await Assert.That(await EvalAs(thing, $"localfun({name})")).IsEqualTo(second.DbRef.ToString());
	}

	[Test]
	public async Task DeletedAndReownedBackingObjectsInvalidateLocalAliases()
	{
		var registry = Factory.Services.GetRequiredService<IUserDefinedFunctionService>();
		var god = (await Mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known;
		var owner = (await god.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		foreach (var delete in new[] { true, false })
		{
			var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LocalTarget");
			var name = "backing" + Guid.NewGuid().ToString("N");
			await Cmd($"&CODE {thing}=value");
			await Cmd($"@function/local {name}={thing},CODE");
			await Cmd($"@function/local/alias {name}alias={name}");
			await Assert.That(await Eval($"localfun({name}alias)")).IsEqualTo("value");
			if (delete) await Mediator.Send(new DeleteObjectCommand(thing));
			else
			{
				var newOwner = await Mediator.Send(new CreatePlayerCommand("LocalNewOwner" + Guid.NewGuid().ToString("N"), "password", new DBRef(0), new DBRef(0), 100));
				await Mediator.Send(new SetObjectOwnerCommand((await Mediator.Send(new GetObjectNodeQuery(thing))).Known,
					(await Mediator.Send(new GetObjectNodeQuery(newOwner))).AsPlayer));
			}
			await Assert.That(registry.Get(name, owner)).IsNull();
			await Assert.That(registry.Get(name + "alias", owner)).IsNull();
			await Assert.That(await Eval($"localfun({name}alias)")).Contains("NOT FOUND");
		}
	}

	private sealed class RegistryProvider(IServiceProvider services, IUserDefinedFunctionService registry) : IServiceProvider
	{
		public object? GetService(Type type) => type == typeof(IUserDefinedFunctionService) ? registry : services.GetService(type);
	}

	[Test]
	public async Task StoredStartupRestoresIntoFreshRegistryAndAttributeRegisterSemanticsMatchU()
	{
		var name = "startup" + Guid.NewGuid().ToString("N");
		var startupObject = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LocalStartup");
		await Cmd($"&{name} {startupObject}=setq(A,changed)");
		await Cmd($"&STARTUP {startupObject}=@function/local {name}=me,{name}");
		var registry = new UserDefinedFunctionService();
		var provider = new RegistryProvider(Factory.Services, registry);
		var parser = new MUSHCodeParser(provider.GetRequiredService<ILogger<MUSHCodeParser>>(),
			provider.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			provider.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			provider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(), provider, ParserState.RootFor(Factory.ExecutorDBRef));
		var god = (await Mediator.Send(new GetObjectNodeQuery(Factory.ExecutorDBRef))).Known;
		var owner = (await god.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		await Assert.That(registry.Get(name, owner)).IsNull();
		await StartupAttributeRunner.RunObjectAttributeAsync(parser, provider.GetRequiredService<IAttributeService>(), (await Mediator.Send(new GetObjectNodeQuery(startupObject))).Known, "STARTUP", god);
		await Assert.That(registry.Get(name, owner)).IsNotNull();
		var local = (await parser.FunctionParse(MarkupText.Plain($"strcat(setq(A,before),localfun({name}),r(A))")))!.Message!.ToPlainText();
		var ordinary = await Eval($"strcat(setq(A,before),u({startupObject}/{name}),r(A))");
		await Assert.That(local).IsEqualTo(ordinary);
	}

	[Test]
	public async Task TemporarilyDeletedBuiltinsRemainReservedForLocals()
	{
		var name = "reserved" + Guid.NewGuid().ToString("N");
		await Cmd($"&{name} me=local");
		await Cmd($"@function/local {name}=me,{name}");
		await Cmd("@function/delete add");
		try
		{
			await Cmd($"@function/local add=me,{name}");
			await Assert.That(await Eval("localfun(add)")).Contains("NOT FOUND");
			await Cmd($"@function/local/alias add={name}");
			await Assert.That(await Eval("localfun(add)")).Contains("NOT FOUND");
		}
		finally { await Cmd("@function/builtin add"); await Cmd("@function/local/delete add"); }
	}

	[Test]
	public async Task AssigningExistingOwnerPreservesLocalDefinitions()
	{
		var name = "sameowner" + Guid.NewGuid().ToString("N");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LocalSameOwner");
		await Cmd($"&CODE {thing}=unchanged");
		await Cmd($"@function/local {name}={thing},CODE");
		var obj = (await Mediator.Send(new GetObjectNodeQuery(thing))).Known;
		await Mediator.Send(new SetObjectOwnerCommand(obj, await obj.Object().Owner.WithCancellation(CancellationToken.None)));
		await Assert.That(await Eval($"localfun({name})")).IsEqualTo("unchanged");
	}

	private sealed class LocalNamePlugin(string name) : PluginBase
	{
		public override string Id => name;
		public override IEnumerable<CommandDefinition> GetCommands() => [];
		public override IEnumerable<FunctionDefinition> GetFunctions() =>
			[new(new SharpFunctionAttribute { Name = name, Flags = FunctionFlags.Regular }, _ => ValueTask.FromResult(new CallState("plugin")))];
	}

	[Test]
	public async Task PluginClaimPreventsAnExistingLocalFromResolving()
	{
		var name = "pluginclaim" + Guid.NewGuid().ToString("N");
		await Cmd($"&{name} me=local");
		await Cmd($"@function/local {name}=me,{name}");
		await Assert.That(await Eval($"localfun({name})")).IsEqualTo("local");
		await Cmd($"@function/local/alias {name}alias={name}");
		var manager = (SharpMUSH.Implementation.Services.PluginManager)Factory.Services.GetRequiredService<IPluginManager>();
		manager.RegisterPlugin(new LocalNamePlugin(name));
		try
		{
			await Assert.That(await Eval($"{name}()")).IsEqualTo("plugin");
			await Assert.That(await Eval($"localfun({name})")).Contains("NOT FOUND");
			await Assert.That(await Eval($"localfun({name}alias)")).Contains("NOT FOUND");
			var actor = await Factory.FunctionParser.CurrentState.KnownExecutorObject(Mediator);
			var owner = (await actor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
			var registry = Factory.Services.GetRequiredService<IUserDefinedFunctionService>();
			await Assert.That(registry.Get(name, owner)).IsNull();
			await Assert.That(registry.Get(name + "alias", owner)).IsNull();
		}
		finally { await manager.UnloadAsync(name); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LiveBuiltinCloneBlocksAnExistingLocalAndItsAlias(bool cloneAlias)
	{
		var name = "cloneclaim" + Guid.NewGuid().ToString("N");
		var alias = name + "alias";
		var clone = cloneAlias ? alias : name;
		await Cmd($"&{name} me=local");
		await Cmd($"@function/local {name}=me,{name}");
		await Cmd($"@function/local/alias {alias}={name}");
		await Assert.That(await Eval($"localfun({alias})")).IsEqualTo("local");
		await Cmd($"@function/clone {clone}=add");
		try
		{
			await Assert.That(await Eval($"{clone}(2,3)")).IsEqualTo("5");
			await Assert.That(await Eval($"localfun({clone})")).Contains("NOT FOUND");
			if (!cloneAlias) await Assert.That(await Eval($"localfun({alias})")).Contains("NOT FOUND");
		}
		finally { await Cmd($"@function/delete {clone}"); await Cmd($"@function/local/delete {name}"); }
	}

}
