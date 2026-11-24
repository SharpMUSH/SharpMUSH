using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Tests.Client;

public class MockHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = new HttpResponseMessage(statusCode)
		{
			Content = new StringContent(content, Encoding.UTF8, "application/json")
		};
		return Task.FromResult(response);
	}
}

public class AdminConfigServiceTests
{
	/// <summary>
	/// A response the client cannot read is a failure the import page can show. It used to throw, and
	/// the page caught the exception into the log and said nothing.
	/// </summary>
	[Test]
	public async Task ImportFromConfigFileAsync_ReportsAnUnreadableResponse()
	{
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		// The mock JSON is incomplete (missing required SharpMUSHOptions properties).
		using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK,
			"""{"Configuration":{"Net":{"MudName":"Test MUSH","Port":4201,"SslPort":4202}}, "Metadata":{}}"""))
		{
			BaseAddress = new Uri("http://localhost")
		};

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		const string configContent = """
		                             # Test configuration
		                             mud_name Test MUSH
		                             port 4201
		                             ssl_port 4202

		                             """;

		var failure = (await service.ImportFromConfigFileAsync(configContent)).Expect<ApiFailure>();

		await Assert.That(failure.Kind).IsEqualTo(ApiFailureKind.Unexpected);
	}

	[Test]
	public async Task ImportFromConfigFileAsync_CarriesTheServersRefusal()
	{
		const string refused = "Error importing configuration: unknown directive on line 1";
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.BadRequest,
			JsonSerializer.Serialize(refused)))
		{
			BaseAddress = new Uri("http://localhost")
		};

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		var failure = (await service.ImportFromConfigFileAsync("invalid config content")).Expect<ApiFailure>();

		await Assert.That(failure.Message).IsEqualTo(refused);
	}

	[Test]
	public async Task ExportConfigAsync_ReturnsTheFileAsSent()
	{
		const string exported = """{"Net":{"MudName":"Test"}}""";
		var httpClient = Substitute.For<IHttpClientFactory>();
		using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, exported))
		{
			BaseAddress = new Uri("http://localhost")
		};
		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(Substitute.For<ILogger<AdminConfigService>>(), httpClient);

		await Assert.That((await service.ExportConfigAsync()).Expect<string>()).IsEqualTo(exported);
	}

	[Test]
	public async Task ExportConfigAsync_CarriesTheServersRefusal()
	{
		var httpClient = Substitute.For<IHttpClientFactory>();
		using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.InternalServerError,
			JsonSerializer.Serialize("Error exporting configuration")))
		{
			BaseAddress = new Uri("http://localhost")
		};
		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(Substitute.For<ILogger<AdminConfigService>>(), httpClient);

		var failure = (await service.ExportConfigAsync()).Expect<ApiFailure>();

		await Assert.That(failure.Message).IsEqualTo("Error exporting configuration");
	}
}
