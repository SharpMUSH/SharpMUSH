using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The rail, the drawer and the section sidebars all read the application list; each read was a GET,
/// and the sidebars re-read on every render (a collapse click was a request). One read is shared, a
/// write through this client refreshes it, and a failed read is not kept.
/// </summary>
public class ApplicationRegistryClientTests
{
	private sealed class Handler : HttpMessageHandler
	{
		public int Lists { get; private set; }
		public bool Fail { get; set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/applications")
			{
				Lists++;
				return Task.FromResult(Fail
					? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
					: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") });
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
		}
	}

	private static (ApplicationRegistryClient, Handler) Build()
	{
		var handler = new Handler();
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		return (new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance), handler);
	}

	[Test]
	public async Task ABurstOfReaders_SharesOneRead()
	{
		var (client, handler) = Build();
		await Task.WhenAll(client.ListAsync(), client.ListAsync(), client.ListAsync());
		await client.ListAsync();
		await Assert.That(handler.Lists).IsEqualTo(1);
	}

	[Test]
	public async Task AWrite_RefreshesTheList()
	{
		var (client, handler) = Build();
		await client.ListAsync();
		await client.DeleteAsync("bbs");
		await client.ListAsync();
		await Assert.That(handler.Lists).IsEqualTo(2);
	}

	[Test]
	public async Task AFailedRead_IsNotKept()
	{
		var (client, handler) = Build();
		handler.Fail = true;
		await client.ListAsync();
		handler.Fail = false;
		await client.ListAsync();
		await Assert.That(handler.Lists).IsEqualTo(2);
	}
}
