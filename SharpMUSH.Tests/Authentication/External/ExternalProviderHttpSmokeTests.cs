using System.Net;
using System.Text.Json;

namespace SharpMUSH.Tests.Authentication.External;

public class ExternalProviderHttpSmokeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task FullServerHostServesAnonymousDiscoveryWithAllRegistrationsDisabled()
	{
		using var client = Factory.CreateHttpClient();
		using var response = await client.GetAsync("/api/auth/external/providers");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		using var providers = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		await Assert.That(providers.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
		await Assert.That(providers.RootElement.GetArrayLength()).IsEqualTo(0);
	}
}
