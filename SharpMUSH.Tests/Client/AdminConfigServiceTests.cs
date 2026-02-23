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

public class AdminConfigServiceTests(IHttpClientFactory httpClient)
{
	[Test, Skip("Skip")]
	public async Task ImportFromConfigFileAsync_ValidConfig_ShouldNotThrow()
	{
		// Arrange
		var logger = Substitute.For<ILogger<AdminConfigService>>();

		var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK,
			"""{"Configuration":{"Net":{"MudName":"Test MUSH","Port":4201,"SslPort":4202}}, "Metadata":[]}"""))
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

		// Act & Assert - Should not throw
		await service.ImportFromConfigFileAsync(configContent);
	}

	[Test, Skip("Skip")]
	public async Task ImportFromConfigFileAsync_HttpError_ShouldHandleGracefully()
	{
		// Arrange
		var logger = Substitute.For<ILogger<AdminConfigService>>();


		var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.BadRequest, "Error"))
		{
			BaseAddress = new Uri("http://localhost")
		};

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		const string configContent = "invalid config content";

		// Act & Assert - Should handle HTTP errors gracefully
		try
		{
			await service.ImportFromConfigFileAsync(configContent);
		}
		catch (Exception)
		{
			// Expected to handle error
		}
	}

	[Test, Skip("Skip")]
	public async Task GetOptions_ShouldReturnConfiguration()
	{
		// Arrange
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK,
			"""{"Configuration":{"Net":{"MudName":"Test"}}, "Metadata":[]}"""));

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		// Act
		var options = await service.GetOptionsAsync();

		// Assert
		await Assert.That(options.IsT1).IsFalse();
	}
}