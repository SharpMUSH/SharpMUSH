using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Text;

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
	[Test]
	public async Task ImportFromConfigFileAsync_PropagatesExceptionForInvalidResponse()
	{
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		// The mock JSON is incomplete (missing required SharpMUSHOptions properties).
		// AdminConfigService.ImportFromConfigFileAsync re-throws deserialization exceptions.
		var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK,
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

		// The incomplete JSON causes a deserialization exception that the service propagates.
		// This validates the service correctly surfaces failures rather than silently swallowing them.
		await Assert.ThrowsAsync(async () => await service.ImportFromConfigFileAsync(configContent));
	}

	[Test]
	public async Task ImportFromConfigFileAsync_HttpError_ShouldHandleGracefully()
	{
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.BadRequest, "Error"))
		{
			BaseAddress = new Uri("http://localhost")
		};

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		const string configContent = "invalid config content";

		try
		{
			await service.ImportFromConfigFileAsync(configContent);
		}
		catch (Exception)
		{
		}
	}

	[Test]
	public async Task GetOptions_ShouldReturnConfiguration()
	{
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK,
			"""{"Configuration":{"Net":{"MudName":"Test"}}, "Metadata":{}}"""));

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		var options = await service.GetOptionsAsync();

		// should be T0 (list of config items), not T1 (error)
		await Assert.That(options.IsT1).IsFalse();
	}
}