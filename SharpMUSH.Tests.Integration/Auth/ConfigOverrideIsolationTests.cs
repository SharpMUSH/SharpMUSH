using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// The host is shared by every test in the session, so a configuration change made for one test
/// must reach that test's own HTTP requests and nothing else. <see cref="TestOptionsOverride"/>
/// carries the change in an <see cref="AsyncLocal{T}"/>, which the TestServer hands to the request
/// only when it preserves the client's execution context.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ConfigOverrideIsolationTests(ServerWebAppFactory factory)
{
	private record AccountRegisterRequest(string Username, string? Email, string Password);

	private async Task<HttpStatusCode> RegisterAsync()
	{
		using var response = await factory.CreateHttpClient().PostAsJsonAsync("api/auth/account-register",
			new AccountRegisterRequest(TestIsolationHelpers.GenerateUniqueName("iso"), null, "Integration-Test-Pw-1!"));
		return response.StatusCode;
	}

	[Test]
	public async Task ScopedOverride_ReachesOwnRequest_AndNotAConcurrentOne()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		// Started before the scope opens, so its flow never carries the override.
		var outside = Task.Run(async () =>
		{
			await release.Task;
			return await RegisterAsync();
		});

		using (TestOptionsOverride.Scope(options => options with { Net = options.Net with { PlayerCreation = false } }))
		{
			await Assert.That(await RegisterAsync()).IsEqualTo(HttpStatusCode.Forbidden);

			release.SetResult();
			await Assert.That(await outside).IsEqualTo(HttpStatusCode.OK);
		}
	}
}
