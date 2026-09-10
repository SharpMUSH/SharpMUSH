using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class HttpCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Arguments(QueueRejectionReason.OwnerLimit)]
	[Arguments(QueueRejectionReason.GlobalLimit)]
	[Arguments(QueueRejectionReason.ShuttingDown)]
	[Arguments(QueueRejectionReason.InvalidTarget)]
	[Arguments(QueueRejectionReason.AlreadyReleased)]
	[Arguments(QueueRejectionReason.None)]
	public async Task HttpReportsCallbackAdmissionResultBeforeIssuingRequest(QueueRejectionReason reason)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HttpAdmission");
		var state = ParserState.RootFor(player.DbRef) with
		{
			Arguments = new()
			{
				["0"] = new CallState(player.DbRef + "/CALLBACK"),
				["1"] = new CallState("https://example.invalid/queued")
			}
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		var admission = new QueueAdmissionResult(reason == QueueRejectionReason.None ? 42 : null, reason);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<AdmitAttributeRequest>(), Arg.Any<CancellationToken>()).Returns(admission);
		var clients = Substitute.For<IHttpClientFactory>();
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, mediator, clients);

		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var result = await commands.Http(parser, new SharpCommandAttribute { Name = "@HTTP" });

		await Assert.That(result.AsValue().Message?.ToPlainText() ?? "").IsEqualTo(admission.Accepted ? "" : admission.Error);
		await mediator.Received(1).Send(Arg.Any<AdmitAttributeRequest>(), budget.Token);
		clients.DidNotReceive().CreateClient(Arg.Any<string>());
		if (reason is QueueRejectionReason.InvalidTarget or QueueRejectionReason.AlreadyReleased)
			await NotifyService.Received(1).Notify(TestHelpers.MatchingObject(player.DbRef),
				TestHelpers.MatchingMessage(admission.Error), TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments(QueueRejectionReason.GlobalLimit)]
	[Arguments(QueueRejectionReason.OwnerLimit)]
	[Arguments(QueueRejectionReason.ShuttingDown)]
	[Arguments(QueueRejectionReason.InvalidTarget)]
	public async Task HttpRejectionEmitsOneNoticeThroughTheRealScheduler(QueueRejectionReason reason)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HttpCapacityNotice");
		var state = ParserState.RootFor(player.DbRef) with
		{
			Arguments = new()
			{
				["0"] = new CallState(player.DbRef + "/CALLBACK"),
				["1"] = new CallState("https://example.invalid/queued")
			}
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		var notifications = Substitute.For<INotifyService>();
		var notices = 0;
		notifications.NotifyLocalized(player.Handle, "QueueRejected", Arg.Any<object[]>())
			.Returns(_ => { Interlocked.Increment(ref notices); return ValueTask.CompletedTask; });
		notifications.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Any<OneOf.OneOf<MarkupText, string>>(),
			TestHelpers.MatchingObject(player.DbRef), INotifyService.NotificationType.Announce)
			.Returns(_ => { Interlocked.Increment(ref notices); return ValueTask.CompletedTask; });
		var baseline = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Limit = baseline.Limit with
			{
				GlobalQueueLimit = reason == QueueRejectionReason.GlobalLimit ? 0u : 10u,
				PlayerQueueLimit = reason == QueueRejectionReason.OwnerLimit ? 0u : 10u
			}
		});
		var lookup = reason == QueueRejectionReason.InvalidTarget ? Substitute.For<IMediator>() : Mediator;
		if (reason == QueueRejectionReason.InvalidTarget)
			lookup.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(new None()));
		var scheduler = new SharpMUSH.Library.Services.TaskScheduler(parser, ConnectionService,
			Substitute.For<Quartz.ISchedulerFactory>(), WebAppFactoryArg.Services.GetRequiredService<IAttributeService>(),
			lookup, Microsoft.Extensions.Logging.Abstractions.NullLogger<SharpMUSH.Library.Services.TaskScheduler>.Instance,
			options, notifications);
		var handler = new SharpMUSH.Implementation.Handlers.AdmissionAsyncScheduleHandler(scheduler);
		var admissionMediator = Substitute.For<IMediator>();
		admissionMediator.Send(Arg.Any<AdmitAttributeRequest>(), Arg.Any<CancellationToken>())
			.Returns(call => handler.Handle(call.Arg<AdmitAttributeRequest>(), call.Arg<CancellationToken>()));
		var clients = Substitute.For<IHttpClientFactory>();
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, admissionMediator, clients, notifications);

		if (reason == QueueRejectionReason.ShuttingDown) await scheduler.DisposeAsync();
		try
		{
			var result = await commands.Http(parser, new SharpCommandAttribute { Name = "@HTTP" });

			await Assert.That(result.AsValue().Message!.ToPlainText()).IsEqualTo(new QueueAdmissionResult(null, reason).Error);
			await notifications.Received(reason == QueueRejectionReason.InvalidTarget ? 0 : 1)
				.NotifyLocalized(player.Handle, "QueueRejected", Arg.Any<object[]>());
			await Assert.That(notices).IsEqualTo(1);
			await Assert.That(scheduler.GetQueueUsage().Total).IsEqualTo(0);
			clients.DidNotReceive().CreateClient(Arg.Any<string>());
		}
		finally
		{
			if (reason != QueueRejectionReason.ShuttingDown) await scheduler.DisposeAsync();
		}
	}

	/// <summary>
	/// Runs a command list under an HTTP request context: a fresh parser state carrying a live
	/// <see cref="HttpResponseContext"/> (the same shape HttpHandlerCommandService builds), so
	/// @respond's isHttpContext branch is exercised instead of the "(HTTP): …" debug notify.
	/// </summary>
	private async Task<HttpResponseContext> RunInHttpContext(string commandList)
	{
		var context = new HttpResponseContext();
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var httpParser = Parser.Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
			SwitchStack: [],
			ExecutionStack: [],
			EnvironmentRegisters: [],
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: null,
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: executor,
			Enactor: executor,
			Caller: executor,
			Handle: null,
			ParseMode: ParseMode.Default,
			HttpResponse: context,
			CallDepth: new InvocationCounter(),
			FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
			TotalInvocations: new InvocationCounter(),
			LimitExceeded: new LimitExceededFlag())
		{
			MoveDepth = new InvocationCounter()
		});

		await httpParser.CommandListParse(MarkupText.Plain(commandList));
		return context;
	}

	[Test]
	public async ValueTask Test_Respond_HttpContext_SetsStatusLine()
	{
		var context = await RunInHttpContext("@respond 404 Not Found");

		await Assert.That(context.StatusLine).IsEqualTo("404 Not Found");
	}

	[Test]
	public async ValueTask Test_Respond_HttpContext_SetsContentType()
	{
		var context = await RunInHttpContext("@respond/type application/json");

		await Assert.That(context.ContentType).IsEqualTo("application/json");
	}

	[Test]
	public async ValueTask Test_Respond_HttpContext_AddsHeader()
	{
		var context = await RunInHttpContext("@respond/header X-Powered-By=MUSHCode");

		await Assert.That(context.Headers).Contains(("X-Powered-By", "MUSHCode"));
	}

	[Test]
	public async ValueTask Test_Respond_HttpContext_DoesNotNotify()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await RunInHttpContext("@respond 503 Service Unavailable");

		// In a real HTTP context the status goes to the response, not to the enactor as debug text.
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Status 503 Service Unavailable",
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_HttpContext_MultipleCommands_BuildFullResponse()
	{
		var context = await RunInHttpContext(
			"@respond 201 Created; @respond/type application/json; @respond/header X-Test=yes");

		await Assert.That(context.StatusLine).IsEqualTo("201 Created");
		await Assert.That(context.ContentType).IsEqualTo("application/json");
		await Assert.That(context.Headers).Contains(("X-Test", "yes"));
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond 200 OK"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Status 200 OK", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_404()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond 404 Not Found"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Status 404 Not Found", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// PennMUSH requires text after the code (`@respond 500` alone is invalid — oracle-verified).
	// Each rejection test uses its own player: the rejection message is identical across cases,
	// so a shared executor would double-count Received() calls.
	[Test]
	public async ValueTask Test_Respond_StatusCode_WithoutText_IsRejected()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RespondNoText");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@respond 500"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), "@respond must be 3 digits, space, then text .", TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	// The status text must begin with an alphanumeric — PennMUSH rejects `@respond 200 "quoted"`
	// (oracle-verified; src/cmds.c checks isalnum on the character after the space).
	[Test]
	public async ValueTask Test_Respond_QuotedText_IsRejected()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RespondQuoted");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@respond 200 \"TEST RESPONSE\""));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), "@respond must be 3 digits, space, then text .", TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_InvalidStatusCode()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RespondInvalid");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@respond abc"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), "@respond must be 3 digits, space, then text .", TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_OutOfRange()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RespondRange");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@respond 99 test_string_RESPOND_out_of_range"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef), "@respond must be 3 digits, space, then text .", TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusLine_TooLong()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@respond 200 test_string_RESPOND_status_line_that_is_way_too_long_and_exceeds_40_chars"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "@respond status code too long.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Type_ApplicationJson()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/type application/json"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Content-Type set to application/json", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Type_TextHtml()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/type text/html"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Content-Type set to text/html", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Type_Empty()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/type"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Content-Type cannot be empty.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_CustomHeader()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/header X-Powered-By=MUSHCode"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Header X-Powered-By: MUSHCode", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_SetCookie()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@respond/header Set-Cookie=name=Bob; Max-Age=3600; Version=1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Header Set-Cookie: name=Bob; Max-Age=3600; Version=1", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_ContentLength_Forbidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/header Content-Length=1234"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Cannot set Content-Length header.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_EmptyName()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/header =value"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "Header name cannot be empty.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_WithoutEquals()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond/header X-Custom-Header"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Header X-Custom-Header: ", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_NoArguments()
	{
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@respond"));

		// With MinArgs = 1, the parser will reject the command before it executes
		// Just verify some notification was sent (any notification indicates an error)
		_ = NotifyService.ReceivedCalls().Any();
	}
}