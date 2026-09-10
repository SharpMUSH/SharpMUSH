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
	private static readonly (string Name, Type[] Parameters)[] LegacyWrites =
	[
		("WriteUserCommand", [typeof(long), typeof(MarkupText), typeof(ParserState)]),
		("WriteCommandList", [typeof(MarkupText), typeof(ParserState)]),
		("WriteCommandList", [typeof(MarkupText), typeof(ParserState), typeof(DbRefAttribute), typeof(int)]),
		("WriteCommandList", [typeof(MarkupText), typeof(ParserState), typeof(DbRefAttribute), typeof(int), typeof(TimeSpan)]),
		("WriteCommandList", [typeof(MarkupText), typeof(ParserState), typeof(TimeSpan)]),
		("WriteAsyncAttribute", [typeof(Func<ValueTask<ParserState>>), typeof(DbRefAttribute)]),
		("EnqueueWork", [typeof(Func<ValueTask<CallState?>>), typeof(string), typeof(string)]),
		("Notify", [typeof(DbRefAttribute), typeof(int), typeof(int)]),
		("NotifyAll", [typeof(DbRefAttribute)])
	];

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PublishedSchedulerWriteSlotsKeepTheirExactSignatures(bool concrete)
	{
		var type = concrete ? typeof(Scheduler) : typeof(ITaskScheduler);
		foreach (var (name, parameters) in LegacyWrites)
			await Assert.That(type.GetMethod(name, parameters)?.ReturnType).IsEqualTo(typeof(ValueTask))
				.Because($"compiled callers bind to the original {type.Name}.{name} parameter and return types");
	}

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
	public async Task PublishedSchedulerConstructorRemainsAvailable()
	{
		await Assert.That(typeof(Scheduler).GetConstructor([typeof(IMUSHCodeParser), typeof(IConnectionService),
			typeof(ISchedulerFactory), typeof(IAttributeService), typeof(IMediator), typeof(ILogger<Scheduler>)])).IsNotNull();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task QueueBudgetAndDiagnosticsConstructorsDispatchCompiledCalls(bool withDiagnostics)
	{
		Type[] parameters = [typeof(IMUSHCodeParser), typeof(IConnectionService), typeof(ISchedulerFactory),
			typeof(IAttributeService), typeof(IMediator), typeof(ILogger<Scheduler>),
			typeof(IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>), typeof(INotifyService)];
		if (withDiagnostics) parameters = [.. parameters, typeof(SharpMUSH.Library.Services.IQueueDiagnosticsRecorder)];
		var constructor = typeof(Scheduler).GetConstructor(parameters);
		await Assert.That(constructor).IsNotNull();
		var caller = new DynamicMethod("PublishedSchedulerConstructor", typeof(Scheduler), [typeof(object[])]);
		var il = caller.GetILGenerator();
		for (var i = 0; i < parameters.Length; i++)
		{
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldc_I4, i);
			il.Emit(OpCodes.Ldelem_Ref);
			il.Emit(OpCodes.Castclass, parameters[i]);
		}
		il.Emit(OpCodes.Newobj, constructor!);
		il.Emit(OpCodes.Ret);
		object?[] arguments = [Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(), Substitute.For<IMediator>(),
			NullLogger<Scheduler>.Instance, null, null];
		if (withDiagnostics) arguments = [.. arguments, null];
		await using var scheduler = caller.CreateDelegate<Func<object?[], Scheduler>>()(arguments);
		await Assert.That(scheduler).IsNotNull();
	}

	[Test]
	public async Task LegacySchedulerImplementationLoadsAndDispatchesWithoutImplementingNewFeatures()
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LegacyScheduler" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
		var type = assembly.DefineDynamicModule("legacy").DefineType("LegacyImplementation", TypeAttributes.Public);
		type.AddInterfaceImplementation(typeof(ITaskScheduler));
		type.DefineDefaultConstructor(MethodAttributes.Public);
		foreach (var (name, parameters) in LegacyWrites) DefineDefaultMethod(type, name, typeof(ValueTask), parameters);
		string[] unchanged = ["GetAllTasks", "GetSemaphoreTasks", "GetDelayTasks", "GetEnqueueTasks", "ModifyQRegisters", "Drain", "Halt", "RescheduleSemaphoreTask", "HaltByPid"];
		foreach (var method in typeof(ITaskScheduler).GetMethods().Where(method => unchanged.Contains(method.Name)))
			DefineDefaultMethod(type, method.Name, method.ReturnType, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
		var instance = Activator.CreateInstance(type.CreateType()!)!;
		var caller = new DynamicMethod("LegacyWriteCaller", typeof(ValueTask), [typeof(ITaskScheduler)]);
		var il = caller.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		EmitDefault(il, typeof(MarkupText));
		EmitDefault(il, typeof(ParserState));
		il.Emit(OpCodes.Callvirt, typeof(ITaskScheduler).GetMethod("WriteCommandList", [typeof(MarkupText), typeof(ParserState)])!);
		il.Emit(OpCodes.Ret);
		await caller.CreateDelegate<Func<ITaskScheduler, ValueTask>>()((ITaskScheduler)instance);
		await Assert.That(instance).IsAssignableTo<ITaskScheduler>();
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

	private static void DefineDefaultMethod(TypeBuilder type, string name, Type result, Type[] parameters)
	{
		var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final, result, parameters);
		var il = method.GetILGenerator();
		EmitDefault(il, result);
		il.Emit(OpCodes.Ret);
	}

	private static void EmitDefault(ILGenerator il, Type type)
	{
		if (!type.IsValueType) { il.Emit(OpCodes.Ldnull); return; }
		var local = il.DeclareLocal(type);
		il.Emit(OpCodes.Ldloca, local);
		il.Emit(OpCodes.Initobj, type);
		il.Emit(OpCodes.Ldloc, local);
	}
}
