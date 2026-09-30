using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// How the server hands out the bundled portal (<see cref="PortalStaticFiles"/>), on a real host
/// running the same two calls Program.ConfigureApp makes, over a web root laid out the way the
/// Dockerfile lays it out: the client's wwwroot, plus its endpoints manifest in the content root.
/// <para>
/// The manifest here is written by the test in the shape <c>dotnet publish</c> emits, with the
/// headers the SDK gives each kind of file — so what is under test is the wiring (is the manifest
/// found, is it what serves the files, what does the fallback send), not the SDK's choice of headers.
/// </para>
/// </summary>
public class PortalStaticFilesTests
{
	private const string Immutable = "max-age=31536000, immutable";
	private const string FingerprintedRoute = "_framework/dotnet.native.abc123.wasm";

	private static readonly byte[] IndexHtml = "<!DOCTYPE html><html><body>portal</body></html>"u8.ToArray();
	private static readonly byte[] Wasm = [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];
	private static readonly byte[] WasmBrotli = [0x0b, 0x03, 0x80, 0x00, 0x61, 0x73, 0x6d, 0x01, 0x03];

	private static async Task<(WebApplication App, HttpClient Client, string Root)> StartAsync(bool withManifest)
	{
		var root = Directory.CreateTempSubdirectory("portal-static-files-").FullName;
		var webRoot = Path.Combine(root, "wwwroot");
		Directory.CreateDirectory(Path.Combine(webRoot, "_framework"));
		await File.WriteAllBytesAsync(Path.Combine(webRoot, "index.html"), IndexHtml);
		await File.WriteAllBytesAsync(Path.Combine(webRoot, FingerprintedRoute), Wasm);
		await File.WriteAllBytesAsync(Path.Combine(webRoot, FingerprintedRoute + ".br"), WasmBrotli);

		if (withManifest)
		{
			await File.WriteAllTextAsync(Path.Combine(root, PortalStaticFiles.ManifestFileName), Manifest());
		}

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			ContentRootPath = root,
			WebRootPath = webRoot,
			EnvironmentName = Environments.Production,
		});
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddAuthorization();
		builder.Services.AddRateLimiter(_ => { });

		var app = builder.Build();
		var manifest = PortalStaticFiles.FindManifest(app.Environment);
		app.UseRouting();
		app.UsePortalStaticFiles(manifest);
		app.UseAuthorization();
		app.UseRateLimiter();
		app.MapPortal(manifest);

		await app.StartAsync();
		return (app, app.GetTestClient(), root);
	}

	private static string Manifest()
	{
		static object Header(string name, string value) => new { Name = name, Value = value };
		static string ETag(byte[] bytes) => $"\"{Convert.ToBase64String(SHA256.HashData(bytes))}\"";

		object Endpoint(string route, string assetFile, byte[] bytes, string contentType, string cacheControl, string? encoding) => new
		{
			Route = route,
			AssetFile = assetFile,
			Selectors = encoding is null
				? Array.Empty<object>()
				: [new { Name = "Content-Encoding", Value = encoding, Quality = "0.5" }],
			ResponseHeaders = new[]
				{
					Header("Cache-Control", cacheControl),
					Header("Content-Length", bytes.Length.ToString()),
					Header("Content-Type", contentType),
					Header("ETag", ETag(bytes)),
					Header("Last-Modified", "Wed, 30 Sep 2026 05:03:25 GMT"),
					Header("Vary", "Accept-Encoding"),
				}
				.Concat(encoding is null ? [] : [Header("Content-Encoding", encoding)])
				.ToArray(),
			EndpointProperties = Array.Empty<object>(),
		};

		return JsonSerializer.Serialize(new
		{
			Version = 1,
			ManifestType = "Publish",
			Endpoints = new[]
			{
				Endpoint("index.html", "index.html", IndexHtml, "text/html", "no-cache", null),
				Endpoint(FingerprintedRoute, FingerprintedRoute, Wasm, "application/wasm", Immutable, null),
				Endpoint(FingerprintedRoute, FingerprintedRoute + ".br", WasmBrotli, "application/wasm", Immutable, "br"),
			},
		});
	}

	private static async Task StopAsync(WebApplication app, string root)
	{
		await app.StopAsync();
		await app.DisposeAsync();
		Directory.Delete(root, recursive: true);
	}

	private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? acceptEncoding = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, path);
		if (acceptEncoding is not null)
		{
			request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(acceptEncoding));
		}

		return client.SendAsync(request);
	}

	private static string? CacheControl(HttpResponseMessage response) => response.Headers.CacheControl?.ToString();

	[Test]
	public async Task WithManifest_AFingerprintedAsset_IsCachedAsImmutable()
	{
		var (app, client, root) = await StartAsync(withManifest: true);
		try
		{
			using var response = await GetAsync(client, "/" + FingerprintedRoute);

			await Assert.That(response.IsSuccessStatusCode).IsTrue();
			await Assert.That(CacheControl(response)).IsEqualTo(Immutable);
			await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(Wasm);
		}
		finally
		{
			await StopAsync(app, root);
		}
	}

	/// <summary>
	/// The precompressed file published beside the asset is what goes out to a client that accepts
	/// it — outside <c>_framework</c> as much as in it, which UseBlazorFrameworkFiles never did.
	/// </summary>
	[Test]
	public async Task WithManifest_ThePrecompressedVariant_IsNegotiated()
	{
		var (app, client, root) = await StartAsync(withManifest: true);
		try
		{
			using var response = await GetAsync(client, "/" + FingerprintedRoute, acceptEncoding: "br");

			await Assert.That(response.Content.Headers.ContentEncoding).Contains("br");
			await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(WasmBrotli);
		}
		finally
		{
			await StopAsync(app, root);
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task TheSpaFallback_ServesIndexHtml_AsNoCache(bool withManifest)
	{
		var (app, client, root) = await StartAsync(withManifest);
		try
		{
			using var response = await GetAsync(client, "/some/deep/route");

			await Assert.That(response.IsSuccessStatusCode).IsTrue();
			await Assert.That(CacheControl(response)).IsEqualTo(PortalStaticFiles.IndexCacheControl);
			await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(IndexHtml);
		}
		finally
		{
			await StopAsync(app, root);
		}
	}

	/// <summary>
	/// Without a manifest the old middleware still serves the files, so a dev run or a test host
	/// that has a web root and no manifest keeps working. It is also the proof that the manifest is
	/// what changes the headers: the same file, served this way, is not immutable.
	/// </summary>
	[Test]
	public async Task WithoutManifest_FilesAreStillServed_ButNotAsImmutable()
	{
		var (app, client, root) = await StartAsync(withManifest: false);
		try
		{
			using var response = await GetAsync(client, "/" + FingerprintedRoute);

			await Assert.That(response.IsSuccessStatusCode).IsTrue();
			await Assert.That(CacheControl(response)).IsNotEqualTo(Immutable);
		}
		finally
		{
			await StopAsync(app, root);
		}
	}
}
