using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

public class HttpHandlerBudgetTests
{
	private static (HttpHandlerCommandService Service, IMUSHCodeParser Parser, HttpOutputCapture Capture) Create(
		Func<ParserState, ValueTask<CallState?>> execute, uint milliseconds = 0, IAttributeService? attributeService = null, IMediator? handlerMediator = null)
	{
		var handler = new TestObjectFactory().CreateThing(8, "HTTP handler").AsThing;
		var mediator = handlerMediator ?? Substitute.For<IMediator>();
		if (handlerMediator is null) mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(handler);
		var attributes = attributeService ?? Substitute.For<IAttributeService>();
		if (attributeService is null) attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "GET",
			IAttributeService.AttributeMode.Execute, false).Returns((OptionalSharpAttributeOrError)new[]
			{ new SharpAttribute("", "", "GET", [], null, "GET", null!, null!, null!) { Value = MarkupText.Plain("think handler") } });
		var parser = Substitute.For<IMUSHCodeParser>();
		ParserState? state = null;
		parser.Push(Arg.Any<ParserState>()).Returns(call => { state = call.Arg<ParserState>(); return parser; });
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => execute(state!));
		var baseline = TestSharpMushOptions.Create();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Database = baseline.Database with { HttpHandler = 8 },
			Limit = baseline.Limit with { QueueEntryCpuTime = milliseconds }
		});
		var capture = new HttpOutputCapture();
		return (new(mediator, attributes, parser, capture, Substitute.For<IEventService>(), options,
			NullLogger<HttpHandlerCommandService>.Instance), parser, capture);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HandlerObjectLookupHonorsRequestCancellationAndDeadline(bool expire)
	{
		var mediator = Substitute.For<IMediator>();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
			{
				entered.TrySetResult();
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), release.Token);
				await Task.Delay(Timeout.Infinite, linked.Token);
				throw new InvalidOperationException("The blocked object lookup must be cancelled.");
			});
		var (service, _, _) = Create(_ => ValueTask.FromResult<CallState?>(CallState.Empty), expire ? 100u : 0u, handlerMediator: mediator);
		using var cancellation = new CancellationTokenSource();
		var dispatch = service.DispatchAsync("GET", "/object", "", [], cancellation.Token).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (expire) await Assert.That((await dispatch.WaitAsync(TimeSpan.FromSeconds(2))).AsT0.Status).IsEqualTo(503);
			else
			{
				cancellation.Cancel();
				await Assert.That(async () => await dispatch.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
			}
		}
		finally
		{
			release.Cancel();
			try { await dispatch; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HandlerLookupHonorsRequestCancellationAndDeadline(bool expire)
	{
		var attributes = Substitute.For<IAttributeService>();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "GET",
			IAttributeService.AttributeMode.Execute, false).Returns(async ValueTask<OptionalSharpAttributeOrError> (_) =>
		{
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			throw new InvalidOperationException("The blocked lookup must be cancelled.");
		});
		var (service, _, capture) = Create(_ => ValueTask.FromResult<CallState?>(CallState.Empty), expire ? 100u : 0u, attributes);
		using var cancellation = new CancellationTokenSource();
		var dispatch = service.DispatchAsync("GET", "/lookup", "", [], cancellation.Token).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (expire) await Assert.That((await dispatch.WaitAsync(TimeSpan.FromSeconds(2))).AsT0.Status).IsEqualTo(503);
			else
			{
				cancellation.Cancel();
				await Assert.That(async () => await dispatch.WaitAsync(TimeSpan.FromSeconds(2))).Throws<OperationCanceledException>();
			}
			await Assert.That(capture.TryCapture(8, "late output")).IsFalse();
		}
		finally { release.Cancel(); }
	}

	[Test]
	public async Task ExpiredHandlerCannotReturnItsPartialSuccessResponse()
	{
		var (service, _, capture) = Create(async state =>
		{
			state.HttpResponse!.StatusLine = "201 Created";
			state.HttpResponse.ContentType = "application/json";
			state.HttpResponse.Headers.Add(("X-Partial", "discard"));
			state.HttpResponse.Body.Append("partial secret");
			await Task.Delay(30);
			return new CallState(ExecutionBudget.Error) { HadErrors = true };
		}, 10);
		var result = await service.DispatchAsync("GET", "/budget", "", []);
		await Assert.That(result.AsT0.Status).IsEqualTo(503);
		await Assert.That(result.AsT0.ContentType).IsEqualTo("text/plain");
		await Assert.That(result.AsT0.Body).IsEqualTo(ExecutionBudget.Error);
		await Assert.That(result.AsT0.Headers.Count).IsEqualTo(0);
		await Assert.That(capture.TryCapture(8, "late output")).IsFalse();
	}

	[Test]
	public async Task RequestCancellationReachesTheHandlerAndUnwindsCapture()
	{
		using var cancellation = new CancellationTokenSource();
		var observed = false;
		var (service, _, capture) = Create(state =>
		{
			cancellation.Cancel();
			observed = state.ExecutionBudget?.Token.IsCancellationRequested == true;
			return ValueTask.FromResult<CallState?>(CallState.Empty);
		});
		await Assert.That(async () => await service.DispatchAsync("GET", "/cancel", "", [], cancellation.Token))
			.Throws<OperationCanceledException>();
		await Assert.That(observed).IsTrue();
		await Assert.That(capture.TryCapture(8, "late output")).IsFalse();
	}

	[Test]
	public async Task SuccessfulHandlerPreservesItsChosenResponse()
	{
		var (service, _, _) = Create(state =>
		{
			state.HttpResponse!.StatusLine = "202 Accepted";
			state.HttpResponse.ContentType = "application/json";
			state.HttpResponse.Headers.Add(("X-Handler", "present"));
			state.HttpResponse.Body.Append("{\"ok\":true}");
			return ValueTask.FromResult<CallState?>(new CallState(ExecutionBudget.Error));
		});
		var result = await service.DispatchAsync("GET", "/normal", "", []);
		await Assert.That(result.AsT0.Status).IsEqualTo(202);
		await Assert.That(result.AsT0.ContentType).IsEqualTo("application/json");
		await Assert.That(result.AsT0.Body).IsEqualTo("{\"ok\":true}");
		await Assert.That(result.AsT0.Headers.Single().Name).IsEqualTo("X-Handler");
	}
}
