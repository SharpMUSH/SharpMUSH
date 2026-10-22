using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Tests.Server;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Packages;

public class PackageLifecycleOutcomeTests
{
	private static SharpAttribute Attribute(int mode) => new("", "", "AINSTALL", [], null, "AINSTALL", null!, null!, null!)
	{
		Value = MarkupText.Plain(mode == 1 ? "" : "think hook"), IsUnreadable = mode == 4
	};
	private static (PackageLifecycleRunner Runner, IMUSHCodeParser Parser, IAttributeService Attributes,
		AnySharpObject Target, AnySharpObject God) Create(int mode = 5, Func<ValueTask<CallState?>>? parse = null, ILogger<PackageLifecycleRunner>? logger = null,
		Func<GetObjectNodeQuery, AnyOptionalSharpObject>? lookup = null)
	{
		var objects = new TestObjectFactory();
		var god = objects.CreatePlayer(1, "God");
		var target = objects.CreateThing(8, "Lifecycle target");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult(lookup is not null ? lookup(call.Arg<GetObjectNodeQuery>())
				: (AnyOptionalSharpObject)(call.Arg<GetObjectNodeQuery>().DBRef.Number == 1 ? god : target)));
		var attributes = Substitute.For<IAttributeService>();
		OptionalSharpAttributeOrError found = mode switch
		{
			0 => new None(),
			2 => new Error<string>("Synthetic denied read"),
			3 => Array.Empty<SharpAttribute>(),
			9 => new[] { Attribute(5) with { IsMetadataUnreadable = true }, Attribute(5) },
			_ => new[] { Attribute(mode) }
		};
		attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "AINSTALL", IAttributeService.AttributeMode.Read, false).Returns(found);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(ParserState.RootFor(god.Object().DBRef));
		parser.Push(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => parse is not null ? parse() : ValueTask.FromResult<CallState?>(mode switch
		{
			6 => new CallState("parse failure") { IsParseFailure = true, HadErrors = true },
			7 => new CallState("command failure") { HadErrors = true },
			8 => null,
			_ => CallState.Empty
		}));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var baseline = TestSharpMushOptions.Create();
		options.CurrentValue.Returns(baseline with { Limit = baseline.Limit with { QueueEntryCpuTime = 1000 } });
		return (new(mediator, attributes, parser, logger ?? NullLogger<PackageLifecycleRunner>.Instance, options), parser, attributes, target, god);
	}

	[Test]
	[Arguments(0, ObjectAttributeRunStatus.Absent)]
	[Arguments(1, ObjectAttributeRunStatus.Empty)]
	[Arguments(2, ObjectAttributeRunStatus.Unavailable)]
	[Arguments(3, ObjectAttributeRunStatus.Unavailable)]
	[Arguments(4, ObjectAttributeRunStatus.Unavailable)]
	[Arguments(5, ObjectAttributeRunStatus.Parsed)]
	[Arguments(6, ObjectAttributeRunStatus.ParseFailure)]
	[Arguments(7, ObjectAttributeRunStatus.CommandFailure)]
	[Arguments(8, ObjectAttributeRunStatus.NoVerdict)]
	[Arguments(9, ObjectAttributeRunStatus.Unavailable)]
	public async Task AttributeVerdictDistinguishesReadAndParseResults(int mode, ObjectAttributeRunStatus expected)
	{
		var (_, parser, attributes, target, god) = Create(mode);
		await Assert.That(await StartupAttributeRunner.RunObjectAttributeObservedAsync(parser, attributes, target, "AINSTALL", god)).IsEqualTo(expected);
		if (mode < 5 || mode == 9) await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
		else await parser.Received(1).CommandListParse(Arg.Any<MarkupText>());
	}

	[Test]
	[Arguments(0, PackageLifecycleOutcome.Absent)]
	[Arguments(1, PackageLifecycleOutcome.Empty)]
	[Arguments(2, PackageLifecycleOutcome.AttributeUnavailable)]
	[Arguments(4, PackageLifecycleOutcome.AttributeUnavailable)]
	[Arguments(5, PackageLifecycleOutcome.Parsed)]
	[Arguments(6, PackageLifecycleOutcome.ParseFailure)]
	[Arguments(7, PackageLifecycleOutcome.CommandFailure)]
	[Arguments(8, PackageLifecycleOutcome.NoVerdict)]
	[Arguments(9, PackageLifecycleOutcome.AttributeUnavailable)]
	public async Task LifecycleVerdictDoesNotTreatEveryReturnedTaskAsSuccessful(int mode, PackageLifecycleOutcome expected)
	{
		var (runner, _, _, _, _) = Create(mode);
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL")).IsEqualTo(expected);
	}

	[Test]
	public async Task ObservedDeferralIsNotReportedAsExecution()
	{
		var (runner, parser, _, _, _) = Create();
		var captured = await PackageLifecycleDeferral.CollectAsync(() => runner.RunObservedLifecycleAsync("#8", "AINSTALL"));
		await Assert.That(captured.Result).IsEqualTo(PackageLifecycleOutcome.Deferred);
		await Assert.That(captured.Hooks.Count).IsEqualTo(1);
		await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
	}

	[Test]
	public async Task ObservedExceptionIsReportedWhileLegacyCallStillCompletes()
	{
		var (runner, _, _, _, _) = Create(parse: () => throw new IOException("Synthetic lifecycle parser exception"));
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL")).IsEqualTo(PackageLifecycleOutcome.Failed);
		await runner.RunLifecycleAsync("#8", "AINSTALL");
	}

	[Test]
	public async Task DirectRequestCancellationReachesTheParserBudget()
	{
		using var request = new CancellationTokenSource();
		var (runner, _, _, _, _) = Create(parse: () =>
		{
			request.Cancel();
			ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult<CallState?>(CallState.Empty);
		});
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL", request.Token)).IsEqualTo(PackageLifecycleOutcome.Cancelled);
	}

	[Test]
	public async Task AnExpiredAmbientBudgetDoesNotBecomeAnOrdinaryParseFailure()
	{
		var (runner, _, _, _, _) = Create();
		using var budget = new ExecutionBudget(TimeSpan.Zero);
		using var scope = budget.Enter();
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL")).IsEqualTo(PackageLifecycleOutcome.ExecutionLimit);
	}

	[Test]
	[Arguments(0, false)]
	[Arguments(1, false)]
	[Arguments(2, false)]
	[Arguments(5, true)]
	[Arguments(6, true)]
	[Arguments(7, true)]
	[Arguments(8, true)]
	public async Task LegacyAttributeBoolStillMeansParseWasAttempted(int mode, bool expected)
	{
		var (_, parser, attributes, target, god) = Create(mode);
		await Assert.That(await StartupAttributeRunner.RunObjectAttributeAsync(parser, attributes, target, "AINSTALL", god)).IsEqualTo(expected);
	}
	[Test]
	public async Task DirectCancellableCallRetainsConfiguredDeadlineAndRestoresAmbientIdentity()
	{
		using var request = new CancellationTokenSource();
		var original = ExecutionBudget.Current;
		TimeSpan remaining = TimeSpan.MaxValue;
		var (runner, _, _, _, _) = Create(parse: () =>
		{
			remaining = ExecutionBudget.Current?.Remaining ?? TimeSpan.MaxValue;
			return ValueTask.FromResult<CallState?>(CallState.Empty);
		});
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL", request.Token)).IsEqualTo(PackageLifecycleOutcome.Parsed);
		await Assert.That(remaining <= TimeSpan.FromSeconds(1)).IsTrue();
		await Assert.That(ReferenceEquals(ExecutionBudget.Current, original)).IsTrue();
	}

	[Test]
	public async Task ReturnedParserFailurePreservesExpiredParentClassificationAndIdentity()
	{
		var clock = new ManualDeadlineClock();
		using var parent = new ExecutionBudget(TimeSpan.FromMinutes(1), CancellationToken.None, clock);
		using var scope = parent.Enter();
		using var request = new CancellationTokenSource();
		var childExpired = true;
		var (runner, _, _, _, _) = Create(parse: () =>
		{
			clock.Expire();
			childExpired = ExecutionBudget.Current?.IsExpired == true;
			return ValueTask.FromResult<CallState?>(new CallState(ExecutionBudget.Error) { HadErrors = true });
		});
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL", request.Token)).IsEqualTo(PackageLifecycleOutcome.ExecutionLimit);
		await Assert.That(childExpired).IsFalse().Because("the original timer fires before the copied child deadline");
		await Assert.That(ReferenceEquals(ExecutionBudget.Current, parent)).IsTrue();
	}

	[Test]
	public async Task LegacyCancellationStillLogsTheOriginalException()
	{
		var error = new OperationCanceledException("Synthetic parser cancellation");
		var log = new ExceptionLogger();
		var (runner, _, _, _, _) = Create(parse: () => throw error, logger: log);
		await runner.RunLifecycleAsync("#8", "AINSTALL");
		await Assert.That(log.Exceptions.Count == 1 && ReferenceEquals(log.Exceptions[0], error)).IsTrue();
	}

	[Test]
	public async Task LookupReturningMissingAfterCancellationIsNotReportedAsMissing()
	{
		using var request = new CancellationTokenSource();
		var (runner, parser, _, _, _) = Create(lookup: _ => { request.Cancel(); return new None(); });
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL", request.Token)).IsEqualTo(PackageLifecycleOutcome.Cancelled);
		await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
	}

	[Test]
	[Arguments(0, PackageLifecycleOutcome.InvalidTarget)]
	[Arguments(1, PackageLifecycleOutcome.MissingExecutor)]
	[Arguments(2, PackageLifecycleOutcome.MissingTarget)]
	public async Task MissingExecutionIdentitiesNeverRunTheParser(int mode, PackageLifecycleOutcome expected)
	{
		var god = new TestObjectFactory().CreatePlayer(1, "God");
		var (runner, parser, _, _, _) = Create(lookup: query =>
			mode == 1 || query.DBRef.Number != 1 ? new None() : (AnyOptionalSharpObject)god);
		await Assert.That(await runner.RunObservedLifecycleAsync(mode == 0 ? "not-a-dbref" : "#8", "AINSTALL")).IsEqualTo(expected);
		await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
	}

	[Test]
	public async Task AlreadyCancelledRequestDoesNotLookupOrRunAnAttribute()
	{
		using var request = new CancellationTokenSource();
		request.Cancel();
		var lookups = 0;
		var (runner, parser, _, _, _) = Create(lookup: _ => { lookups++; return new None(); });
		await Assert.That(await runner.RunObservedLifecycleAsync("#8", "AINSTALL", request.Token)).IsEqualTo(PackageLifecycleOutcome.Cancelled);
		await Assert.That(lookups).IsEqualTo(0);
		await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
	}

	private sealed class ExceptionLogger : ILogger<PackageLifecycleRunner>
	{
		internal List<Exception> Exceptions { get; } = [];
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (exception is not null) Exceptions.Add(exception);
		}
	}

	private sealed class ManualDeadlineClock : TimeProvider
	{
		private DeadlineTimer? _timer;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
			=> _timer = new DeadlineTimer(callback, state);
		internal void Expire() => (_timer ?? throw new InvalidOperationException("No owned deadline")).Fire();
		private sealed class DeadlineTimer(TimerCallback callback, object? state) : ITimer
		{
			private bool _disposed;
			internal void Fire() { if (!_disposed) callback(state); }
			public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
			public void Dispose() => _disposed = true;
			public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
		}
	}

}
