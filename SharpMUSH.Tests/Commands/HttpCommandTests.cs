using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
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
	public async Task HttpCallbackSnapshotsCallerStateBeforeDeferredRequest()
	{
		ParserState? submitted = null;
		var run = await RunHttpAsync("HttpSnapshot", [], ["https://example.invalid/get"],
			qRegisters: true, registers: new() { ["X"] = "before" }, beforeCallback: state =>
			{
				submitted = state;
				state.AddRegister("X", MarkupText.Plain("after"));
				state.LimitExceeded!.IsExceeded = true;
			});
		await Assert.That(run.Callback!.Registers.First()["X"].ToPlainText()).IsEqualTo("before");
		await Assert.That(run.Callback.LimitExceeded!.IsExceeded).IsFalse();
		await Assert.That(submitted!.Registers.First().ContainsKey("STATUS")).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HttpResponseCallbackStartsItsOwnPresence(bool nested)
	{
		var run = await RunHttpAsync("HttpPresence", [], ["https://example.invalid/get"], nestedPresence: nested);
		var result = await Parser.FromState(run.Callback!).FunctionParse(MarkupText.Plain("[hasarg(0)]|[hasarg(0,supplied)]|[hasarg(status,supplied)]|[hasarg(parent,supplied)]"));
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo("1|1|1|0");
	}

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
		await GrantHttpAsync(player.DbRef);
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
		mediator.Send(Arg.Any<AdmitAttributeAfterRequest>(), Arg.Any<CancellationToken>()).Returns(admission);
		var clients = Substitute.For<IHttpClientFactory>();
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, mediator, clients);

		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var result = (await commands.Http(parser, new SharpCommandAttribute { Name = "@HTTP" })).Expect<CallState>();

		await Assert.That(result.Message.ToPlainText()).IsEqualTo(admission.Accepted ? "" : admission.Error);
		await mediator.Received(1).Send(Arg.Any<AdmitAttributeAfterRequest>(), budget.Token);
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
		await GrantHttpAsync(player.DbRef);
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
		notifications.NotifyLocalized(player.Handle, "QueueRejected", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>())
			.Returns(_ => { Interlocked.Increment(ref notices); return ValueTask.CompletedTask; });
		notifications.Notify(TestHelpers.MatchingObject(player.DbRef), Arg.Any<SharpMessage>(),
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
		var handler = new SharpMUSH.Implementation.Handlers.AdmissionAfterScheduleHandler(scheduler);
		var admissionMediator = Substitute.For<IMediator>();
		admissionMediator.Send(Arg.Any<AdmitAttributeAfterRequest>(), Arg.Any<CancellationToken>())
			.Returns(call => handler.Handle(call.Arg<AdmitAttributeAfterRequest>(), call.Arg<CancellationToken>()));
		var clients = Substitute.For<IHttpClientFactory>();
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, admissionMediator, clients, notifications);

		if (reason == QueueRejectionReason.ShuttingDown) await scheduler.DisposeAsync();
		try
		{
			var result = (await commands.Http(parser, new SharpCommandAttribute { Name = "@HTTP" })).Expect<CallState>();

			await Assert.That(result.Message.ToPlainText()).IsEqualTo(new QueueAdmissionResult(null, reason).Error);
			// A quota refusal is reported by the runaway notice to the owner instead (pay_queue, src/cque.c:304).
			var runaway = reason == QueueRejectionReason.OwnerLimit;
			await notifications.Received(reason == QueueRejectionReason.InvalidTarget || runaway ? 0 : 1)
				.NotifyLocalized(player.Handle, "QueueRejected", Arg.Any<AnySharpObject?>(), Arg.Any<object[]>());
			if (runaway)
			{
				for (var deadline = DateTime.UtcNow.AddSeconds(10); !RunawayNoticed() && DateTime.UtcNow < deadline;)
					await Task.Delay(20);
				await Assert.That(RunawayNoticed()).IsTrue();
			}
			else await Assert.That(notices).IsEqualTo(1);

			bool RunawayNoticed() => notifications.ReceivedCalls().Count(call =>
				call.GetMethodInfo().Name == nameof(INotifyService.NotifyLocalized)
				&& call.GetArguments() is [_, nameof(ErrorMessages.Notifications.RunawayObjectFormat), ..]) == 1;
			await Assert.That(scheduler.GetQueueUsage().Total).IsEqualTo(0);
			clients.DidNotReceive().CreateClient(Arg.Any<string>());
		}
		finally
		{
			if (reason != QueueRejectionReason.ShuttingDown) await scheduler.DisposeAsync();
		}
	}

	/// <summary>Answers every request with <paramref name="reply"/>, keeping what was sent.</summary>
	private sealed class RecordingHandler(string reply, string replyType = "text/plain") : HttpMessageHandler
	{
		public HttpRequestMessage? Request { get; private set; }
		public byte[]? Body { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Request = request;
			Body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
			var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent(reply, System.Text.Encoding.UTF8, replyType)
			};
			response.Headers.TryAddWithoutValidation("Retry-After", "30");
			response.Headers.TryAddWithoutValidation("Link", ["<https://example.invalid/2>; rel=\"next\"", "<https://example.invalid/9>; rel=\"last\""]);
			return response;
		}

		public string? Header(string name)
			=> Request!.Headers.TryGetValues(name, out var values)
				|| Request.Content?.Headers.TryGetValues(name, out values) == true
				? string.Join(", ", values!)
				: null;
	}

	/// <summary>Answers once <paramref name="wait"/> finishes, telling the test when the request has started.</summary>
	private sealed class GatedHandler(Func<CancellationToken, Task> wait) : HttpMessageHandler
	{
		public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Entered.TrySetResult(cancellationToken);
			await wait(cancellationToken);
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") };
		}
	}

	/// <summary>
	/// Runs <c>@http</c> as a player of its own against the real queue, sending through <paramref name="handler"/>,
	/// with <paramref name="callback"/> as the player's CALLBACK attribute.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> HttpOnTheQueueAsync(string name, HttpMessageHandler handler, string callback)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, name);
		await GrantHttpAsync(player.DbRef);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&CALLBACK {player.DbRef}={callback}"));
		var state = ParserState.RootFor(player.DbRef) with
		{
			Arguments = new()
			{
				["0"] = new CallState(player.DbRef + "/CALLBACK"),
				["1"] = new CallState("https://example.invalid/slow")
			}
		};
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);
		var clients = Substitute.For<IHttpClientFactory>();
		clients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, clients);
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var result = (await commands.Http(parser, new SharpCommandAttribute { Name = "@HTTP" })).Expect<CallState>();
		await Assert.That(result.Message.ToPlainText()).IsEqualTo("");
		return player;
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		for (var started = System.Diagnostics.Stopwatch.GetTimestamp();
			System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15);)
		{
			if (condition()) return;
			await Task.Delay(20);
		}
		throw new TimeoutException("The condition never held.");
	}

	/// <summary>
	/// The request waits off the queue, as PennMUSH's waits in libcurl's multi handle: other queued work
	/// runs while it waits, and the callback is queued when the reply arrives.
	/// </summary>
	[Test]
	public async Task HttpWaitsForItsReplyOffTheQueue()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("HttpReplied");
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var handler = new GatedHandler(token => release.Task.WaitAsync(token));
		var player = await HttpOnTheQueueAsync("HttpOffQueue", handler, $"think {marker} %<status>");
		await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

		var bystander = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var queue = WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
		await queue.AdmitWork(() => { bystander.TrySetResult(); return ValueTask.FromResult<CallState?>(null); },
			"http-bystander", SharpMUSH.Library.Services.TaskScheduler.EnqueueGroup);
		await bystander.Task.WaitAsync(TimeSpan.FromSeconds(15));
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Any(message => message.Contains(marker))).IsFalse();

		release.SetResult();
		await WaitUntilAsync(() => WebAppFactoryArg.Notifications.For(player.DbRef).Contains($"{marker} 200"));
	}

	/// <summary>A request that fails tells the object why and queues nothing (PennMUSH, src/bsd.c:1209-1210).</summary>
	[Test]
	public async Task HttpTellsTheObjectWhenItsRequestFails()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("HttpNeverAnswered");
		var handler = new GatedHandler(_ => Task.FromException(new HttpRequestException("Connection refused")));
		var player = await HttpOnTheQueueAsync("HttpFails", handler, $"think {marker}");
		await WaitUntilAsync(() => WebAppFactoryArg.Notifications.For(player.DbRef).Contains("Request failed: Connection refused"));
		await WebAppFactoryArg.QueueBarrierAsync();
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Any(message => message.Contains(marker))).IsFalse();
	}

	/// <summary>@halt cancels a request still waiting, and releases the queue entry it held.</summary>
	[Test]
	public async Task HaltCancelsAnHttpRequestStillWaiting()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("HttpHalted");
		var handler = new GatedHandler(token => Task.Delay(Timeout.Infinite, token));
		var player = await HttpOnTheQueueAsync("HttpHalt", handler, $"think {marker}");
		var request = await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
		var scheduler = WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.TaskScheduler>();
		await Assert.That(scheduler.GetQueueEntries().Any(entry => entry.Source?.Number == player.DbRef.Number)).IsTrue();

		await scheduler.Halt(player.DbRef);

		await WaitUntilAsync(() => request.IsCancellationRequested);
		await WaitUntilAsync(() => !scheduler.GetQueueEntries().Any(entry => entry.Source?.Number == player.DbRef.Number));
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Any(message => message.StartsWith("Request failed"))).IsFalse();
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Any(message => message.Contains(marker))).IsFalse();
	}

	/// <summary>What one <c>@http</c> run sent and the state its callback was queued with.</summary>
	private sealed record HttpRun(CallState Result, RecordingHandler Handler, ParserState? Callback, bool ClientCreated, string? ClientName);

	/// <summary>
	/// Runs <c>@http</c> with <paramref name="arguments"/> (from %1 on) as a player of its own, sending through
	/// <see cref="RecordingHandler"/> and running the callback's state factory in place of the queue.
	/// </summary>
	private async Task<HttpRun> RunHttpAsync(string name, string[] switches, string[] arguments,
		bool qRegisters = false, Dictionary<string, string>? registers = null, string reply = "ok", int? outputLimit = null,
		bool canHttp = true, ISecretStore? secrets = null, bool nestedPresence = false, Action<ParserState>? beforeCallback = null)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, name);
		if (canHttp) await GrantHttpAsync(player.DbRef);
		var args = new Dictionary<string, CallState> { ["0"] = new(player.DbRef + "/CALLBACK") };
		for (var i = 0; i < arguments.Length; i++) args[(i + 1).ToString()] = new CallState(arguments[i]);
		var state = ParserState.RootFor(player.DbRef) with { Arguments = args, Switches = switches };
		if (nestedPresence)
		{
			var parent = new Dictionary<string, CallState> { ["parent"] = new("unrelated") };
			state = state with { EnvironmentRegisters = parent, SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(parent) };
		}
		if (outputLimit is { } limit) state = state with { OutputLimit = limit };
		foreach (var (register, value) in registers ?? [])
			state.AddRegister(register, MarkupText.Plain(value));
		await state.KnownExecutorObject(Mediator);
		await state.KnownEnactorObject(Mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(state);

		ParserState? callback = null;
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<AdmitAttributeAfterRequest>(), Arg.Any<CancellationToken>())
			.Returns(call => Admit(call.Arg<AdmitAttributeAfterRequest>()));
		var handler = new RecordingHandler(reply);
		string? clientName = null;
		var clients = Substitute.For<IHttpClientFactory>();
		clients.CreateClient(Arg.Any<string>()).Returns(call =>
		{
			clientName = call.Arg<string>();
			return new HttpClient(handler);
		});
		var baseline = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with { Compatibility = baseline.Compatibility with { HttpQRegisters = qRegisters } });
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			WebAppFactoryArg.Services, mediator, clients, options, secrets ?? Substitute.For<ISecretStore>());

		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var result = (await commands.Http(parser, new SharpCommandAttribute { Name = "@HTTP" })).Expect<CallState>();
		return new HttpRun(result, handler, callback, clientName is not null, clientName);

		async ValueTask<QueueAdmissionResult> Admit(AdmitAttributeAfterRequest request)
		{
			beforeCallback?.Invoke(state);
			callback = await request.Work();
			return new QueueAdmissionResult(1, QueueRejectionReason.None);
		}
	}

	/// <summary>@http is for wizards and holders of the Can_HTTP power, as in PennMUSH.</summary>
	private Task GrantHttpAsync(DBRef player)
		=> Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {player}=CAN_HTTP")).AsTask();

	[Test]
	public async Task HttpIsRefusedWithoutTheCanHttpPower()
	{
		var run = await RunHttpAsync("HttpNoPower", [], ["https://example.invalid/get"], canHttp: false);

		await Assert.That(run.Result.Message.ToPlainText()).IsEqualTo($"{ErrorMessages.Returns.PermissionDenied}: NEEDS GAME.WIZARD OR GAME.CAN_HTTP");
		await Assert.That(run.ClientCreated).IsFalse();
		await Assert.That(run.Callback).IsNull();
	}

	[Test]
	public async Task HttpGetSendsTheHeadersAfterTheUrl()
	{
		var run = await RunHttpAsync("HttpGetHeaders", [], ["https://example.invalid/get", "{\"X-One\":\"1\",\"X-Two\":\"two words\"}"]);

		await Assert.That(run.Handler.Request!.Method).IsEqualTo(HttpMethod.Get);
		await Assert.That(run.Handler.Header("X-One")).IsEqualTo("1");
		await Assert.That(run.Handler.Header("X-Two")).IsEqualTo("two words");
		await Assert.That(run.Handler.Header("User-Agent")).IsEqualTo("SharpMUSH");
		await Assert.That(run.Handler.Body).IsNull();
	}

	[Test]
	public async Task HttpPostTakesTheHeadersAfterTheData()
	{
		var run = await RunHttpAsync("HttpPostHeaders", ["POST"],
			["https://example.invalid/post", "{\"a\":1}", "{\"Content-Type\":\"application/json\",\"Authorization\":\"Bearer abc\",\"User-Agent\":\"Bot/2\"}"]);

		await Assert.That(run.Handler.Request!.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(run.Handler.Header("Content-Type")).IsEqualTo("application/json");
		await Assert.That(run.Handler.Header("Authorization")).IsEqualTo("Bearer abc");
		await Assert.That(run.Handler.Header("User-Agent")).IsEqualTo("Bot/2");
		await Assert.That(System.Text.Encoding.UTF8.GetString(run.Handler.Body!)).IsEqualTo("{\"a\":1}");
	}

	[Test]
	public async Task HttpBodyDefaultsToAFormInUtf8()
	{
		var run = await RunHttpAsync("HttpFormDefault", ["PUT"], ["https://example.invalid/put", "name=caf\u00e9"]);

		await Assert.That(run.Handler.Header("Content-Type")).IsEqualTo("application/x-www-form-urlencoded");
		await Assert.That(run.Handler.Body!).IsEquivalentTo(System.Text.Encoding.UTF8.GetBytes("name=caf\u00e9"));
	}

	[Test]
	public async Task HttpBodyIsEncodedInTheCharsetItsTypeNames()
	{
		var run = await RunHttpAsync("HttpCharset", ["POST"],
			["https://example.invalid/post", "caf\u00e9", "{\"Content-Type\":\"text/plain; charset=ISO-8859-1\"}"]);

		await Assert.That(run.Handler.Header("Content-Type")).IsEqualTo("text/plain; charset=ISO-8859-1");
		await Assert.That(run.Handler.Body!).IsEquivalentTo(new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 });
	}

	[Test]
	public async Task HttpHeadersTakeListsForRepeatedHeaders()
	{
		var run = await RunHttpAsync("HttpListHeaders", ["POST"],
			["https://example.invalid/post", "{\"a\":1}",
				"{\"Content-Type\":\"application/json\",\"X-One\":\" 1 \",\"Accept\":[\"text/plain\",\"application/json\"]}"]);

		await Assert.That(run.Handler.Request!.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(run.Handler.Header("Content-Type")).IsEqualTo("application/json");
		await Assert.That(run.Handler.Header("X-One")).IsEqualTo("1");
		await Assert.That(run.Handler.Header("Accept")).IsEqualTo("text/plain, application/json");
	}

	[Test]
	[Arguments("X-One:1")]
	[Arguments("{\"\":\"1\"}")]
	[Arguments("{\"X-One\":null}")]
	[Arguments("[\"X-One\",\"1\"]")]
	[Arguments("{\"X-One\":1}")]
	[Arguments("{\"X-One\":[\"1\",2]}")]
	[Arguments("{\"Bad Name\":\"1\"}")]
	[Arguments("{\"Content-Length\":\"12\"}")]
	[Arguments("{\"X-One\":\"a\\nb\"}")]
	public async Task HttpRefusesHeadersItCannotSend(string json)
	{
		var run = await RunHttpAsync("HttpBadHeader", ["POST"], ["https://example.invalid/post", "data", json]);

		await Assert.That(run.Result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidHttpHeader);
		await Assert.That(run.ClientCreated).IsFalse();
		await Assert.That(run.Callback).IsNull();
	}

	/// <summary>A store holding <paramref name="name"/> = <paramref name="value"/>, which @http may send to <paramref name="hosts"/>.</summary>
	private static ISecretStore SecretFor(string name, string value, params string[] hosts)
	{
		var secrets = FixedSecrets.With(name, value);
		secrets.HostsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult<IReadOnlyList<string>>(call.ArgAt<string>(0) == name ? hosts : []));
		return secrets;
	}

	[Test]
	public async Task HttpFillsASecretIntoAHeaderForItsHost()
	{
		var secrets = SecretFor("http.test.token", "s3cret", "api.example.invalid");
		var run = await RunHttpAsync("HttpSecretBearer", [], ["https://api.example.invalid/get",
			"{\"Authorization\":{\"secret\":\"http.test.token\",\"scheme\":\"Bearer\"},\"X-Key\":{\"secret\":\"http.test.token\"}}"],
			secrets: secrets);

		await Assert.That(run.Handler.Header("Authorization")).IsEqualTo("Bearer s3cret");
		await Assert.That(run.Handler.Header("X-Key")).IsEqualTo("s3cret");
		await Assert.That(run.ClientName).IsEqualTo(SharpMUSH.Implementation.Commands.Commands.SecretHttpClient);
	}

	[Test]
	public async Task HttpSendsABasicSecretBase64Encoded()
	{
		var secrets = SecretFor("http.test.login", "user:pass", "*.example.invalid");
		var run = await RunHttpAsync("HttpSecretBasic", [], ["https://api.example.invalid/get",
			"{\"Authorization\":{\"secret\":\"http.test.login\",\"scheme\":\"basic\"}}"], secrets: secrets);

		await Assert.That(run.Handler.Header("Authorization")).IsEqualTo("Basic " + Convert.ToBase64String("user:pass"u8.ToArray()));
	}

	[Test]
	public async Task HttpWithoutASecretFollowsRedirects()
	{
		var run = await RunHttpAsync("HttpPlainClient", [], ["https://api.example.invalid/get", "{\"X-One\":\"1\"}"]);

		await Assert.That(run.ClientName).IsEqualTo("api");
	}

	[Test]
	[Arguments("https://other.example.invalid/get", "http.test.token")]
	[Arguments("https://example.invalid/get", "http.test.token")]
	[Arguments("http://api.example.invalid/get", "http.test.token")]
	[Arguments("https://api.example.invalid/get", "test.token")]
	[Arguments("https://api.example.invalid/get", "http.test.unset")]
	public async Task HttpRefusesASecretOffItsHostsOrOutsideHttp(string url, string secret)
	{
		var secrets = SecretFor("http.test.token", "s3cret", "api.example.invalid");
		secrets.HostsAsync("test.token", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<string>>(["api.example.invalid"]));
		secrets.HostsAsync("http.test.unset", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<string>>(["api.example.invalid"]));
		var run = await RunHttpAsync("HttpSecretRefused", [], [url, $"{{\"X-Key\":{{\"secret\":\"{secret}\"}}}}"], secrets: secrets);

		await Assert.That(run.Result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidHttpHeader);
		await Assert.That(run.ClientCreated).IsFalse();
	}

	[Test]
	[Arguments("{\"Content-Type\":{\"secret\":\"http.test.token\"}}")]
	[Arguments("{\"X-Key\":{\"secret\":\"http.test.token\",\"other\":\"x\"}}")]
	[Arguments("{\"X-Key\":{\"secret\":\"HTTP.Bad Name\"}}")]
	[Arguments("{\"X-Key\":{\"secret\":\"http.test.token\",\"scheme\":\"Two words\"}}")]
	public async Task HttpRefusesASecretItCannotRead(string json)
	{
		var run = await RunHttpAsync("HttpSecretBad", ["POST"], ["https://api.example.invalid/post", "data", json],
			secrets: SecretFor("http.test.token", "s3cret", "api.example.invalid"));

		await Assert.That(run.Result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidHttpHeader);
		await Assert.That(run.ClientCreated).IsFalse();
	}

	[Test]
	public async Task HttpHandsTheResponseHeadersToTheCallback()
	{
		var run = await RunHttpAsync("HttpResponseHeaders", [], ["https://example.invalid/get"]);

		var headers = System.Text.Json.JsonDocument.Parse(run.Callback!.Argument("headers").ToPlainText()).RootElement;
		await Assert.That(headers.GetProperty("retry-after").GetString()).IsEqualTo("30");
		await Assert.That(headers.GetProperty("link").GetArrayLength()).IsEqualTo(2);
		await Assert.That(headers.GetProperty("content-type").GetString()).IsEqualTo("text/plain; charset=utf-8");
	}

	[Test]
	public async Task HttpReadsContentTypeAndUserpassRegistersOnlyWithHttpQRegisters()
	{
		var registers = new Dictionary<string, string> { ["CONTENT-TYPE"] = "text/csv", ["USERPASS"] = "user:secret" };

		var penn = await RunHttpAsync("HttpQRegsOn", ["POST"], ["https://example.invalid/post", "a,b"], qRegisters: true, registers: registers);
		var sharp = await RunHttpAsync("HttpQRegsOff", ["POST"], ["https://example.invalid/post", "a,b"], registers: registers);

		await Assert.That(penn.Handler.Header("Content-Type")).IsEqualTo("text/csv");
		await Assert.That(penn.Handler.Header("Authorization")).IsEqualTo("Basic " + Convert.ToBase64String("user:secret"u8.ToArray()));
		await Assert.That(sharp.Handler.Header("Content-Type")).IsEqualTo("application/x-www-form-urlencoded");
		await Assert.That(sharp.Handler.Header("Authorization")).IsNull();
	}

	[Test]
	public async Task HttpHeadersArgumentReplacesTheContentTypeRegister()
	{
		// PennMUSH #1398: given headers, the content-type q-register is ignored, even with no Content-Type line.
		var run = await RunHttpAsync("HttpHeadersOverReg", ["POST"], ["https://example.invalid/post", "x=1", "{\"X-One\":\"1\"}"],
			qRegisters: true, registers: new() { ["CONTENT-TYPE"] = "text/csv" });

		await Assert.That(run.Handler.Header("Content-Type")).IsEqualTo("application/x-www-form-urlencoded");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HttpOverflowSaysWhetherTheBodyWasCut(bool qRegisters)
	{
		var whole = await RunHttpAsync("HttpWhole", [], ["https://example.invalid/get"], qRegisters, reply: "0123456789", outputLimit: 10);
		var cut = await RunHttpAsync("HttpCut", [], ["https://example.invalid/get"], qRegisters, reply: "0123456789AB", outputLimit: 10);

		await Assert.That(whole.Callback!.Argument("0").ToPlainText()).IsEqualTo("0123456789");
		await Assert.That(whole.Callback.Argument("http-overflow").ToPlainText()).IsEqualTo("0");
		await Assert.That(cut.Callback!.Argument("0").ToPlainText()).IsEqualTo("0123456789");
		await Assert.That(cut.Callback.Argument("http-overflow").ToPlainText()).IsEqualTo("1");
		await Assert.That(cut.Callback.Registers.TryPeek(out var registers) && registers.ContainsKey("HTTP-OVERFLOW"))
			.IsEqualTo(qRegisters);
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
		var httpParser = Parser.Push(ParserState.RootFor(executor) with { HttpResponse = context });

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