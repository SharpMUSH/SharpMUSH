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

	/// <summary>
	/// The service, the handler that counts its requests, and the client between them. The test owns
	/// the client (a factory hands clients out and never takes them back), so the harness disposes it.
	/// </summary>
	private sealed record Harness(ConfigSchemaService Service, CountingHandler Handler, HttpClient Client) : IDisposable
	{
		public void Dispose() => Client.Dispose();
	}

	private static Harness Build(params Func<HttpResponseMessage>[] replies) =>
		Build(factory => factory, replies);

	private static Harness Build(Func<IHttpClientFactory, IHttpClientFactory> configure, params Func<HttpResponseMessage>[] replies)
	{
		var handler = new CountingHandler(new Queue<Func<HttpResponseMessage>>(replies));
		var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		return new Harness(new ConfigSchemaService(configure(factory), NullLogger<ConfigSchemaService>.Instance), handler, client);
	}

	[Test]
	public async Task ASuccessfulSchemaIsFetchedOnce()
	{
		using var harness = Build(Ok, Ok);
		var first = await harness.Service.GetSchemaAsync();
		var second = await harness.Service.GetSchemaAsync();
		first.Expect<ConfigurationSchema>();
		second.Expect<ConfigurationSchema>();
		await Assert.That(harness.Handler.Requests).IsEqualTo(1);
	}

	[Test]
	public async Task AFailureIsNotRemembered()
	{
		using var harness = Build(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), Ok);
		(await harness.Service.GetSchemaAsync()).Expect<ApiFailure>();
		(await harness.Service.GetSchemaAsync()).Expect<ConfigurationSchema>();
		await Assert.That(harness.Handler.Requests).IsEqualTo(2);
	}

	[Test]
	public async Task AFetchThatThrowsIsNotRemembered()
	{
		var calls = 0;
		using var harness = Build(factory =>
		{
			var client = factory.CreateClient("api");
			var throwing = Substitute.For<IHttpClientFactory>();
			throwing.CreateClient("api").Returns(_ => ++calls == 1
				? throw new InvalidOperationException("no api client yet")
				: client);
			return throwing;
		}, Ok);

		await Assert.That(async () => await harness.Service.GetSchemaAsync()).Throws<InvalidOperationException>();
		(await harness.Service.GetSchemaAsync()).Expect<ConfigurationSchema>();
		await Assert.That(harness.Handler.Requests).IsEqualTo(1);
	}
}
