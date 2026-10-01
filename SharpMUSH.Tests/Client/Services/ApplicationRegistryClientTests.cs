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
public class ApplicationRegistryClientTests : IDisposable
{
	private sealed class Handler : HttpMessageHandler
	{
		public int Lists { get; private set; }
		public bool Fail { get; set; }

		/// <summary>The catalog a GET answers with; a write replaces it once its gate opens.</summary>
		public string Catalog { get; set; } = "[]";

		/// <summary>When set, a write waits for it before the server applies it.</summary>
		public TaskCompletionSource? WriteGate { get; set; }

		/// <summary>When set, a GET reads the catalog, then waits for it before answering.</summary>
		public TaskCompletionSource? ReadGate { get; set; }

		public string CatalogAfterWrite { get; set; } = "[]";

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/applications")
			{
				Lists++;
				var catalog = Catalog;
				if (ReadGate is { } read) await read.Task;
				return Fail
					? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
					: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalog, Encoding.UTF8, "application/json") };
			}

			if (WriteGate is { } write) await write.Task;
			Catalog = CatalogAfterWrite;
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
		}
	}

	private const string Bbs = """[{"slug":"bbs","displayName":"BBS","kind":"schema","schemaUrl":"/apps/bbs/schema"}]""";

	private readonly List<HttpClient> _clients = [];

	public void Dispose()
	{
		foreach (var client in _clients)
		{
			client.Dispose();
		}
	}

	private (ApplicationRegistryClient, Handler) Build()
	{
		var handler = new Handler();
		var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(http);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		return (new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance), handler);
	}

	[Test]
	public async Task AReadDuringAWrite_IsNotKeptPastTheWrite()
	{
		var (client, handler) = Build();
		handler.CatalogAfterWrite = Bbs;
		handler.WriteGate = new TaskCompletionSource();

		var delete = client.DeleteAsync("bbs");
		await Assert.That((await client.ListAsync()).Count).IsEqualTo(0).Because("the server has not applied the write yet");
		handler.WriteGate.SetResult();
		await delete;

		await Assert.That((await client.ListAsync()).Count).IsEqualTo(1)
			.Because("the list read while the write was in flight is stale once the write lands");
	}

	[Test]
	public async Task AReadStartedBeforeAWrite_DoesNotRepopulateTheList()
	{
		var (client, handler) = Build();
		handler.CatalogAfterWrite = Bbs;
		handler.ReadGate = new TaskCompletionSource();

		var early = client.ListAsync();
		await client.DeleteAsync("bbs");
		handler.ReadGate.SetResult();
		handler.ReadGate = null;
		await early;

		await Assert.That((await client.ListAsync()).Count).IsEqualTo(1)
			.Because("the early read saw the catalog before the write; it must not be served after it");
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
