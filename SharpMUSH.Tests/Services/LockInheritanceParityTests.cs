using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class LockInheritanceParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task ParentLockIsInheritedUntilPrivateOrOverridden()
	{
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		options.CurrentValue.Returns(ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"));
		var service = new LockService(Substitute.For<IBooleanExpressionParser>(), options, Substitute.For<IMediator>(), new Lazy<IPermissionService>(() => Substitute.For<IPermissionService>()));
		var factory = new TestObjectFactory();
		var parent = factory.CreatePlayer(100, "Parent");
		var child = factory.CreatePlayer(101, "Child");
		child.Object().Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(parent));
		parent.Object().WithLock("Basic", new SharpLockData("#FALSE"));
		await Assert.That((await service.LookupAsync(child, "basic")).Expect<ResolvedLock>().Source).IsEqualTo(parent);
		parent.Object().WithLock("Basic", new SharpLockData("#FALSE", LockService.LockFlags.Private));
		await Assert.That(await service.LookupAsync(child, "Basic") is NotFound).IsTrue();
		child.Object().WithLock("Basic", new SharpLockData("#TRUE"));
		await Assert.That((await service.LookupAsync(child, "Basic")).Expect<ResolvedLock>().Data.LockString).IsEqualTo("#TRUE");
	}

	[Test]
	public async Task CyclicParentsTerminate()
	{
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		options.CurrentValue.Returns(ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"));
		var service = new LockService(Substitute.For<IBooleanExpressionParser>(), options, Substitute.For<IMediator>(), new Lazy<IPermissionService>(() => Substitute.For<IPermissionService>()));
		var player = new TestObjectFactory().CreatePlayer(100, "Cycle");
		player.Object().Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(player));
		await Assert.That(await service.LookupAsync(player, "Basic") is NotFound).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ConfiguredAncestorAndItsParentSupplyInheritedLock(bool fromParent)
	{
		var factory = new TestObjectFactory();
		var child = factory.CreatePlayer(100, "Child");
		var ancestor = factory.CreatePlayer(101, "Ancestor");
		var source = fromParent ? factory.CreatePlayer(102, "AncestorParent") : ancestor;
		if (fromParent) ancestor.Object().Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(source));
		source.Object().WithLock("Basic", new SharpLockData("#FALSE"));
		var service = AncestorService(ancestor);
		await Assert.That((await service.LookupAsync(child, "Basic")).Expect<ResolvedLock>().Source).IsEqualTo(source);
		source.Object().WithLock("Basic", new SharpLockData("#FALSE", LockService.LockFlags.Private));
		await Assert.That(await service.LookupAsync(child, "Basic") is NotFound).IsTrue();
	}

	[Test]
	public async Task OrphanSuppressesAncestorButStillUsesExplicitParent()
	{
		var factory = new TestObjectFactory();
		var child = factory.CreatePlayer(100, "Orphan");
		var ancestor = factory.CreatePlayer(101, "Ancestor");
		ancestor.Object().WithLock("Basic", new SharpLockData("#FALSE"));
		child.Object().Flags = new(() => new[] { new SharpObjectFlag
		{
			Name = "ORPHAN", Symbol = "O", SetPermissions = [], UnsetPermissions = [], System = true, TypeRestrictions = []
		} }.ToAsyncEnumerable());
		var service = AncestorService(ancestor);
		await Assert.That(await service.LookupAsync(child, "Basic") is NotFound).IsTrue();
		var parent = factory.CreatePlayer(102, "ExplicitParent");
		parent.Object().WithLock("Basic", new SharpLockData("#TRUE"));
		child.Object().Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(parent));
		await Assert.That((await service.LookupAsync(child, "Basic")).Expect<ResolvedLock>().Source).IsEqualTo(parent);
	}

	[Test]
	[Arguments(100, true)]
	[Arguments(101, false)]
	public async Task ParentTraversalHonorsPennDepthBoundary(int hops, bool found)
	{
		var factory = new TestObjectFactory();
		var child = factory.CreatePlayer(100, "Child");
		var current = child;
		for (var i = 1; i <= hops; i++)
		{
			var parent = factory.CreatePlayer(100 + i, $"Parent{i}");
			current.Object().Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(parent));
			current = parent;
		}
		current.Object().WithLock("Basic", new SharpLockData("#FALSE"));
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		options.CurrentValue.Returns(ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"));
		var service = new LockService(Substitute.For<IBooleanExpressionParser>(), options, Substitute.For<IMediator>(),
			new Lazy<IPermissionService>(() => Substitute.For<IPermissionService>()));
		await Assert.That(await service.LookupAsync(child, "Basic") is ResolvedLock).IsEqualTo(found);
	}

	[Test]
	public async Task ParentMutationsImmediatelyChangeInheritedReadbackAndEvaluation()
	{
		var parent = await RunAsync($"@create InheritParent{Guid.NewGuid():N}");
		var child = await RunAsync($"@create InheritChild{Guid.NewGuid():N}");
		await RunAsync($"@parent {child}={parent}");
		await RunAsync($"@lock {parent}=#FALSE");
		await RunAsync($"@lset {parent}/Basic=!no_inherit");
		await Assert.That(await ReadAsync($"lock({child})")).IsEqualTo("#FALSE");
		await Assert.That(await ReadAsync($"elock({child},#1)")).IsEqualTo("0");
		await RunAsync($"@lock {parent}=#TRUE");
		await Assert.That(await ReadAsync($"lock({child})")).IsEqualTo("#TRUE");
		await Assert.That(await ReadAsync($"elock({child},#1)")).IsEqualTo("1");
		await RunAsync($"@unlock {parent}");
		await Assert.That(await ReadAsync($"lock({child})")).IsEqualTo("*UNLOCKED*");
	}

	private static LockService AncestorService(AnySharpObject ancestor)
	{
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		var config = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		options.CurrentValue.Returns(config with { Database = config.Database with { AncestorPlayer = (uint)ancestor.Object().DBRef.Number } });
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(ancestor));
		return new LockService(Substitute.For<IBooleanExpressionParser>(), options, mediator,
			new Lazy<IPermissionService>(() => Substitute.For<IPermissionService>()));
	}

	private async Task<string> RunAsync(string command)
		=> (await Factory.CommandParser.CommandParse(1, Factory.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command))).Message!.ToPlainText();

	private async Task<string> ReadAsync(string expression)
		=> (await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
}
