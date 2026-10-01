using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The account session a service built in a test harness reads its acting character from: the
/// <see cref="IAccountAuthState"/> the container resolves, which <see cref="TrackingBunitContext"/>
/// defaults to the registered <see cref="AccountAuthService"/> or a session nobody is signed in to.
/// </summary>
internal static class TestAccountAuth
{
	public static IAccountAuthState Of(IServiceProvider services) => services.GetRequiredService<IAccountAuthState>();
}
