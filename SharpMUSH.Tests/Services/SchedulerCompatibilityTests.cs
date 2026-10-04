using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Models.SchedulerModels;
using System.Reflection;
using System.Reflection.Emit;
using Mediator;
using Microsoft.Extensions.Logging;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class SchedulerCompatibilityTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PublishedTwoArgumentReleaseSlotDispatchesCompiledCalls(bool concrete)
	{
		var target = concrete ? typeof(Scheduler) : typeof(ITaskScheduler);
		var method = target.GetMethod("ReleaseScheduledWork", [typeof(long), typeof(bool)]);
		await Assert.That(method?.ReturnType).IsEqualTo(typeof(ValueTask<QueueAdmissionResult>));
		var factory = Substitute.For<ISchedulerFactory>();
		await using var scheduler = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			factory, Substitute.For<IAttributeService>(), Substitute.For<IMediator>(), NullLogger<Scheduler>.Instance);
		var caller = new DynamicMethod("PublishedReleaseCaller", typeof(ValueTask<QueueAdmissionResult>), [typeof(object)]);
		var il = caller.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Castclass, target);
		il.Emit(OpCodes.Ldc_I8, 123L);
		il.Emit(OpCodes.Ldc_I4_0);
		il.Emit(OpCodes.Callvirt, method!);
		il.Emit(OpCodes.Ret);
		var result = await caller.CreateDelegate<Func<object, ValueTask<QueueAdmissionResult>>>()(scheduler);
		await Assert.That(result.Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		var generated = target.GetMethod("ReleaseScheduledWork", [typeof(long), typeof(bool), typeof(long?)]);
		await Assert.That(generated!.GetParameters()[2].IsOptional).IsFalse();
	}

	[Test]
	public async Task PublishedQueueRequestsRetainUnitContractsAndConstructors()
	{
		(Type Type, Type[] Parameters)[] requests =
		[
			(typeof(QueueCommandListRequest), [typeof(MarkupText), typeof(ParserState), typeof(DbRefAttribute), typeof(int)]),
			(typeof(QueueAttributeRequest), [typeof(Func<ValueTask<ParserState>>), typeof(DbRefAttribute)]),
			(typeof(QueueDelayedCommandListRequest), [typeof(MarkupText), typeof(ParserState), typeof(TimeSpan)]),
			(typeof(QueueCommandListWithTimeoutRequest), [typeof(MarkupText), typeof(ParserState), typeof(DbRefAttribute), typeof(int), typeof(TimeSpan)]),
			(typeof(NotifySemaphoreRequest), [typeof(DbRefAttribute), typeof(int), typeof(int)]),
			(typeof(NotifyAllSemaphoreRequest), [typeof(DbRefAttribute)])
		];
		foreach (var (type, parameters) in requests)
		{
			await Assert.That(typeof(IRequest).IsAssignableFrom(type)).IsTrue();
			await Assert.That(type.GetConstructor(parameters)).IsNotNull();
			await Assert.That(type.GetMethod("Deconstruct", parameters.Select(parameter => parameter.MakeByRefType()).ToArray())).IsNotNull();
		}
	}
}
