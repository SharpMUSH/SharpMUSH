using System.Runtime.CompilerServices;
using Mediator;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class AttributeReadCancellationTests
{
	[Test]
	public async Task DeferredPatternDoesNotRestartADisposedOriginatingDeadline()
	{
		var target = new TestObjectFactory().CreateThing(1, "god");
		var mediator = Substitute.For<IMediator>();
		var service = new AttributeService(mediator, Substitute.For<IPermissionService>(), Substitute.For<ILocateService>(), Substitute.For<IValidateService>(),
			Substitute.For<INotifyService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>());
		LazySharpAttributesOrError result;
		using (var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(100)))
		using (budget.Enter())
			result = await service.LazilyGetAttributePatternAsync(target, target, "*", false);
		await Task.Delay(150);
		await Assert.That(async () => await result.Expect<IAsyncEnumerable<LazySharpAttribute>>().ToArrayAsync()).Throws<OperationCanceledException>();
		mediator.DidNotReceive().CreateStream(Arg.Any<GetLazyAttributesQuery>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments(false, "query")]
	[Arguments(true, "query")]
	[Arguments(false, "flags")]
	[Arguments(true, "flags")]
	[Arguments(false, "permission")]
	[Arguments(true, "permission")]
	[Arguments(true, "caller-query")]
	[Arguments(true, "caller-permission")]
	[Arguments(true, "consumer-query")]
	[Arguments(true, "consumer-permission")]
	[Arguments(true, "second-permission")]
	[Arguments(true, "second-parent")]
	[Arguments(true, "second-prefix")]
	[Arguments(true, "privileged-query")]
	[Arguments(false, "parent")]
	[Arguments(true, "parent")]
	[Arguments(true, "caller-parent")]
	[Arguments(false, "prefix")]
	[Arguments(true, "prefix")]
	[Arguments(true, "caller-prefix")]
	public async Task PatternReadsRespectExecutionAndEnumerationCancellation(bool lazy, string stage)
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var mediator = Substitute.For<IMediator>();
		var permissions = Substitute.For<IPermissionService>();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst")));
		var service = new AttributeService(mediator, permissions, Substitute.For<ILocateService>(), Substitute.For<IValidateService>(),
			Substitute.For<INotifyService>(), options, Substitute.For<IServiceProvider>());
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = stage.StartsWith("caller-") || stage.StartsWith("consumer-") || stage.StartsWith("second-") ? null : budget.Enter();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task Block(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
		}
		async IAsyncEnumerable<T> BlockStream<T>([EnumeratorCancellation] CancellationToken token = default)
		{
			await Block(token);
			yield break;
		}
		if (stage.EndsWith("parent")) target.Object().Parent = new(async token => { await Block(token); return new None(); });
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(call => BlockStream<SharpAttribute>(call.Arg<CancellationToken>()));
		mediator.CreateStream(Arg.Any<GetLazyAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(call => BlockStream<LazySharpAttribute>(call.Arg<CancellationToken>()));
		var source = stage.EndsWith("parent") ? new DBRef(11) : target.Object().DBRef;
		var attr = TestAttributeFactory.Named(stage.EndsWith("prefix") ? "TREE`LEAF" : "RUN");
		var lazyAttr = new LazySharpAttribute(attr.Id, attr.Key, attr.Name, attr.Flags, null, attr.LongName,
			new(_ => Task.FromResult(AsyncEnumerable.Empty<LazySharpAttribute>())), attr.Owner, attr.SharpAttributeEntry,
			new(_ => Task.FromResult<MarkupString.MarkupText>(MarkupString.MarkupText.Empty)));
		mediator.CreateStream(Arg.Any<GetAttributesQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			stage.EndsWith("query") ? BlockStream<AttributeWithSource>() : new[] { new AttributeWithSource(attr, source) }.ToAsyncEnumerable());
		mediator.CreateStream(Arg.Any<GetLazyAttributesQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			stage.EndsWith("query") ? BlockStream<LazyAttributeWithSource>() : (stage.StartsWith("second-") ? new[] { new LazyAttributeWithSource(lazyAttr with { Name = "A", Key = "A", LongName = "A" }, target.Object().DBRef), new LazyAttributeWithSource(lazyAttr, source) } : new[] { new LazyAttributeWithSource(lazyAttr, source) }).ToAsyncEnumerable());
		if (stage == "flags") target.Object().Flags = new(() => BlockStream<SharpObjectFlag>());
		if (stage == "privileged-query") target.Object().Flags = new(() => new[] { new SharpObjectFlag { Name = "WIZARD", Symbol = "W", UnsetPermissions = [], SetPermissions = [], System = false, TypeRestrictions = [] } }.ToAsyncEnumerable());
		async ValueTask<bool> Permission()
		{
			await Block(CancellationToken.None); // Legacy permission API cannot accept a token.
			return true;
		}
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<SharpAttribute[]>()).Returns(_ => Permission());
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<LazySharpAttribute[]>()).Returns(call => stage.StartsWith("second-") && call.Arg<LazySharpAttribute[]>().Last().LongName == "A" ? new ValueTask<bool>(true) : Permission());
		async Task Read()
		{
			if (!lazy) await service.GetAttributePatternAsync(target, target, "*", false, IAttributeService.AttributePatternMode.Wildcard);
			else
			{
				var result = await service.LazilyGetAttributePatternAsync(target, target, "*", false);
				using var enumerationScope = stage.StartsWith("consumer-") ? budget.Enter() : null;
				await result.Expect<IAsyncEnumerable<LazySharpAttribute>>().ToArrayAsync(stage.StartsWith("caller-") || stage.StartsWith("second-") ? cancel.Token : default);
			}
		}
		var operation = Read();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			cancel.Cancel();
			await Assert.That(async () => await operation.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
		}
		finally
		{
			cleanup.Cancel();
			try { await operation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StalledPermissionReadsDoNotHoldTheExecutionAfterCancellation(bool execute)
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var mediator = Substitute.For<IMediator>();
		var permissions = Substitute.For<IPermissionService>();
		var validation = Substitute.For<IValidateService>();
		validation.Valid(Arg.Any<IValidateService.ValidationType>(), Arg.Any<MarkupString.MarkupText>(), Arg.Any<ValidationTarget>()).Returns(true);
		var attributes = new[] { TestAttributeFactory.Named("RUN") };
		mediator.CreateStream(Arg.Any<GetAttributeWithInheritanceQuery>(), Arg.Any<CancellationToken>())
			.Returns(new[] { new AttributeWithInheritance(attributes, target.Object().DBRef, AttributeSource.Self, []) }.ToAsyncEnumerable());
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		ValueTask<bool> Block() { entered.TrySetResult(); return new(release.Task); }
		permissions.CanExecuteAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<SharpAttribute[]>()).Returns(_ => Block());
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<SharpAttribute[]>()).Returns(_ => Block());
		var service = new AttributeService(mediator, permissions, Substitute.For<ILocateService>(), validation,
			Substitute.For<INotifyService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>());
		using var cancel = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var operation = service.GetAttributeAsync(target, target, "RUN",
			execute ? IAttributeService.AttributeMode.Execute : IAttributeService.AttributeMode.Read, false).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			cancel.Cancel();
			await Assert.That(async () => await operation.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
		}
		finally
		{
			release.TrySetResult(true);
			try { await operation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments("fallback")]
	[Arguments("parent")]
	[Arguments("ancestor-node")]
	[Arguments("prefix")]
	[Arguments("lazy-fallback")]
	[Arguments("orphan-flags")]
	public async Task SecondaryAttributeReadsReceiveExecutionCancellation(string stage)
	{
		var mediator = Substitute.For<IMediator>();
		var factory = new TestObjectFactory();
		var target = factory.CreateThing(10, "target");
		var ancestor = factory.CreateThing(6, "ancestor");
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Database = config.Database with { AncestorThing = 6 } });
		var validation = Substitute.For<IValidateService>();
		validation.Valid(Arg.Any<IValidateService.ValidationType>(), Arg.Any<MarkupString.MarkupText>(), Arg.Any<ValidationTarget>()).Returns(true);
		var service = new AttributeService(mediator, Substitute.For<IPermissionService>(), Substitute.For<ILocateService>(), validation,
			Substitute.For<INotifyService>(), options, Substitute.For<IServiceProvider>());
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task Block(CancellationToken token)
		{
			entered.TrySetResult(token);
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
		}
		async IAsyncEnumerable<T> BlockStream<T>(CancellationToken queryToken, [EnumeratorCancellation] CancellationToken token = default)
		{
			await Block(queryToken.CanBeCanceled ? queryToken : token);
			yield break;
		}
		if (stage == "orphan-flags") target.Object().Flags = new(() => BlockStream<SharpObjectFlag>(default));
		var resolved = new AttributeWithInheritance([TestAttributeFactory.Named("TREE"), TestAttributeFactory.Named("TREE`LEAF")], ancestor.Object().DBRef, AttributeSource.Parent, []);
		mediator.CreateStream(Arg.Any<GetAttributeWithInheritanceQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var query = call.Arg<GetAttributeWithInheritanceQuery>();
			if (stage == "fallback" && query.DBRef.Number == 6) return BlockStream<AttributeWithInheritance>(call.Arg<CancellationToken>());
			if (stage is "parent" or "prefix" || (stage == "ancestor-node" && query.DBRef.Number == 6)) return new[] { resolved }.ToAsyncEnumerable();
			return AsyncEnumerable.Empty<AttributeWithInheritance>();
		});
		mediator.CreateStream(Arg.Any<GetLazyAttributeWithInheritanceQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			call.Arg<GetLazyAttributeWithInheritanceQuery>().DBRef.Number == 6
				? BlockStream<LazyAttributeWithInheritance>(call.Arg<CancellationToken>()) : AsyncEnumerable.Empty<LazyAttributeWithInheritance>());
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(call => BlockStream<SharpAttribute>(call.Arg<CancellationToken>()));
		if (stage == "parent") target.Object().Parent = new(async token => { await Block(token); return new None(); });
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
		{
			await Block(call.Arg<CancellationToken>());
			return ancestor;
		});
		var operation = stage == "lazy-fallback"
			? service.LazilyGetAttributeAsync(target, target, "TREE`LEAF", IAttributeService.AttributeMode.Read).AsTask() as Task
			: service.GetAttributeAsync(target, target, "TREE`LEAF", IAttributeService.AttributeMode.Read).AsTask();
		try
		{
			var observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await Assert.That(observed).IsEqualTo(budget.Token);
			cancel.Cancel();
			await Assert.That(async () => await operation!.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
		}
		finally
		{
			cleanup.Cancel();
			try { await operation!; } catch (OperationCanceledException) { }
		}
	}
}
