using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The channel rules moved to <see cref="IChannelPermissionService"/>, which
/// <see cref="IPermissionService"/> extends. These pin the two promises that move keeps: plugins built
/// against the wider interface still bind and still load, and both interfaces resolve to one service.
/// </summary>
public class ChannelPermissionCompatibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task PermissionServiceStillDeclaresEveryChannelMember()
	{
		// A compiled call binds to the interface named at compile time; a member that only a base
		// interface declares is a MissingMethodException for a plugin built against IPermissionService.
		foreach (var method in typeof(IChannelPermissionService).GetMethods())
		{
			var declared = typeof(IPermissionService).GetMethod(method.Name,
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
				method.GetParameters().Select(p => p.ParameterType).ToArray());
			await Assert.That(declared).IsNotNull();
			await Assert.That(declared!.ReturnType).IsEqualTo(method.ReturnType);
		}
	}

	[Test]
	public async Task ChannelPermissionServiceIsThePermissionService()
	{
		var permissions = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var channelPermissions = WebAppFactoryArg.Services.GetRequiredService<IChannelPermissionService>();
		await Assert.That(ReferenceEquals(permissions, channelPermissions)).IsTrue();
	}

	[Test]
	public async Task LegacyImplementationAnswersThroughTheChannelInterface()
	{
		// The published implementation shape: every abstract IPermissionService slot, nothing else. A
		// channel caller reaching it through IChannelPermissionService must still get its answer.
		var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"LegacyChannelPermissions{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
		var builder = assembly.DefineDynamicModule("plugin").DefineType("LegacyChannelPermissions", TypeAttributes.Public);
		builder.AddInterfaceImplementation(typeof(IPermissionService));
		foreach (var method in typeof(IPermissionService).GetMethods().Where(m => m.IsAbstract))
		{
			var parameters = method.GetParameters();
			var implementation = builder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
				method.ReturnType, parameters.Select(p => p.ParameterType).ToArray());
			var il = implementation.GetILGenerator();
			if (method.Name == nameof(IChannelPermissionService.ChannelOkType))
			{
				il.Emit(OpCodes.Ldc_I4_1);
				il.Emit(OpCodes.Ret);
			}
			else
			{
				il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
				il.Emit(OpCodes.Throw);
			}
			builder.DefineMethodOverride(implementation, method);
		}

		IChannelPermissionService legacy = (IPermissionService)Activator.CreateInstance(builder.CreateType()!)!;
		var actor = new TestObjectFactory().CreatePlayer(1, "actor");
		await Assert.That(legacy.ChannelOkType(actor, null!)).IsTrue();
	}
}
