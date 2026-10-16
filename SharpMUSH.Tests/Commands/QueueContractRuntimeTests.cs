using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class QueueContractServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

/// <summary>Real queue/parser/database evidence, isolated so limits and latency measurements affect no other tests.</summary>
[NotInParallel(nameof(QueueContractRuntimeTests))]
public class QueueContractRuntimeTests
{
	[ClassDataSource<QueueContractServerFactory>(Shared = SharedType.PerTestSession)]
	public required QueueContractServerFactory Factory { get; init; }
	private ITaskScheduler Queue => Factory.Services.GetRequiredService<ITaskScheduler>();
	private ITaskQueueReader Reader => Factory.Services.GetRequiredService<ITaskQueueReader>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private TestIsolationHelpers.TestPlayer _actor = null!;
	private DBRef _worker;
	private IMUSHCodeParser ActorParser => Factory.CommandParserFor(_actor.DbRef, _actor.Handle);
	private async Task<string> Cmd(string text) => (await ActorParser.CommandParse(_actor.Handle, Connections, MString.Plain(text))).Message.ToPlainText();
	private async Task God(string text) => await Factory.CommandParser.CommandParse(1, Connections, MString.Plain(text));
	private async Task<string> Read(string attribute) => (await Factory.FunctionParser.FunctionParse(MString.Plain($"get({_worker}/{attribute})")))!.Message.ToPlainText();

	[Before(Test)]
	public async Task SetUp()
	{
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "QueueContract");
		_worker = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("Worker")}"));
		await Cmd($"&DONE {_worker}=0");
	}

	[After(Test)]
	public async Task CleanUp()
	{
		if (_worker.Number > 0) await God($"@halt {_worker}");
		if (_actor is not null) await Connections.Disconnect(_actor.Handle);
	}

	[Test]
	public async Task RetriggeredEntriesGetFreshInvocationBudgetsAndDoNotRetainSwitchFrames()
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { Limit = original.Limit with { FunctionInvocationLimit = 100 } });
		await Assert.That(options.CurrentValue.Limit.FunctionInvocationLimit).IsEqualTo(100u);
		try
		{
			await Cmd($"&WORK {_worker}=&DONE me=[inc(get(me/DONE))];think {string.Concat(Enumerable.Repeat("[add(1,1)]", 12))};@assert eq(slev(),0);@switch lt(get(me/DONE),20)=1,@trigger me/WORK");
			await Cmd($"@trigger {_worker}/WORK");
			await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
			await Assert.That(await Read("DONE")).IsEqualTo("20");
			await Assert.That(Reader.GetQueueEntries().Any(entry => entry.Source == _worker)).IsFalse();
		}
		finally { options.CurrentValue.Returns(original); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OneOversizedEntryStillHitsItsInvocationLimit(bool piped)
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		options.CurrentValue.Returns(original with { Limit = original.Limit with { FunctionInvocationLimit = 100 } });
		await Assert.That(options.CurrentValue.Limit.FunctionInvocationLimit).IsEqualTo(100u);
		try
		{
			await Cmd($"&WORK {_worker}=think {string.Concat(Enumerable.Repeat("[add(1,1)]", 120))};{(piped ? "|" : "")}&DONE me=1");
			await Cmd($"@trigger {_worker}/WORK");
			await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
			await Assert.That(await Read("DONE")).IsEqualTo("0");
			await Queue.AdmitCommandList(MString.Plain("&SECOND me=[add(1,2)]"), ParserState.RootFor(_worker));
			await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
			await Assert.That(await Read("SECOND")).IsEqualTo("3");
			// A piped first command captures its notice, but the failed invocation remains observable.
			await Assert.That(Factory.Services.GetRequiredService<QueueDiagnosticsRecorder>().Recent()
				.Any(entry => entry.Source == _worker && entry.Outcome == QueueOutcome.InvocationFailure && entry.FailedInvocations > 0)).IsTrue();
			if (!piped)
				await Assert.That(Factory.Notifications.For(_worker).Any(message => message.Contains("INVOCATION LIMIT", StringComparison.OrdinalIgnoreCase))).IsTrue();
		}
		finally { options.CurrentValue.Returns(original); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DeferredEntriesCaptureArgumentsRegistersAndIndependentLimits(bool semaphore)
	{
		var arguments = new Dictionary<string, CallState> { ["0"] = new("before") };
		var state = ParserState.RootFor(_worker).ForAttributeArguments(arguments);
		state.AddRegister("X", MString.Plain("before"));
		var body = MString.Plain("&RESULT me=%0|%qX|[add(1,2)]");
		var admission = semaphore
			? await Queue.AdmitCommandList(body, state, new DbRefAttribute(_worker, ["SEMAPHORE"]), 0, TimeSpan.FromHours(1), true)
			: await Queue.AdmitCommandList(body, state, TimeSpan.FromHours(1));
		await Assert.That(admission.Accepted).IsTrue();
		arguments["0"] = new("after");
		state.AddRegister("X", MString.Plain("after"));
		state.LimitExceeded!.IsExceeded = true;
		if (semaphore) await Cmd($"@notify {_worker}");
		else await Queue.ReleaseScheduledWork(admission.Pid!.Value);
		await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
		await Assert.That(await Read("RESULT")).IsEqualTo("before|before|3");
	}

	[Test]
	public async Task TriggerRefusalDoesNotRunTheBodyOrOwnQueueCapacity()
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		await Cmd($"&WORK {_worker}=&DONE me=1");
		options.CurrentValue.Returns(original with { Limit = original.Limit with { GlobalQueueLimit = 1 } });
		await Assert.That(options.CurrentValue.Limit.GlobalQueueLimit).IsEqualTo(1u);
		using var occupied = await Queue.ReserveCommandList(MString.Plain("think occupied"), ParserState.RootFor(_worker));
		await Assert.That(occupied.Admission.Accepted).IsTrue();
		try
		{
			await Assert.That(await Cmd($"@trigger {_worker}/WORK")).Contains("QUEUE REJECTED");
			await Assert.That(await Read("DONE")).IsEqualTo("0");
			await Assert.That(Reader.GetQueueEntries().Count(entry => entry.Source == _worker)).IsEqualTo(1);
		}
		finally { options.CurrentValue.Returns(original); }
	}

	[Test]
	public async Task AsyncAttributeFactoriesStartWithIndependentEvaluationCounters()
	{
		await Cmd($"&WORK {_worker}=&RESULT me=%0|[add(1,2)]");
		var state = ParserState.RootFor(_worker).ForAttributeArguments(new() { ["0"] = new("captured") });
		state.LimitExceeded!.IsExceeded = true;
		state.TotalInvocations!.Increment();
		var admission = await Queue.AdmitAsyncAttribute(() => ValueTask.FromResult(state), new DbRefAttribute(_worker, ["WORK"]), _worker);
		await Assert.That(admission.Accepted).IsTrue();
		await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
		await Assert.That(await Read("RESULT")).IsEqualTo("captured|3");
		await Assert.That(state.TotalInvocations.Count).IsEqualTo(1);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AStoppedPageProducerDoesNotClaimCompletion(bool refuseContinuation)
	{
		await Cmd($"&STATUS {_worker}=running");
		await Cmd($"&PAGE {_worker}=@dolist/inline lnum(1,8)={{&DONE me=[inc(get(me/DONE))]}};@assert lt(get(me/DONE),80)={{&STATUS me=complete}};@trigger me/PAGE");
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var original = options.CurrentValue;
		try
		{
			if (refuseContinuation)
			{
				// The running page occupies the only slot, so its continuation must be refused.
				options.CurrentValue.Returns(original with { Limit = original.Limit with { GlobalQueueLimit = 1 } });
				await Queue.AdmitCommandList(MString.Plain("@include me/PAGE"), ParserState.RootFor(_worker));
			}
			else
			{
				await Queue.AdmitWork(async () =>
				{
					await Queue.AdmitCommandList(MString.Plain("@include me/PAGE"), ParserState.RootFor(_worker));
					await Queue.AdmitWork(async () => { await God($"@halt {_worker}"); return CallState.Empty; }, "cancel between pages", "test");
					return CallState.Empty;
				}, "publish page and cancellation", "test");
			}
			await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
			await Assert.That(await Read("DONE")).IsEqualTo("8");
			await Assert.That(await Read("STATUS")).IsEqualTo("running");
			var history = Factory.Services.GetRequiredService<QueueDiagnosticsRecorder>().Recent().Where(entry => entry.Source == _worker).ToArray();
			await Assert.That(history.Count(entry => entry.Outcome == QueueOutcome.GlobalLimit)).IsEqualTo(refuseContinuation ? 1 : 0);
			await Assert.That(history.Count(entry => entry.Outcome == QueueOutcome.Cancelled)).IsEqualTo(refuseContinuation ? 0 : 1);
			await Assert.That(Reader.GetQueueEntries().Any(entry => entry.Source == _worker)).IsFalse();
		}
		finally { options.CurrentValue.Returns(original); }
	}

	[Test]
	public async Task PublishedPagingExampleCompletesExactlyOnce()
	{
		var guide = await File.ReadAllTextAsync(Path.Combine(TestPaths.RepositoryRoot, "docs/guides/bounded-queue-work.md"));
		var commands = guide.Split("```sharp\n")[1].Split("```")[0]
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Replace("<worker>", _worker.ToString())).ToArray();
		foreach (var command in commands[..^1]) await Cmd(command);
		// Both starts are admitted before either runs. The second must see the running guard.
		await Queue.AdmitWork(async () =>
		{
			await Cmd(commands[^1]);
			await Cmd(commands[^1]);
			return CallState.Empty;
		}, "publish duplicate starts", "test");
		await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
		await Assert.That(await Read("DONE")).IsEqualTo("80");
		await Assert.That(await Read("STATUS")).IsEqualTo("complete");
		for (var row = 1; row <= 80; row++)
			await Assert.That(await Read($"ROW`{row}")).IsEqualTo($"{row}|60");
		await Assert.That(Reader.GetQueueEntries().Any(entry => entry.Source == _worker)).IsFalse();
	}

	[Test]
	public async Task MeasureTwoActorBoundedProduction()
	{
		const int rows = 80;
		const int pageSize = 8;
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "QueueWitness");
		var limits = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit;
		Console.WriteLine($"QUEUE_SETTINGS invocation_limit={limits.FunctionInvocationLimit} queue_cpu_ms={limits.QueueEntryCpuTime} owner_limit={limits.PlayerQueueLimit} global_limit={limits.GlobalQueueLimit}");
		await Cmd($"&ROW {_worker}=&ROW`%0 me=%0|[words(iter(lnum(60),add(%i0,1)))];&DONE me=[inc(get(me/DONE))]");
		await Cmd($"&PAGE {_worker}=@dolist/inline lnum(%0,min(add(%0,{pageSize - 1}),{rows}))={{@include me/ROW=##}};@break gte(get(me/DONE),{rows});@trigger me/PAGE=add(%0,{pageSize})");
		// Warm parser, attribute and storage paths before comparing the three strategies.
		await Queue.AdmitCommandList(MString.Plain("@include me/ROW=warm"), ParserState.RootFor(_worker));
		await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
		try
		{
			foreach (var mode in new[] { "dolist", "inline", "pages" })
			{
				await Cmd($"&DONE {_worker}=0");
				var recorder = Factory.Services.GetRequiredService<QueueDiagnosticsRecorder>();
				var since = recorder.Recent().Select(entry => entry.Sequence).DefaultIfEmpty().Max();
				var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var witness = new TaskCompletionSource<(double Latency, int Pending)>(TaskCreationOptions.RunContinuationsAsynchronously);
				await Queue.AdmitWork(async () => { started.TrySetResult(); await release.Task; return CallState.Empty; }, "measurement gate", "test");
				await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
				var work = mode switch
				{
					"dolist" => $"@dolist lnum(1,{rows})={{@include me/ROW=##}}",
					"inline" => $"@dolist/inline lnum(1,{rows})={{@include me/ROW=##}}",
					_ => "@include me/PAGE=1"
				};
				var watch = new Stopwatch();
				var peak = 0;
				var finished = false;
				Task sampling = Task.CompletedTask;
				try
				{
					await Assert.That((await Queue.AdmitCommandList(MString.Plain(work), ParserState.RootFor(_worker))).Accepted).IsTrue();
					await Assert.That((await Queue.AdmitWork(async () =>
					{
						await Factory.CommandParserFor(other.DbRef, other.Handle).CommandListParse(MString.Plain("think second actor"));
						var pending = Reader.GetQueueEntries().Count(entry => entry.Source == _worker);
						witness.TrySetResult((watch.Elapsed.TotalMilliseconds, pending));
						return CallState.Empty;
					}, "measurement witness", "enqueue", other.DbRef)).Accepted).IsTrue();
					sampling = Sample();
					watch.Start();
					release.TrySetResult();
					var measured = await witness.Task.WaitAsync(TimeSpan.FromSeconds(30));
					await Queue.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
					watch.Stop();
					await Assert.That(await Read("DONE")).IsEqualTo(rows.ToString());
					var recorded = await Factory.FunctionParser.FunctionParse(MString.Plain($"iter(lnum(1,{rows}),get({_worker}/ROW`%i0))"));
					await Assert.That(recorded!.Message.ToPlainText()).IsEqualTo(string.Join(" ", Enumerable.Range(1, rows).Select(row => $"{row}|60")));
					await Assert.That(Reader.GetQueueEntries().Any(entry => entry.Source == _worker)).IsFalse();
					await Assert.That(measured.Pending).IsEqualTo(mode == "dolist" ? rows : mode == "pages" ? 1 : 0);
					var entries = recorder.Recent().Where(entry => entry.Source == _worker && entry.Sequence > since).ToArray();
					await Assert.That(entries.Length).IsEqualTo(mode == "dolist" ? rows + 1 : mode == "pages" ? rows / pageSize : 1);
					await Assert.That(entries.All(entry => entry.Outcome == QueueOutcome.Completed)).IsTrue();
					var costs = entries.Select(entry => entry.ExecutionDuration!.Value.TotalMilliseconds).ToArray();
					Console.WriteLine($"QUEUE_ENTRIES mode={mode} completed={entries.Length} refused=0 cancelled=0 mean_execution_ms={costs.Average():F3} max_execution_ms={costs.Max():F3}");
					Console.WriteLine($"QUEUE_METRICS mode={mode} rows={rows} page={pageSize} second_actor_ms={measured.Latency:F3} peak_admitted={Math.Max(peak, measured.Pending)} total_ms={watch.Elapsed.TotalMilliseconds:F3} rows_per_second={rows / watch.Elapsed.TotalSeconds:F1}");
				}
				finally
				{
					release.TrySetResult();
					finished = true;
					await sampling;
				}
				async Task Sample()
				{
					while (!Volatile.Read(ref finished))
					{
						peak = Math.Max(peak, Reader.GetQueueEntries().Count(entry => entry.Source == _worker));
						await Task.Delay(1);
					}
				}
			}
		}
		finally { await Connections.Disconnect(other.Handle); }
	}
}
