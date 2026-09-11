using System.Runtime.CompilerServices;
using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class AttributeVisibilityCancellationTests
{
	private static LazySharpAttribute Lazy(SharpAttribute attr) => new(attr.Id, attr.Key, attr.Name, attr.Flags, null, attr.LongName,
		new(_ => Task.FromResult(AsyncEnumerable.Empty<LazySharpAttribute>())), attr.Owner, attr.SharpAttributeEntry,
		new(_ => Task.FromResult<MarkupString.MarkupText>(MarkupString.MarkupText.Empty)));

	[Test]
	[Arguments(false, "root")]
	[Arguments(true, "root")]
	[Arguments(false, "permission")]
	[Arguments(true, "permission")]
	[Arguments(false, "leaves")]
	[Arguments(true, "leaves")]
	[Arguments(false, "leaf-stream")]
	[Arguments(true, "leaf-stream")]
	[Arguments(false, "child-permission")]
	[Arguments(true, "child-permission")]
	[Arguments(true, "caller-permission")]
	[Arguments(true, "consumer-permission")]
	[Arguments(true, "second-permission")]
	[Arguments(true, "second-leaves")]
	public async Task VisibleReadsStopAtEveryEnumerationAndPermissionBoundary(bool lazy, string stage)
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var permissions = Substitute.For<IPermissionService>();
		var service = new AttributeService(Substitute.For<IMediator>(), permissions, Substitute.For<ILocateService>(), Substitute.For<IValidateService>(),
			Substitute.For<INotifyService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>());
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = stage.Contains('-') && (stage.StartsWith("caller-") || stage.StartsWith("consumer-") || stage.StartsWith("second-")) ? null : budget.Enter();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task Block(CancellationToken token)
		{
			entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token);
		}
		async IAsyncEnumerable<T> BlockStream<T>([EnumeratorCancellation] CancellationToken token = default)
		{
			await Block(token);
			yield break;
		}
		var root = TestAttributeFactory.Named("ROOT");
		var child = TestAttributeFactory.Named("ROOT`CHILD");
		var lazyRoot = Lazy(root);
		var lazyChild = Lazy(child);
		root = root with
		{
			Leaves = new(async token =>
		{
			if (stage.EndsWith("leaves")) await Block(token);
			return stage == "leaf-stream" ? BlockStream<SharpAttribute>() : new[] { child }.ToAsyncEnumerable();
		})
		};
		lazyRoot = lazyRoot with
		{
			Leaves = new(async token =>
		{
			if (stage.EndsWith("leaves")) await Block(token);
			return stage == "leaf-stream" ? BlockStream<LazySharpAttribute>() : new[] { lazyChild }.ToAsyncEnumerable();
		})
		};
		target.Object().Attributes = new(() => stage == "root" ? BlockStream<SharpAttribute>() : new[] { root }.ToAsyncEnumerable());
		target.Object().LazyAttributes = new(() => stage == "root" ? BlockStream<LazySharpAttribute>() :
			(stage == "second-permission" ? new[] { lazyRoot, lazyRoot with { Name = "SECOND", LongName = "SECOND" } } : new[] { lazyRoot }).ToAsyncEnumerable());
		async ValueTask<bool> Permission(string name)
		{
			if (stage.EndsWith("permission") && (stage != "child-permission" || name.Contains('`')) && (stage != "second-permission" || name == "SECOND"))
				await Block(CancellationToken.None);
			return true;
		}
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<SharpAttribute>()).Returns(call => Permission(call.Arg<SharpAttribute[]>()[0].LongName));
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<LazySharpAttribute>()).Returns(call => Permission(call.Arg<LazySharpAttribute[]>()[0].LongName));
		var depth = stage.EndsWith("leaves") || stage == "leaf-stream" || stage == "child-permission" ? 2 : 1;
		async Task Read()
		{
			if (!lazy) await service.GetVisibleAttributesAsync(target, target, depth);
			else
			{
				var result = await service.LazilyGetVisibleAttributesAsync(target, target, depth);
				using var consumer = stage.StartsWith("consumer-") ? budget.Enter() : null;
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
		finally { cleanup.Cancel(); try { await operation; } catch (OperationCanceledException) { } }
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task LazyVisibilityHonorsRequestedDepth(int depth)
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var permissions = Substitute.For<IPermissionService>();
		permissions.CanViewAttribute(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<LazySharpAttribute>()).Returns(true);
		var service = new AttributeService(Substitute.For<IMediator>(), permissions, Substitute.For<ILocateService>(), Substitute.For<IValidateService>(),
			Substitute.For<INotifyService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>());
		var node = Lazy(TestAttributeFactory.Named("FOUR"));
		foreach (var name in new[] { "THREE", "TWO", "ONE" })
		{
			var child = node;
			node = Lazy(TestAttributeFactory.Named(name)) with { Leaves = new(_ => Task.FromResult(new[] { child }.ToAsyncEnumerable())) };
		}
		target.Object().LazyAttributes = new(() => new[] { node }.ToAsyncEnumerable());
		var result = await service.LazilyGetVisibleAttributesAsync(target, target, depth);
		// Take four bounds the old traversal without waiting for its non-terminating tail.
		await Assert.That((await result.Expect<IAsyncEnumerable<LazySharpAttribute>>().Take(4).ToArrayAsync()).Length).IsEqualTo(depth);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task AttributeEvaluationPreflightReadsRespectCancellation(bool stringTarget, bool identity)
	{
		var target = new TestObjectFactory().CreateThing(10, "target");
		var mediator = Substitute.For<IMediator>();
		var validation = Substitute.For<IValidateService>();
		var service = new AttributeService(mediator, Substitute.For<IPermissionService>(), Substitute.For<ILocateService>(), validation,
			Substitute.For<INotifyService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Substitute.For<IServiceProvider>());
		using var cancel = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task Block(CancellationToken token) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token).WaitAsync(cleanup.Token); }
		validation.Valid(Arg.Any<IValidateService.ValidationType>(), Arg.Any<MarkupString.MarkupText>(), Arg.Any<ValidationTarget>())
			.Returns(async ValueTask<bool> (_) => { if (!identity) await Block(CancellationToken.None); return true; });
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(async ValueTask<AnyOptionalSharpObject> (call) => { await Block(call.Arg<CancellationToken>()); return new None(); });
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(ParserState.RootFor(target.Object().DBRef));
		var operation = stringTarget
			? service.EvaluateAttributeFunctionAsync(parser, target, MarkupString.MarkupText.Plain("#10/RUN"), [], ignorePermissions: true).AsTask()
			: service.EvaluateAttributeFunctionAsync(parser, target, target, "RUN", [], ignorePermissions: true).AsTask();
		try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel(); await Assert.That(async () => await operation.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>(); }
		finally { cleanup.Cancel(); try { await operation; } catch (OperationCanceledException) { } }
	}
}
