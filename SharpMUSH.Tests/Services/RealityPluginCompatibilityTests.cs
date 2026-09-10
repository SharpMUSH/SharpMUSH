using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class RealityPluginCompatibilityTests
{
	[Test]
	public async Task PublishedEngineStateConstructorAndDeconstructorRemainCallable()
	{
		var type = typeof(EngineStateResponse);
		var signature = new[] { typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>) };
		var constructor = type.GetConstructor(signature);
		await Assert.That(constructor).IsNotNull();
		var message = (EngineStateResponse)constructor!.Invoke(["#1", "#2", "Room", new[] { "#3" }]);
		await Assert.That(message.Truncated).IsFalse();
		var deconstruct = type.GetMethod("Deconstruct", signature.Select(t => t.MakeByRefType()).ToArray());
		await Assert.That(deconstruct).IsNotNull();
		object?[] values = [null, null, null, null];
		deconstruct!.Invoke(message, values);
		await Assert.That(values[2]).IsEqualTo("Room");
		var truncated = message with { Truncated = true };
		var restored = JsonSerializer.Deserialize<EngineStateResponse>(JsonSerializer.Serialize(truncated));
		await Assert.That(restored!.Truncated).IsTrue();
		await Assert.That(restored.VisibleObjectDbrefs).IsEquivalentTo(new[] { "#3" });
	}

	[Test]
	public async Task PublishedRoomEventConstructorAndDeconstructorRemainCallable()
	{
		var type = typeof(RoomEventMessage);
		var constructor = type.GetConstructor([typeof(string), typeof(RoomEventType), typeof(string), typeof(string)]);
		await Assert.That(constructor).IsNotNull();
		var message = (RoomEventMessage)constructor!.Invoke(["#1", default(RoomEventType), "actor", "content"]);
		await Assert.That(message.ActorDbref).IsNull();
		var deconstruct = type.GetMethod("Deconstruct", [typeof(string).MakeByRefType(), typeof(RoomEventType).MakeByRefType(),
			typeof(string).MakeByRefType(), typeof(string).MakeByRefType()]);
		await Assert.That(deconstruct).IsNotNull();
		object?[] values = [null, null, null, null];
		deconstruct!.Invoke(message, values);
		await Assert.That(values[0]).IsEqualTo("#1");
		await Assert.That(values[3]).IsEqualTo("content");
	}

	[Test]
	public async Task RoomEventActorIdentitySurvivesSerialization()
	{
		var message = new RoomEventMessage("#1", default, "actor", "content", "#2:3");
		var restored = JsonSerializer.Deserialize<RoomEventMessage>(JsonSerializer.Serialize(message));
		await Assert.That(restored).IsEqualTo(message);
	}

	[Test]
	public async Task LegacyPermissionImplementationLoadsAndUsesDefaultNewOverload()
	{
		// Emit the published implementation shape: all old interface slots, without the new
		// four-argument overload. This reproduces loading an already-built plugin assembly.
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"LegacyPermissions{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
		var builder = assembly.DefineDynamicModule("plugin").DefineType("LegacyPermissions", TypeAttributes.Public);
		builder.AddInterfaceImplementation(typeof(IPermissionService));
		foreach (var method in typeof(IPermissionService).GetMethods())
		{
			var parameters = method.GetParameters();
			if (!method.IsAbstract || method.Name == nameof(IPermissionService.CanInteract) && parameters.Length == 4) continue;
			var implementation = builder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
				method.ReturnType, parameters.Select(p => p.ParameterType).ToArray());
			var il = implementation.GetILGenerator();
			if (method.Name == nameof(IPermissionService.CanInteract) && parameters.Length == 3)
			{
				il.Emit(OpCodes.Ldc_I4_1);
				il.Emit(OpCodes.Newobj, typeof(ValueTask<bool>).GetConstructor([typeof(bool)])!);
				il.Emit(OpCodes.Ret);
			}
			else
			{
				il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
				il.Emit(OpCodes.Throw);
			}
			builder.DefineMethodOverride(implementation, method);
		}
		var legacy = (IPermissionService)Activator.CreateInstance(builder.CreateType()!)!;
		var actor = new TestObjectFactory().CreatePlayer(1, "actor");
		await Assert.That(await legacy.CanInteract(actor, actor, IPermissionService.InteractType.Hear, actor)).IsTrue();
	}
}
