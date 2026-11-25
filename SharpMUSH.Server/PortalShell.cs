using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server;

/// <summary>
/// The portal's <c>index.html</c> as this game serves it: its <c>&lt;title&gt;</c> is the game's name, and a
/// <see cref="PortalBootIdentity"/> rides in it as JSON, so the portal's first render shows the game's own logos and
/// guide instead of SharpMUSH's until <c>api/server-info</c> answers. The portal reads the JSON before it renders
/// (Program.cs), which waits on nothing from the network.
/// </summary>
public sealed partial class PortalShell(IFileProvider files, string? earlyHints)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	[GeneratedRegex(@"<title>[^<]*</title>", RegexOptions.IgnoreCase)]
	private static partial Regex Title();

	/// <summary>What <paramref name="options"/> give the portal to show before it has asked the server anything.</summary>
	public static PortalBootIdentity Identity(SharpMUSHOptions options) => new(
		string.IsNullOrWhiteSpace(options.Net.MudName) ? "SharpMUSH" : options.Net.MudName.Trim(),
		PortalPicture.Logo(options.Cosmetic) ?? "",
		PortalPicture.FullLogo(options.Cosmetic) ?? "",
		options.Onboarding?.GuideName.Trim() ?? "",
		OnboardingGuide.Art(options.Onboarding));

	/// <summary>
	/// <paramref name="shell"/> with <paramref name="identity"/> written in: the game's name as its title, and the
	/// identity as JSON just before <c>&lt;/head&gt;</c>. The JSON escapes <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>,
	/// so no value can close the script element.
	/// </summary>
	public static string Render(string shell, PortalBootIdentity identity)
	{
		var titled = Title().Replace(shell, $"<title>{WebUtility.HtmlEncode(identity.Name)}</title>", 1);
		var head = titled.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
		if (head < 0) return titled;
		var script = $"<script id=\"{PortalBootIdentity.ElementId}\" type=\"application/json\">{JsonSerializer.Serialize(identity, Json)}</script>\n";
		return titled.Insert(head, script);
	}

	/// <summary>
	/// Sends the shell for a request the SPA fallback matched, with the early hints as its <c>Link</c> header.
	/// Without game options (a bare test host) the shell goes out as it is on disk.
	/// </summary>
	public async Task WriteAsync(HttpContext context)
	{
		var request = context.Request;
		var response = context.Response;
		if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) || await ReadAsync() is not { } shell)
		{
			response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		var options = context.RequestServices.GetService<IOptionsWrapper<SharpMUSHOptions>>()?.CurrentValue;
		var body = Encoding.UTF8.GetBytes(options is null ? shell : Render(shell, Identity(options)));
		var etag = $"\"{Convert.ToBase64String(SHA256.HashData(body), 0, 12)}\"";

		response.Headers.CacheControl = PortalStaticFiles.IndexCacheControl;
		response.Headers.ETag = etag;
		if (earlyHints is not null) response.Headers.Link = earlyHints;
		if (request.Headers.IfNoneMatch.Contains(etag))
		{
			response.StatusCode = StatusCodes.Status304NotModified;
			return;
		}

		response.ContentType = "text/html; charset=utf-8";
		response.ContentLength = body.Length;
		if (HttpMethods.IsHead(request.Method)) return;
		await response.Body.WriteAsync(body, context.RequestAborted);
	}

	private (DateTimeOffset Modified, long Length, string Text)? _cached;

	/// <summary>The shell from the web root, read again only when the file changes (a client rebuilt under a dev run).</summary>
	private async Task<string?> ReadAsync()
	{
		var file = files.GetFileInfo(PortalStaticFiles.IndexFileName);
		if (!file.Exists) return null;
		if (_cached is { } cached && cached.Modified == file.LastModified && cached.Length == file.Length) return cached.Text;

		await using var stream = file.CreateReadStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		var text = await reader.ReadToEndAsync();
		_cached = (file.LastModified, file.Length, text);
		return text;
	}
}
