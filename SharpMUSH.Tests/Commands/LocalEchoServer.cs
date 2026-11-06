using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// An HTTP echo service on a loopback port, answering the endpoints the @http tests and the examples in
/// <c>help @http</c> use, in the shape postman-echo.com answers them: the request's query in <c>args</c>, its
/// lower-cased headers in <c>headers</c>, a form body in <c>form</c>, a JSON body in <c>json</c> and the URL in
/// <c>url</c>. <c>/gzip</c> and <c>/deflate</c> send their body compressed, <c>/basic-auth</c> accepts
/// <c>postman:password</c>, and <c>/status/{code}</c> answers with that code.
/// </summary>
public sealed class LocalEchoServer : IAsyncInitializer, IAsyncDisposable
{
	private WebApplication? _app;

	/// <summary>The server's base URL, without a trailing slash.</summary>
	public string BaseUrl { get; private set; } = "";

	public async Task InitializeAsync()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		var app = builder.Build();

		app.MapGet("/get", async context => await WriteJsonAsync(context, await EchoAsync(context, withBody: false)));
		foreach (var verb in new[] { "POST", "PUT", "PATCH", "DELETE" })
			app.MapMethods($"/{verb.ToLowerInvariant()}", [verb],
				async context => await WriteJsonAsync(context, await EchoAsync(context, withBody: true)));
		app.MapGet("/gzip", async context =>
			await WriteCompressedAsync(context, "gzip", Echo(context, "gzipped"), body => new GZipStream(body, CompressionLevel.Fastest)));
		app.MapGet("/deflate", async context =>
			await WriteCompressedAsync(context, "deflate", Echo(context, "deflated"), body => new ZLibStream(body, CompressionLevel.Fastest)));
		app.MapGet("/basic-auth", async context =>
		{
			var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("postman:password"));
			var authenticated = context.Request.Headers.Authorization.ToString() == expected;
			context.Response.StatusCode = authenticated ? 200 : 401;
			await WriteJsonAsync(context, new JsonObject { ["authenticated"] = authenticated });
		});
		app.MapGet("/status/{code:int}", async (HttpContext context, int code) =>
		{
			context.Response.StatusCode = code;
			await WriteJsonAsync(context, new JsonObject { ["status"] = code });
		});

		await app.StartAsync();
		_app = app;
		BaseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>() is { } addresses
			? addresses.Addresses.Single().TrimEnd('/')
			: throw new InvalidOperationException("Kestrel reported no listening address.");
	}

	public async ValueTask DisposeAsync()
	{
		if (_app is null) return;
		await _app.StopAsync();
		await _app.DisposeAsync();
	}

	private static JsonObject Echo(HttpContext context, string? flag = null)
	{
		var echo = new JsonObject();
		if (flag is not null) echo[flag] = true;
		var args = new JsonObject();
		foreach (var (key, value) in context.Request.Query) args[key] = value.ToString();
		echo["args"] = args;
		var headers = new JsonObject();
		foreach (var (key, value) in context.Request.Headers) headers[key.ToLowerInvariant()] = value.ToString();
		echo["headers"] = headers;
		echo["url"] = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
		return echo;
	}

	private static async Task<JsonObject> EchoAsync(HttpContext context, bool withBody)
	{
		var echo = Echo(context);
		if (!withBody) return echo;

		using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
		var data = await reader.ReadToEndAsync();
		var form = new JsonObject();
		JsonNode? json = null;
		var mediaType = context.Request.ContentType?.Split(';')[0].Trim();
		if (mediaType == "application/json")
		{
			try { json = JsonNode.Parse(data); }
			catch (System.Text.Json.JsonException) { json = null; }
		}
		else if (mediaType is null or "application/x-www-form-urlencoded" or "text/plain")
		{
			foreach (var (key, value) in QueryHelpers.ParseQuery(data)) form[key] = value.ToString();
		}

		echo["data"] = data;
		echo["form"] = form;
		echo["json"] = json;
		return echo;
	}

	private static async Task WriteJsonAsync(HttpContext context, JsonNode body)
	{
		context.Response.ContentType = "application/json; charset=utf-8";
		await context.Response.WriteAsync(body.ToJsonString());
	}

	private static async Task WriteCompressedAsync(HttpContext context, string encoding, JsonNode body,
		Func<Stream, Stream> compress)
	{
		var buffer = new MemoryStream();
		await using (var compressed = compress(buffer))
			await compressed.WriteAsync(Encoding.UTF8.GetBytes(body.ToJsonString()));
		context.Response.ContentType = "application/json; charset=utf-8";
		context.Response.Headers.ContentEncoding = encoding;
		await context.Response.Body.WriteAsync(buffer.ToArray());
	}
}
