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
	public async Task PublishedSchedulerConstructorRemainsAvailable()
	{
		await Assert.That(typeof(Scheduler).GetConstructor([typeof(IMUSHCodeParser), typeof(IConnectionService),
			typeof(ISchedulerFactory), typeof(IAttributeService), typeof(IMediator), typeof(ILogger<Scheduler>)])).IsNotNull();
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
