using Microsoft.Extensions.Configuration;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Filesystem-backed <see cref="IWikiAssetService"/>.
/// Each asset is stored as <c>{id}.bin</c> (raw bytes) plus a sidecar <c>{id}.json</c>
/// (serialized <see cref="WikiAsset"/> metadata) under a configurable root directory
/// (config key <c>Wiki:AssetRoot</c>; defaults to <c>wiki-assets</c> next to the app binaries).
/// </summary>
public partial class FileSystemWikiAssetService : IWikiAssetService
{
	private readonly string _root;

	[GeneratedRegex("[^a-zA-Z0-9._-]")]
	private static partial Regex DisallowedFileNameChars();

	public FileSystemWikiAssetService(IConfiguration configuration)
		: this(configuration["Wiki:AssetRoot"] ?? Path.Combine(AppContext.BaseDirectory, "wiki-assets"))
	{
	}

	/// <summary>Test-friendly constructor taking the storage root directly.</summary>
	public FileSystemWikiAssetService(string root)
	{
		_root = root;
		Directory.CreateDirectory(_root);
	}

	/// <summary>
	/// Strips any path components and replaces characters outside [a-zA-Z0-9._-].
	/// Returns "file" when nothing usable remains.
	/// </summary>
	public static string SanitizeFileName(string fileName)
	{
		var name = fileName.Replace('\\', '/');
		var lastSlash = name.LastIndexOf('/');
		if (lastSlash >= 0)
		{
			name = name[(lastSlash + 1)..];
		}

		name = DisallowedFileNameChars().Replace(name, "_").Trim('.');
		return string.IsNullOrEmpty(name) ? "file" : name;
	}

	private string BinPath(string id) => Path.Combine(_root, $"{id}.bin");
	private string MetaPath(string id) => Path.Combine(_root, $"{id}.json");

	/// <summary>Ids are Guid "N" strings; reject anything else so ids can't traverse paths.</summary>
	private static bool IsValidId(string id) => Guid.TryParseExact(id, "N", out _);

	public async Task<OneOf<WikiAsset, Error<string>>> SaveAsync(
		string fileName,
		string contentType,
		Stream content,
		string uploaderDbref,
		CancellationToken ct = default)
	{
		var id = Guid.NewGuid().ToString("N");
		var binPath = BinPath(id);

		try
		{
			long size;
			string sha256;
			await using (var file = File.Create(binPath))
			using (var hasher = SHA256.Create())
			{
				await using (var hashStream = new CryptoStream(file, hasher, CryptoStreamMode.Write, leaveOpen: true))
				{
					await content.CopyToAsync(hashStream, ct);
				}

				size = file.Length;
				sha256 = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
			}

			var asset = new WikiAsset(
				id,
				SanitizeFileName(fileName),
				contentType,
				size,
				sha256,
				uploaderDbref,
				DateTimeOffset.UtcNow);

			await File.WriteAllTextAsync(MetaPath(id), JsonSerializer.Serialize(asset), ct);
			return asset;
		}
		catch (Exception ex)
		{
			try { File.Delete(binPath); } catch { /* ignore */ }
			try { File.Delete(MetaPath(id)); } catch { /* ignore */ }
			return new Error<string>($"Failed to store asset: {ex.Message}");
		}
	}

	public async Task<OneOf<(WikiAsset Asset, Stream Content), NotFound>> OpenAsync(string id, CancellationToken ct = default)
	{
		if (!IsValidId(id) || !File.Exists(MetaPath(id)) || !File.Exists(BinPath(id)))
		{
			return new NotFound();
		}

		var json = await File.ReadAllTextAsync(MetaPath(id), ct);
		var asset = JsonSerializer.Deserialize<WikiAsset>(json);
		if (asset is null)
		{
			return new NotFound();
		}

		Stream stream = File.OpenRead(BinPath(id));
		return (asset, stream);
	}

	public async Task<IReadOnlyList<WikiAsset>> ListAsync(int skip = 0, int take = 100)
	{
		var assets = new List<WikiAsset>();
		foreach (var metaFile in Directory.EnumerateFiles(_root, "*.json"))
		{
			try
			{
				var asset = JsonSerializer.Deserialize<WikiAsset>(await File.ReadAllTextAsync(metaFile));
				if (asset is not null)
				{
					assets.Add(asset);
				}
			}
			catch (JsonException)
			{
			}
		}

		return assets
			.OrderByDescending(a => a.UploadedAt)
			.Skip(skip)
			.Take(take)
			.ToList();
	}

	public Task<OneOf<None, NotFound>> DeleteAsync(string id)
	{
		if (!IsValidId(id) || !File.Exists(MetaPath(id)))
		{
			return Task.FromResult<OneOf<None, NotFound>>(new NotFound());
		}

		File.Delete(MetaPath(id));
		if (File.Exists(BinPath(id)))
		{
			File.Delete(BinPath(id));
		}

		return Task.FromResult<OneOf<None, NotFound>>(new None());
	}
}
