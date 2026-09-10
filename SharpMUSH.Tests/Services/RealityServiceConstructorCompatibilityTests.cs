using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.DiscriminatedUnions;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using System.Reflection.Emit;

namespace SharpMUSH.Tests.Services;

public class RealityServiceConstructorCompatibilityTests
{
	[Test]
	public async Task DependencyInjectionUsesConfiguredRealityWhileLegacyConstructorPreservesBehavior()
	{
		var locks = Substitute.For<ILockService>();
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		var reality = Substitute.For<IRealityPolicy>();
		var services = new ServiceCollection();
		services.AddSingleton(locks).AddSingleton(options).AddSingleton(reality).AddTransient<PermissionService>();
		await using var provider = services.BuildServiceProvider();
		var objects = new TestObjectFactory();
		AnySharpObject room = objects.CreateRoom(10, "Room");
		var legacy = new PermissionService(locks, options);
		await Assert.That(await legacy.CanInteract(room, room, IPermissionService.InteractType.See)).IsTrue();
		await Assert.That(await provider.GetRequiredService<PermissionService>().CanInteract(room, room, IPermissionService.InteractType.See)).IsFalse();
	}

	[Test]
	[Arguments("permission")]
	[Arguments("notify")]
	[Arguments("listener")]
	public async Task PublishedConstructorSlotCanStillBeInvoked(string service)
	{
		(Type type, Type[] parameters) = service switch
		{
			"permission" => (typeof(PermissionService), new Type[] { typeof(ILockService), typeof(IOptionsMonitor<SharpMUSHOptions>) }),
			"notify" => (typeof(NotifyService), new Type[] { typeof(IMessageBus), typeof(IConnectionService), typeof(ILocalizationService), typeof(IListenerRoutingService), typeof(IMediator), typeof(IHttpOutputCapture) }),
			_ => (typeof(ListenerRoutingService), new Type[] { typeof(IMediator), typeof(IListenPatternMatcher), typeof(IPermissionService), typeof(ILockService), typeof(IConnectionService), typeof(IServiceProvider), typeof(IMessageBus) })
		};
		// Exact signatures from the published pre-reality library, including optional parameters.
		// MoveService is not among them: IMoveService no longer declares ExecuteMoveAsync,
		// CanMoveAsync or CalculateMoveCostAsync, so a binary compiled against the old library
		// cannot call it whatever constructor it finds.
		var constructor = type.GetConstructor(parameters);
		await Assert.That(constructor).IsNotNull();
		var method = new DynamicMethod("LegacyNew", typeof(object), Type.EmptyTypes);
		var il = method.GetILGenerator();
		foreach (var _ in parameters) il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Newobj, constructor!);
		il.Emit(OpCodes.Ret);
		var instance = ((Func<object>)method.CreateDelegate(typeof(Func<object>)))();
		await Assert.That(instance.GetType()).IsEqualTo(type);
	}
}
