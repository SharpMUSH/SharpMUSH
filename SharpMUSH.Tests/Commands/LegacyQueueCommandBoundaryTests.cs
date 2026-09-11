using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Commands;

public class LegacyQueueCommandBoundaryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("ProcessStatus", "")]
	[Arguments("ProcessStatus", "DEBUG")]
	[Arguments("ProcessStatus", "ALL")]
	[Arguments("ProcessStatus", "SEMAPHORE")]
	[Arguments("Halt", "PID")]
	[Arguments("Wait", "PID")]
	public async Task UnsupportedSnapshotsDoNotBreakLegacyCommandBoundaries(string method, string commandSwitch)
	{
		var (commands, parser, scheduler, mediator) = Create(method, commandSwitch);
		scheduler.GetQueueEntry(Arg.Any<long>()).Returns(_ => throw new NotSupportedException());
		scheduler.GetQueueEntries().Returns(_ => throw new NotSupportedException());
		scheduler.GetQueueUsage().Returns(_ => throw new NotSupportedException());
		var result = (await Invoke(commands, parser, method)).Expect<CallState>();
		await Assert.That(result.Message?.ToPlainText() ?? "").IsEqualTo(
			method == "ProcessStatus" && commandSwitch is not ("DEBUG" or "SEMAPHORE") ? "" : ErrorMessages.Returns.ErrorNotSupported);
		var expectedKey = method == "ProcessStatus" && commandSwitch is not ("DEBUG" or "SEMAPHORE")
			? nameof(ErrorMessages.Notifications.PsQueueForTargetFormat)
			: nameof(ErrorMessages.Notifications.NotSupportedForSharpMUSH);
		if (commandSwitch == "ALL") expectedKey = nameof(ErrorMessages.Notifications.PsAllHeader);
		await Assert.That(parser.ServiceProvider.GetRequiredService<INotifyService>().ReceivedCalls()
			.Any(call => call.GetMethodInfo().Name == "NotifyLocalized" && Equals(call.GetArguments()[1], expectedKey))).IsTrue();
		if (commandSwitch == "ALL")
		{
			mediator.DidNotReceive().CreateStream(Arg.Any<ScheduleSemaphoreQuery>(), Arg.Any<CancellationToken>());
			scheduler.DidNotReceive().GetQueueEntry(Arg.Any<long>());
		}
		if (commandSwitch == "SEMAPHORE")
			await Assert.That(parser.ServiceProvider.GetRequiredService<INotifyService>().ReceivedCalls()
				.Any(call => call.GetMethodInfo().Name == "NotifyLocalized"
					&& Equals(call.GetArguments()[1], nameof(ErrorMessages.Notifications.PsSemaphoreTaskEntryFormat)))).IsFalse();
		await mediator.DidNotReceive().Send(Arg.Any<HaltByPidRequest>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments("unsupported", ErrorMessages.Returns.ErrorNotSupported)]
	[Arguments("absent", ErrorMessages.Returns.NoSuchPid)]
	[Arguments("denied", ErrorMessages.Returns.PermissionDenied)]
	public async Task PidInfoPreservesLegacyInspectionBoundaries(string behavior, string expected)
	{
		var (_, parser, scheduler, mediator) = Create("PIDInfo", "SEMAPHORE");
		if (behavior == "absent") mediator.CreateStream(Arg.Any<ScheduleSemaphoreQuery>(), Arg.Any<CancellationToken>())
			.Returns(AsyncEnumerable.Empty<SemaphoreTaskData>());
		scheduler.GetQueueEntry(42).Returns(_ => behavior == "unsupported" ? throw new NotSupportedException() : null);
		var functions = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Functions.Functions>(Factory.Services, mediator);
		var result = await functions.PIDInfo(parser, new SharpFunctionAttribute { Name = "pidinfo", Flags = FunctionFlags.Regular });
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("LIST")]
	[Arguments("PAUSE")]
	[Arguments("RESUME")]
	public async Task QueueControlReportsUnsupportedInspection(string commandSwitch)
	{
		var (commands, parser, scheduler, _) = Create("QueueControl", commandSwitch);
		scheduler.GetQueueEntries().Returns(_ => throw new NotSupportedException());
		var metadata = typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("QueueControl")!
			.GetCustomAttributes(typeof(SharpCommandAttribute), false).Cast<SharpCommandAttribute>().Single();
		var result = (await commands.QueueControl(parser, metadata)).Expect<CallState>();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.ErrorNotSupported);
	}

	[Test]
	public async Task AbsentPidCannotBecomeAnUnauthorizedHaltAfterTheLookup()
	{
		var (commands, parser, scheduler, mediator) = Create("Halt", "PID");
		var admittedAfterLookup = false;
		scheduler.GetQueueEntry(42).Returns(_ =>
		{
			admittedAfterLookup = true;
			return null;
		});
		var victimHalted = false;
		mediator.Send(Arg.Any<HaltByPidRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			victimHalted = admittedAfterLookup;
			return ValueTask.FromResult(victimHalted);
		});
		var result = (await Invoke(commands, parser, "Halt")).Expect<CallState>();
		await Assert.That(admittedAfterLookup).IsTrue();
		await Assert.That(victimHalted).IsFalse();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NotFound);
		await mediator.DidNotReceive().Send(Arg.Any<HaltByPidRequest>(), Arg.Any<CancellationToken>());
	}

	private (SharpMUSH.Implementation.Commands.Commands Commands, IMUSHCodeParser Parser, ITaskScheduler Scheduler, IMediator Mediator)
		Create(string method, string commandSwitch)
	{
		var actor = new TestObjectFactory().CreatePlayer(commandSwitch == "ALL" ? 1 : 40, "Queue actor");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(actor));
		mediator.CreateStream(Arg.Any<ScheduleSemaphoreQuery>(), Arg.Any<CancellationToken>()).Returns(commandSwitch is "SEMAPHORE" or "ALL"
			? new[] { new SemaphoreTaskData(42, MarkupText.Plain("private command"), actor.Object().DBRef,
				new DbRefAttribute(actor.Object().DBRef, ["SEMAPHORE"]), null) }.ToAsyncEnumerable()
			: AsyncEnumerable.Empty<SemaphoreTaskData>());
		mediator.CreateStream(Arg.Any<ScheduleDelayQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<long>());
		mediator.CreateStream(Arg.Any<ScheduleEnqueueQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<long>());
		mediator.CreateStream(Arg.Any<ScheduleAllTasksQuery>(), Arg.Any<CancellationToken>())
			.Returns(AsyncEnumerable.Empty<(string, (DateTimeOffset, NameOrDbRef)[])>());
		var permissions = Substitute.For<IPermissionService>();
		permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);
		var scheduler = Substitute.For<ITaskScheduler>();
		var capabilities = Substitute.For<SharpMUSH.Library.Authorization.IAdministrativeCapabilityService>();
		var capabilityActor = new SharpMUSH.Library.Authorization.CapabilityActor("legacy-test", actor.Object().DBRef, actor.Object().DBRef);
		capabilities.GetGameActorAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(capabilityActor);
		capabilities.GetGrantedScopesAsync(capabilityActor, Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
		var controls = new QueueControlService(scheduler, capabilities, mediator, permissions);
		var notifications = Substitute.For<INotifyService>();
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(ITaskScheduler)
			? scheduler : call.Arg<Type>() == typeof(SharpMUSH.Library.Authorization.IAdministrativeCapabilityService) ? capabilities : call.Arg<Type>() == typeof(INotifyService) ? notifications
			: call.Arg<Type>() == typeof(IQueueControlService) ? controls : Factory.Services.GetService(call.Arg<Type>()));
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(provider);
		parser.CurrentState.Returns(ParserState.RootFor(actor.Object().DBRef) with
		{
			Switches = string.IsNullOrEmpty(commandSwitch) ? [] : [commandSwitch],
			Arguments = method == "QueueControl" ? commandSwitch == "LIST" ? new() : new() { ["0"] = new("42") } : method == "ProcessStatus" && commandSwitch != "DEBUG" ? new() : new()
			{
				["0"] = new("42"), ["1"] = new(method == "QueueControl" ? "" : "30")
			}
		});
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services,
			mediator, permissions, notifications);
		return (commands, parser, scheduler, mediator);
	}

	private static ValueTask<Option<CallState>> Invoke(SharpMUSH.Implementation.Commands.Commands commands,
		IMUSHCodeParser parser, string method)
	{
		var metadata = typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod(method)!
			.GetCustomAttributes(typeof(SharpCommandAttribute), false).Cast<SharpCommandAttribute>().Single();
		return method switch
		{
			"Halt" => commands.Halt(parser, metadata),
			"Wait" => commands.Wait(parser, metadata),
			_ => commands.ProcessStatus(parser, metadata)
		};
	}
}
