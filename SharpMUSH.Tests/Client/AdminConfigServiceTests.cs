using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tests.Server;
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

	/// <summary>
	/// The whole round trip: a real configuration payload off the wire becomes rows carrying its values.
	/// The response deliberately sends no metadata, so this also covers the fallback to the locally
	/// generated <c>ConfigMetadata</c> table.
	/// </summary>
	[Test]
	public async Task GetOptions_ShouldReturnConfiguration()
	{
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		var payload = JsonSerializer.Serialize(new ConfigurationResponse
		{
			Configuration = TestSharpMushOptions.Create(),
			Metadata = []
		});

		using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, payload))
		{
			BaseAddress = new Uri("http://localhost/")
		};

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		var options = (await service.GetOptionsAsync()).Expect<IEnumerable<AdminConfigService.ConfigItem>>();

		var mudName = options.Single(i => i.Key == nameof(NetOptions.MudName));
		await Assert.That(mudName.Value).IsEqualTo("SharpMUSH");
		await Assert.That(mudName.Description).IsEqualTo("Name of your MUSH as displayed to players");
	}

	/// <summary>
	/// A response whose configuration failed to parse is an error, not an empty-but-successful list.
	/// It used to surface as one bogus "Error" row per category.
	/// </summary>
	[Test]
	public async Task GetOptions_ReportsAnErrorWhenTheConfigurationIsMissing()
	{
		var logger = Substitute.For<ILogger<AdminConfigService>>();
		var httpClient = Substitute.For<IHttpClientFactory>();

		// Incomplete: SharpMUSHOptions' required members are absent, so deserialization fails.
		using var client = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK,
			"""{"configuration":{"Net":{"MudName":"Test"}}, "metadata":{}}"""))
		{
			BaseAddress = new Uri("http://localhost/")
		};

		httpClient.CreateClient("api").Returns(client);

		var service = new AdminConfigService(logger, httpClient);

		var options = await service.GetOptionsAsync();

		await Assert.That(options.Value).IsTypeOf<Error<string>>();
	}

	/// <summary>
	/// A fully-populated response, exactly as the server sends it: <see cref="OptionHelper"/> ships
	/// <c>ConfigMetadata.PropertyMetadata</c> as the metadata dictionary.
	/// </summary>
	private static ConfigurationResponse FullResponse() => new()
	{
		Configuration = TestSharpMushOptions.Create(),
		Metadata = ConfigMetadata.PropertyMetadata.ToDictionary()
	};

	/// <summary>
	/// Every configured property must surface with the value the response actually carries, filed
	/// under its category. This is the assertion the old reflection walk could not satisfy: it read
	/// SharpMUSHOptions' property list but called GetValue against the ConfigurationResponse, so
	/// every category threw TargetException and became an error row instead.
	/// </summary>
	[Test]
	public async Task ToConfigItems_ReadsValuesFromTheConfigurationSection()
	{
		var configured = FullResponse().ToConfigItems().Expect<IEnumerable<AdminConfigService.ConfigItem>>();
		var items = configured.ToList();

		var mudName = items.Single(i => i.Key == nameof(NetOptions.MudName));

		await Assert.That(mudName.Value).IsEqualTo("SharpMUSH");
		await Assert.That(mudName.Section).IsEqualTo("Net");
		await Assert.That(mudName.Type).IsEqualTo(nameof(String));

		// Section and Category are two names for the same thing on ConfigItem; they must not drift into
		// the two different spellings the generated metadata and the schema use for a category.
		await Assert.That(mudName.Category).IsEqualTo(mudName.Section);
	}

	/// <summary>
	/// No item may be an error placeholder. ToConfigItems catches per-section and per-property
	/// failures and turns them into rows the admin page renders as config, so a total failure to
	/// read the configuration looks like a populated list unless the rows are inspected.
	/// </summary>
	[Test]
	public async Task ToConfigItems_ProducesNoErrorRows()
	{
		var configured = FullResponse().ToConfigItems().Expect<IEnumerable<AdminConfigService.ConfigItem>>();
		var items = configured.ToList();

		var errors = items
			.Where(i => i.Type == "Error" || i.Value.StartsWith("Error:", StringComparison.Ordinal))
			.Select(i => $"{i.Section}.{i.Key}: {i.Value}")
			.ToList();

		await Assert.That(errors).IsEmpty();
	}

	/// <summary>
	/// Every property the generator recorded must appear exactly once — the metadata dictionary and
	/// the emitted rows are two views of the same generated table and must not drift.
	/// </summary>
	[Test]
	public async Task ToConfigItems_CoversEveryConfiguredProperty()
	{
		var configured = FullResponse().ToConfigItems().Expect<IEnumerable<AdminConfigService.ConfigItem>>();
		var items = configured.ToList();

		var keys = items.Select(i => i.Key).ToList();

		await Assert.That(keys.Order()).IsEquivalentTo(ConfigMetadata.PropertyMetadata.Keys.Order());
	}
}
