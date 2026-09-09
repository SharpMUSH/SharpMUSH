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
		Func<ParserState, ValueTask<CallState?>> execute, uint milliseconds = 0)
	{
		var handler = new TestObjectFactory().CreateThing(8, "HTTP handler").AsThing;
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(handler);
		var attributes = Substitute.For<IAttributeService>();
		attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "GET",
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
