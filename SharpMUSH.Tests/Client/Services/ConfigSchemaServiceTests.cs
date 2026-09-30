using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The schema describes the shape of the options, which only changes with a deploy, so one fetch per
/// session is enough: the config layout, the home and every section page used to fetch it again.
/// </summary>
public class ConfigSchemaServiceTests
{
	/// <summary>Counts requests and answers each from a queue of responses.</summary>
	private sealed class CountingHandler(Queue<Func<HttpResponseMessage>> replies) : HttpMessageHandler
	{
		public int Requests { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests++;
			return Task.FromResult(replies.Dequeue()());
		}
	}

	private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
	{
		Content = new StringContent(JsonSerializer.Serialize(new ConfigurationResponse
		{
			Configuration = SharpMUSHOptions.Default(),
			Schema = SchemaBuilder.BuildSchema()
		}), Encoding.UTF8, "application/json")
	};

	private static (ConfigSchemaService Service, CountingHandler Handler) Build(params Func<HttpResponseMessage>[] replies)
	{
		var handler = new CountingHandler(new Queue<Func<HttpResponseMessage>>(replies));
		var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		return (new ConfigSchemaService(factory, NullLogger<ConfigSchemaService>.Instance), handler);
	}

	[Test]
	public async Task ASuccessfulSchemaIsFetchedOnce()
	{
		var (service, handler) = Build(Ok, Ok);
		var first = await service.GetSchemaAsync();
		var second = await service.GetSchemaAsync();
		first.Expect<ConfigurationSchema>();
		second.Expect<ConfigurationSchema>();
		await Assert.That(handler.Requests).IsEqualTo(1);
	}

	[Test]
	public async Task AFailureIsNotRemembered()
	{
		var (service, handler) = Build(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), Ok);
		(await service.GetSchemaAsync()).Expect<ApiFailure>();
		(await service.GetSchemaAsync()).Expect<ConfigurationSchema>();
		await Assert.That(handler.Requests).IsEqualTo(2);
	}
}
