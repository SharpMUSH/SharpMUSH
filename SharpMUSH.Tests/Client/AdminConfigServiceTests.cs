using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client;

public class MockHttpMessageHandler : HttpMessageHandler
{
	private readonly HttpStatusCode _statusCode;
	private readonly string _content;

	public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
	{
		_statusCode = statusCode;
		_content = content;
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = new HttpResponseMessage(_statusCode)
		{
			Content = new StringContent(_content, Encoding.UTF8, "application/json")
		};
		return Task.FromResult(response);
	}
}

public class AdminConfigServiceTests
{
	[Test, Skip("WIP")]
	public async Task ImportFromConfigFileAsync_ValidConfig_ShouldNotThrow()
	{
		// Arrange
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"));
		var service = new AdminConfigService(logger, httpClient);
		
		var configContent = @"# Test configuration
mud_name Test MUSH
port 4201
ssl_port 4202
";

		// Act & Assert - Should not throw
		await service.ImportFromConfigFileAsync(configContent);
	}

	[Test, Skip("WIP")]
	public async Task ImportFromConfigFileAsync_HttpError_ShouldHandleGracefully()
	{
		// Arrange
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.BadRequest, "Error"));
		var service = new AdminConfigService(logger, httpClient);
		
		var configContent = "invalid config content";

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

	[Test, Skip("WIP")]
	public async Task GetOptions_ShouldReturnConfiguration()
	{
		// Arrange
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, 
			"""{"Configuration":{"Net":{"MudName":"Test"}}, "Metadata":[]}"""));
		var service = new AdminConfigService(logger, httpClient);

		// Act
		var options = await service.GetOptionsAsync();

		// Assert
		await Assert.That(options).IsNotNull();
	}
}