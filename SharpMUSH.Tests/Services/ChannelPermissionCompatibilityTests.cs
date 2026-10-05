using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The channel rules live on <see cref="IChannelPermissionService"/>, apart from
/// <see cref="IPermissionService"/>; the engine still resolves both interfaces to one service.
/// </summary>
public class ChannelPermissionCompatibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task PermissionServiceDoesNotCarryTheChannelRules()
		=> await Assert.That(typeof(IChannelPermissionService).IsAssignableFrom(typeof(IPermissionService))).IsFalse();

	[Test]
	public async Task ChannelPermissionServiceIsThePermissionService()
	{
		var permissions = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var channelPermissions = WebAppFactoryArg.Services.GetRequiredService<IChannelPermissionService>();
		await Assert.That(ReferenceEquals(permissions, channelPermissions)).IsTrue();
	}
}
