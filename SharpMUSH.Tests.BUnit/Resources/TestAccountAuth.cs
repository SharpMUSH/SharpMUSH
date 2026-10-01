using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The account session a service built in a test harness reads its acting character from: the one the
/// test registered (as <see cref="IAccountAuthState"/> or, as most harnesses do, the concrete
/// <see cref="AccountAuthService"/>), or else a session nobody is signed in to.
/// </summary>
internal static class TestAccountAuth
{
	public static IAccountAuthState Of(IServiceProvider services) =>
		services.GetService<IAccountAuthState>()
		?? services.GetService<AccountAuthService>()
		?? Substitute.For<IAccountAuthState>();
}
