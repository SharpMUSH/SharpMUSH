using System.Reflection.Emit;
using System.Text.Json;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Tests.Services;

public class QueueSnapshotCompatibilityTests
{
	private static readonly (string Name, Type Type)[] PublishedParameters =
	[
		("Pid", typeof(long)), ("Source", typeof(DBRef?)), ("Owner", typeof(DBRef?)),
		("Kind", typeof(string)), ("State", typeof(QueueEntryState)), ("RemainingDelay", typeof(TimeSpan?)),
		("PauseReason", typeof(string)), ("ReleasePending", typeof(bool))
	];

	private static QueueEntrySnapshot Example() => new(42, new DBRef(2, 7), new DBRef(1, 3), "delay",
		QueueEntryState.Paused, TimeSpan.FromSeconds(15), "maintenance", true);

	[Test]
	public async Task PublishedEightArgumentConstructorSupportsCompiledPluginCall()
	{
		var constructor = typeof(QueueEntrySnapshot).GetConstructor(PublishedParameters.Select(x => x.Type).ToArray());
		await Assert.That(constructor).IsNotNull();
		var factory = new DynamicMethod("LegacyQueueSnapshotConstructor", typeof(QueueEntrySnapshot), [typeof(object[])]);
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
		var source = Example();
		var values = PublishedParameters.Select(x => typeof(QueueEntrySnapshot).GetProperty(x.Name)!.GetValue(source)).ToArray();
		var result = factory.CreateDelegate<Func<object?[], QueueEntrySnapshot>>()(values);
		await Assert.That(result).IsEqualTo(source);
		await Assert.That(result.EnqueuedAt).IsNull();
		await Assert.That(result.StartedAt).IsNull();
		await Assert.That(result.WaitDuration).IsNull();
		await Assert.That(result.ExecutionDuration).IsNull();
		await Assert.That(result.InvocationCount).IsNull();
		await Assert.That(result.SourceAttribute).IsNull();
	}

	[Test]
	public async Task PublishedEightFieldDeconstructionSupportsCompiledPluginCall()
	{
		var deconstruct = typeof(QueueEntrySnapshot).GetMethod("Deconstruct", PublishedParameters.Select(x => x.Type.MakeByRefType()).ToArray());
		await Assert.That(deconstruct).IsNotNull();
		var unpack = new DynamicMethod("LegacyQueueSnapshotDeconstruct", typeof(void), [typeof(QueueEntrySnapshot), typeof(object[])]);
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
		var source = Example();
		var values = new object?[PublishedParameters.Length];
		unpack.CreateDelegate<Action<QueueEntrySnapshot, object?[]>>()(source, values);
		for (var i = 0; i < values.Length; i++)
			await Assert.That(values[i]).IsEqualTo(typeof(QueueEntrySnapshot).GetProperty(PublishedParameters[i].Name)!.GetValue(source));
	}

	[Test]
	public async Task DiagnosticsSurviveJsonAndRecordCopyWithoutChangingPositionalFields()
	{
		var source = Example() with
		{
			Source = null,
			Owner = null,
			EnqueuedAt = DateTimeOffset.UnixEpoch,
			StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(3),
			WaitDuration = TimeSpan.FromSeconds(3),
			ExecutionDuration = TimeSpan.FromSeconds(2),
			InvocationCount = 7,
			SourceAttribute = "ACTION"
		};
		var copy = source with { State = QueueEntryState.Running };
		var restored = JsonSerializer.Deserialize<QueueEntrySnapshot>(JsonSerializer.Serialize(copy));
		await Assert.That(restored).IsEqualTo(copy);
		await Assert.That(copy.SourceAttribute).IsEqualTo(source.SourceAttribute);
		await Assert.That(copy.InvocationCount).IsEqualTo(source.InvocationCount);
	}
}
