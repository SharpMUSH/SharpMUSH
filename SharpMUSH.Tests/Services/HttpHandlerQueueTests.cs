using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// An inbound HTTP request's <c>&lt;METHOD&gt;</c> attribute is a queue entry (#1184). PennMUSH's
/// <c>run_http_command</c> (<c>src/cque.c:1092</c>) builds an <c>@include #&lt;handler&gt;/&lt;METHOD&gt;</c>
/// entry and runs it from the main loop, so it never interleaves with softcode; here it used to run on
/// the request's own thread, beside whatever queue entry was part-way through.
/// </summary>
public class HttpHandlerQueueTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Evaluate(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	[Test]
	public async Task AHandlerRequestWaitsForTheQueueEntryAlreadyRunningAndSeesItsWrites()
	{
		var handler = await Evaluate($"create(HttpQueueHandler{Guid.NewGuid():N})");
		await Evaluate($"attrib_set({handler}/WITNESS,before)");
		await Evaluate($"attrib_set({handler}/GET,lit(think [get(me/WITNESS)]))");

		var scheduler = WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var running = await scheduler.AdmitWork(async () =>
		{
			started.TrySetResult();
			await release.Task;
			// Half-way through: the entry has written, and is not finished.
			await Parser.FunctionParse(MarkupText.Plain($"attrib_set({handler}/WITNESS,after)"));
			return null;
		}, "http-queue-test", "test");
		await Assert.That(running.Accepted).IsTrue();

		Task<Found<HttpHandlerResult>>? request = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			using (TestOptionsOverride.Scope(o => o with
			{
				Database = o.Database with { HttpHandler = (uint)DBRef.Parse(handler).Number, HttpRequestsPerSecond = 30 }
			}))
			{
				request = dispatcher.DispatchAsync("GET", "/queued", "", []).AsTask();
			}

			await Task.Delay(500);
			await Assert.That(request.IsCompleted).IsFalse()
				.Because("the handler command is a queue entry and the queue is busy; it cannot run beside the running entry");

			release.TrySetResult();
			var result = (await request.WaitAsync(TimeSpan.FromSeconds(10))).Expect<HttpHandlerResult>();

			await Assert.That(result.Status).IsEqualTo(200);
			await Assert.That(result.Body.TrimEnd()).IsEqualTo("after")
				.Because("it ran after the earlier entry finished, so it sees that entry's writes");
		}
		finally
		{
			release.TrySetResult();
			// Only to let the request finish before the next test; its outcome was asserted above.
			if (request is not null) await ((Task)request.WaitAsync(TimeSpan.FromSeconds(10))).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	/// <summary>
	/// An entry that leaves the queue without running — halted with <c>@halt/pid</c>, or dropped at
	/// shutdown — still answers the request rather than leaving it waiting on a token that may never fire.
	/// </summary>
	[Test]
	public async Task AHandlerRequestWhoseEntryIsHaltedBeforeItRunsIsAnswered()
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())
			.Returns(call =>
			{
				// Halted while it waited: released, never run.
				call.Arg<Action?>()?.Invoke();
				return ValueTask.FromResult(new QueueAdmissionResult(1, QueueRejectionReason.None));
			});
		var baseline = TestSharpMushOptions.Create();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with { Database = baseline.Database with { HttpHandler = 8, HttpRequestsPerSecond = 30 } });
		var dispatcher = new HttpHandlerCommandService(Substitute.For<Mediator.IMediator>(), Substitute.For<IAttributeService>(),
			Substitute.For<IMUSHCodeParser>(),
			new HttpOutputCapture(), Substitute.For<IEventService>(), scheduler, options, NullLogger<HttpHandlerCommandService>.Instance);

		var result = (await dispatcher.DispatchAsync("GET", "/halted", "", []).AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
			.Expect<HttpHandlerResult>();

		await Assert.That(result.Status).IsEqualTo(503);
		await Assert.That(result.Body).IsEqualTo(HttpHandlerCommandService.Halted);
		await scheduler.DidNotReceive().AdmitWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>());
	}
}
