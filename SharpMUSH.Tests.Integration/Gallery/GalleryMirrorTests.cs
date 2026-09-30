using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Gallery;

/// <summary>
/// Spec §2 end to end: a gallery write through <c>/api/profile/{name}/gallery</c> mirrors the icon,
/// the banner and the icon's caption into <c>IMAGE</c>, <c>IMAGE`BANNER</c> and <c>IMAGE`ALT</c> on
/// the character, and a later write that removes one clears it. The harness authenticates as God (#1),
/// so the gallery under test is God's; the test empties it again when it is done.
/// </summary>
[NotInParallel]
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class GalleryMirrorTests(ServerWebAppFactory factory)
{
	private ISharpDatabase Database => factory.Services.GetRequiredService<ISharpDatabase>();

	private async Task<string> ReadAttributeAsync(string attribute)
	{
		var leaf = await Database.GetAttributeAsync(new DBRef(1), attribute.Split('`'), CancellationToken.None)
			.LastOrDefaultAsync();
		return leaf?.Value.ToPlainText() ?? "";
	}

	private static async Task<string> GodNameAsync(HttpClient http)
	{
		var rows = await http.GetFromJsonAsync<List<GalleryNameRow>>("http/characters");
		return rows!.First(r => r.Objid.StartsWith("#1:", StringComparison.Ordinal)).Name;
	}

	private sealed record GalleryNameRow(string Name, string Objid);

	private static async Task<List<GalleryEntry>> UploadAsync(HttpClient http, string name, string fileName)
	{
		using var content = new MultipartFormDataContent();
		// The smallest valid PNG: the store checks the declared type, not the bytes.
		var bytes = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
		bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		content.Add(bytes, "file", fileName);
		var response = await http.PostAsync($"api/profile/{Uri.EscapeDataString(name)}/gallery", content);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<List<GalleryEntry>>())!;
	}

	[Test]
	public async Task Replace_KeepsTheStoredUrl_WhateverTheClientSends()
	{
		// PUT used to store each entry as sent, so a client could point a gallery image (and, through the
		// mirror, IMAGE) at any URL it liked. Only order, captions and flags are the client's to set.
		var http = factory.CreateHttpClient();
		var name = await GodNameAsync(http);
		var url = $"api/profile/{Uri.EscapeDataString(name)}/gallery";
		var before = await http.GetFromJsonAsync<List<GalleryEntry>>(url) ?? [];
		try
		{
			var entries = await UploadAsync(http, name, $"spoof-{Guid.NewGuid():N}.png");
			var mine = entries[^1];
			var response = await http.PutAsJsonAsync(url, entries
				.Select(e => e.AssetId == mine.AssetId ? e with { Url = "javascript:alert(1)", FileName = "x" } : e)
				.ToList());
			var saved = (await response.Content.ReadFromJsonAsync<List<GalleryEntry>>())!.Single(e => e.AssetId == mine.AssetId);

			await Assert.That(saved.Url).IsEqualTo(mine.Url);
			await Assert.That(saved.FileName).IsEqualTo(mine.FileName);
		}
		finally
		{
			var now = await http.GetFromJsonAsync<List<GalleryEntry>>(url) ?? [];
			foreach (var entry in now.Where(e => before.All(b => b.AssetId != e.AssetId)))
			{
				await http.DeleteAsync($"{url}/{Uri.EscapeDataString(entry.AssetId)}");
			}
		}
	}

	[Test]
	public async Task GalleryWrites_MirrorIconBannerAndCaption_AndClearWhatTheyRemove()
	{
		var http = factory.CreateHttpClient();
		var name = await GodNameAsync(http);
		var url = $"api/profile/{Uri.EscapeDataString(name)}/gallery";
		var before = await http.GetFromJsonAsync<List<GalleryEntry>>(url) ?? [];

		try
		{
			await UploadAsync(http, name, $"icon-{Guid.NewGuid():N}.png");
			var entries = await UploadAsync(http, name, $"banner-{Guid.NewGuid():N}.png");
			var icon = entries.Single(e => e.IsIcon);
			var banner = entries[^1];

			var replaced = await http.PutAsJsonAsync(url, entries
				.Select(e => e.AssetId == banner.AssetId ? e with { IsBanner = true } : e)
				.Select(e => e.AssetId == icon.AssetId ? e with { Caption = "God at dusk" } : e)
				.ToList());
			await Assert.That(replaced.StatusCode).IsEqualTo(HttpStatusCode.OK);

			await Assert.That(await ReadAttributeAsync("IMAGE")).IsEqualTo(icon.Url);
			await Assert.That(await ReadAttributeAsync("IMAGE`BANNER")).IsEqualTo(banner.Url);
			await Assert.That(await ReadAttributeAsync("IMAGE`ALT")).IsEqualTo("God at dusk");

			var deleted = await http.DeleteAsync($"{url}/{Uri.EscapeDataString(banner.AssetId)}");
			await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(await ReadAttributeAsync("IMAGE`BANNER")).IsEqualTo(string.Empty)
				.Because("deleting the banner image leaves no banner, and the attribute must say so");
			await Assert.That(await ReadAttributeAsync("IMAGE")).IsEqualTo(icon.Url);
		}
		finally
		{
			var now = await http.GetFromJsonAsync<List<GalleryEntry>>(url) ?? [];
			foreach (var entry in now.Where(e => before.All(b => b.AssetId != e.AssetId)))
			{
				await http.DeleteAsync($"{url}/{Uri.EscapeDataString(entry.AssetId)}");
			}
		}
	}
}
