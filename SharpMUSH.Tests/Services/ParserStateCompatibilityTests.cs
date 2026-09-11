using System.Collections.Concurrent;
using System.Reflection.Emit;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using MarkupString;

namespace SharpMUSH.Tests.Services;

public class ParserStateCompatibilityTests
{
	// Published positional contract at 4dc394dec, before queue execution budgets.
	private static readonly (string Name, Type Type)[] PublishedParameters =
	[
		("Registers", typeof(ConcurrentStack<Dictionary<string, MarkupText>>)),
		("IterationRegisters", typeof(ConcurrentStack<IterationWrapper<MarkupText>>)),
		("RegexRegisters", typeof(ConcurrentStack<Dictionary<string, MarkupText>>)),
		("SwitchStack", typeof(ConcurrentStack<MarkupText>)),
		("ExecutionStack", typeof(ConcurrentStack<Execution>)),
		("EnvironmentRegisters", typeof(Dictionary<string, CallState>)),
		("CurrentEvaluation", typeof(DBAttribute)),
		("ParserFunctionDepth", typeof(int?)),
		("Function", typeof(string)),
		("Command", typeof(string)),
		("CommandInvoker", typeof(Func<IMUSHCodeParser, ValueTask<Option<CallState>>>)),
		("Switches", typeof(IEnumerable<string>)),
		("Arguments", typeof(Dictionary<string, CallState>)),
		("Executor", typeof(DBRef?)),
		("Enactor", typeof(DBRef?)),
		("Caller", typeof(DBRef?)),
		("Handle", typeof(long?)),
		("ParseMode", typeof(ParseMode)),
		("HttpResponse", typeof(HttpResponseContext)),
		("CallDepth", typeof(InvocationCounter)),
		("FunctionRecursionDepths", typeof(Dictionary<string, int>)),
		("TotalInvocations", typeof(InvocationCounter)),
		("LimitExceeded", typeof(LimitExceededFlag)),
		("CommandHistory", typeof(ConcurrentStack<(Func<IMUSHCodeParser, ValueTask<Option<CallState>>>, Dictionary<string, CallState>)>)),
		("Flags", typeof(ParserStateFlags)),
		("CallerArguments", typeof(Dictionary<string, CallState>)),
		("BreakPropagation", typeof(BreakPropagation)),
		("ConnectionSessionId", typeof(string))
	];

	[Test]
	public async Task PublishedConstructorSupportsDirectPluginCall()
	{
		var constructor = typeof(ParserState).GetConstructor(PublishedParameters.Select(x => x.Type).ToArray());
		await Assert.That(constructor).IsNotNull();
		var factory = new DynamicMethod("LegacyPluginCreateState", typeof(ParserState), [typeof(object[])]);
		var il = factory.GetILGenerator();
		for (var i = 0; i < PublishedParameters.Length; i++)
		{
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldc_I4, i);
			il.Emit(OpCodes.Ldelem_Ref);
			il.Emit(OpCodes.Unbox_Any, PublishedParameters[i].Type);
		}
		il.Emit(OpCodes.Newobj, constructor!);
		il.Emit(OpCodes.Ret);
		var source = ParserState.RootFor(new DBRef(42, 7)) with { Command = "legacy", ConnectionSessionId = "session" };
		var arguments = PublishedParameters.Select(x => typeof(ParserState).GetProperty(x.Name)!.GetValue(source)).ToArray();
		var result = factory.CreateDelegate<Func<object?[], ParserState>>()(arguments);
		await Assert.That(result.Executor).IsEqualTo(source.Executor);
		await Assert.That(result.Command).IsEqualTo("legacy");
		await Assert.That(result.ConnectionSessionId).IsEqualTo("session");
		await Assert.That(ReferenceEquals(result.Registers, source.Registers)).IsTrue();
		await Assert.That(result.ExecutionBudget).IsNull();
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(1));
		var budgeted = result with { ExecutionBudget = budget };
		await Assert.That(ReferenceEquals((budgeted with { Command = "nested" }).ExecutionBudget, budget)).IsTrue();
	}

	[Test]
	public async Task PublishedDeconstructionSupportsDirectPluginCall()
	{
		var deconstruct = typeof(ParserState).GetMethod("Deconstruct", PublishedParameters.Select(x => x.Type.MakeByRefType()).ToArray());
		await Assert.That(deconstruct).IsNotNull();
		var unpack = new DynamicMethod("LegacyPluginUnpackState", typeof(void), [typeof(ParserState), typeof(object[])]);
		var il = unpack.GetILGenerator();
		var locals = PublishedParameters.Select(x => il.DeclareLocal(x.Type)).ToArray();
		il.Emit(OpCodes.Ldarg_0);
		foreach (var local in locals) il.Emit(OpCodes.Ldloca, local);
		il.Emit(OpCodes.Callvirt, deconstruct!);
		for (var i = 0; i < locals.Length; i++)
		{
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldc_I4, i);
			il.Emit(OpCodes.Ldloc, locals[i]);
			if (PublishedParameters[i].Type.IsValueType) il.Emit(OpCodes.Box, PublishedParameters[i].Type);
			il.Emit(OpCodes.Stelem_Ref);
		}
		il.Emit(OpCodes.Ret);
		var source = ParserState.RootFor(new DBRef(42, 7)) with { Command = "legacy", ConnectionSessionId = "session" };
		var results = new object?[PublishedParameters.Length];
		unpack.CreateDelegate<Action<ParserState, object?[]>>()(source, results);
		for (var i = 0; i < results.Length; i++)
			await Assert.That(results[i]).IsEqualTo(typeof(ParserState).GetProperty(PublishedParameters[i].Name)!.GetValue(source));
	}
}
