using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The channel rules live on <see cref="IChannelPermissionService"/>, which
/// <see cref="IPermissionService"/> extends; both interfaces resolve to one service.
/// </summary>
public class ChannelPermissionCompatibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task ChannelPermissionServiceIsThePermissionService()
	{
		var permissions = WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
		var channelPermissions = WebAppFactoryArg.Services.GetRequiredService<IChannelPermissionService>();
		await Assert.That(ReferenceEquals(permissions, channelPermissions)).IsTrue();
	}
}
