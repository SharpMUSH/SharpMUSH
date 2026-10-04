using System.Net;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Authentication;

file sealed class FixedJsonHandler(string json) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
		});
}

/// <summary>
/// A sign-in answer that parses as JSON but leaves out the roster, the name or the token is refused
/// before the tab adopts anything: the caller gets an <see cref="ApiFailure"/>, not an exception,
/// and the tab stays signed out.
/// </summary>
public class AccountAuthServiceIncompleteSessionTests : TrackingBunitContext
{
	private const string NoRoster =
		"""{"accountId":"a","username":"Ann","accountSessionToken":"t","mustChangePassword":false}""";
	private const string NullRoster =
		"""{"accountId":"a","username":"Ann","characters":null,"accountSessionToken":"t","mustChangePassword":false}""";
	private const string NoToken =
		"""{"accountId":"a","username":"Ann","characters":[],"mustChangePassword":false}""";
	private const string NoName =
		"""{"accountId":"a","characters":[],"accountSessionToken":"t","mustChangePassword":false}""";

	private AccountAuthService ServiceAnswering(string json, out HttpClient http)
	{
		http = new HttpClient(new FixedJsonHandler(json)) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		return new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
	}

	[TUnit.Core.Test]
	[TUnit.Core.Arguments(NoRoster)]
	[TUnit.Core.Arguments(NullRoster)]
	[TUnit.Core.Arguments(NoToken)]
	[TUnit.Core.Arguments(NoName)]
	public async Task Login_IncompleteAnswer_IsAFailureAndAdoptsNothing(string json)
	{
		var service = ServiceAnswering(json, out var http);
		using var _ = http;

		var result = await service.LoginAsync("Ann", "pw");

		result.Expect<ApiFailure>();
		await Assert.That(service.IsLoggedIn).IsFalse();
		await Assert.That(service.Username).IsNull();
	}

	[TUnit.Core.Test]
	[TUnit.Core.Arguments(NoRoster)]
	[TUnit.Core.Arguments(NoToken)]
	public async Task Register_IncompleteAnswer_IsAFailureAndAdoptsNothing(string json)
	{
		var service = ServiceAnswering(json, out var http);
		using var _ = http;

		var result = await service.RegisterAsync("Ann", null, "pw");

		result.Expect<ApiFailure>();
		await Assert.That(service.IsLoggedIn).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task SetupClaim_WithoutRoster_StandsButDoesNotSignIn()
	{
		var service = ServiceAnswering(NullRoster, out var http);
		using var _ = http;

		var result = await service.CompleteSetupAsync("Ann", "pw");

		await Assert.That(result.Expect<AccountAuthService.SetupClaimed>().SignedIn).IsFalse();
		await Assert.That(service.IsLoggedIn).IsFalse();
	}
}
