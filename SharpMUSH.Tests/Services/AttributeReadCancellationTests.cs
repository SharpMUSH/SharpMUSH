using System.Runtime.CompilerServices;
using Mediator;
using NSubstitute;
using OneOf.Types;
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
		validation.Valid(Arg.Any<IValidateService.ValidationType>(), Arg.Any<MarkupString.MarkupText>(), Arg.Any<OneOf.OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None>>()).Returns(true);
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
			return ancestor.AsThing;
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
