using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>How a path to a file a package carries is written: relative, with <c>/</c> between folders.</summary>
public static class PackageFilePath
{
	/// <summary>
	/// <paramref name="path"/> as every <see cref="IPackageFileSource"/> takes it, or null when it would leave the
	/// package directory or is not a plain relative path: no leading <c>/</c>, no <c>\</c>, no drive, no empty,
	/// <c>.</c> or <c>..</c> part, and no control character.
	/// </summary>
	public static string? Normalize(string? path)
	{
		var trimmed = path?.Trim() ?? "";
		if (trimmed.Length == 0 || trimmed.StartsWith('/') || trimmed.Contains('\\') || trimmed.Contains(':')
			|| trimmed.Any(char.IsControl))
		{
			return null;
		}

		var parts = trimmed.Split('/');
		return parts.Any(part => part is "" or "." or "..") ? null : string.Join('/', parts);
	}
}

/// <summary>An <see cref="IPackageFileSource"/> over a package directory on disk, which never reads outside it.</summary>
public sealed class DirectoryPackageFileSource(string directory) : IPackageFileSource
{
	private readonly string _root = Path.GetFullPath(directory);

	public async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		if (PackageFilePath.Normalize(path) is not { } normalized)
		{
			return null;
		}

		// Join, not Combine: Combine drops the root when the second part is rooted. Normalize refuses a rooted path, and
		// the containment check below holds whatever it lets through.
		var full = Path.GetFullPath(Path.Join(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
		var inside = full.StartsWith(_root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar,
			StringComparison.Ordinal);
		if (!inside || !File.Exists(full) || new FileInfo(full).LinkTarget is not null)
		{
			return null;
		}

		return await File.ReadAllBytesAsync(full, cancellationToken);
	}
}

/// <summary>
/// Reads the bodies of a manifest's <c>file:</c> wiki pages, once, before the manifest is planned: the plan
/// carries the text, so the review screen and the apply see the same pages.
/// </summary>
public static class PackageWikiBodies
{
	/// <summary>The largest page body a package may carry.</summary>
	public const int MaxBodyBytes = 1024 * 1024;

	/// <summary>True when a <c>file:</c> page has yet to be read.</summary>
	public static bool NeedsFiles(PackageManifest manifest) => manifest.WikiPages.Any(page => page.Markdown is null);

	/// <summary>
	/// <paramref name="manifest"/> with every page's body read from <paramref name="files"/>, and the warnings about
	/// those bodies' links; or every file that is missing, too large or not UTF-8 text, and every link in them that
	/// does not name its page in full (<see cref="PackageWikiLinks"/>). Inline bodies were checked when the manifest
	/// was read.
	/// </summary>
	public static async Task<Result<PackageWikiLoad>> LoadAsync(
		PackageManifest manifest, IPackageFileSource? files, CancellationToken cancellationToken = default)
	{
		if (!NeedsFiles(manifest))
		{
			return new PackageWikiLoad(manifest, []);
		}

		if (files is null)
		{
			return new Error<string>($"{manifest.Name} ships wiki pages in files, and there is nothing to read them from.");
		}

		var problems = new List<string>();
		var pages = new List<PackageWikiPageSpec>();
		var read = new List<(PackageWikiPageSpec Page, string File)>();
		foreach (var page in manifest.WikiPages)
		{
			if (page.Markdown is not null || page.File is null)
			{
				pages.Add(page);
				continue;
			}

			switch (await ReadBodyAsync(files, page.File, cancellationToken))
			{
				case string markdown:
					var loaded = page with { Markdown = markdown };
					pages.Add(loaded);
					read.Add((loaded, page.File));
					break;
				case Error<string> error:
					problems.Add($"{page.Label}: {error.Value}");
					break;
			}
		}

		var issues = read.SelectMany(body => PackageWikiLinks.Check(body.Page, manifest.WikiPages, body.File)).ToList();
		problems.AddRange(issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).Select(i => i.ToString()));

		return problems.Count > 0
			? new Error<string>($"Wiki pages cannot be read: {string.Join("; ", problems)}")
			: new PackageWikiLoad(manifest with { Wiki = pages },
				issues.Where(i => i.Severity != PackageManifestIssueSeverity.Error).ToList());
	}

	private static async Task<Result<string>> ReadBodyAsync(IPackageFileSource files, string path, CancellationToken cancellationToken)
	{
		if (await files.ReadFileAsync(path, cancellationToken) is not { } bytes)
		{
			return new Error<string>($"{path} is not in the package.");
		}

		if (bytes.Length > MaxBodyBytes)
		{
			return new Error<string>($"{path} is larger than {MaxBodyBytes / 1024} KB.");
		}

		try
		{
			var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
			return PackageWikiContent.NormalizeMarkdown(text.TrimStart('﻿'));
		}
		catch (DecoderFallbackException)
		{
			return new Error<string>($"{path} is not UTF-8 text.");
		}
	}
}

/// <summary>A manifest with its wiki bodies read, and the warnings about their links.</summary>
/// <param name="Manifest">The manifest, every page carrying its body.</param>
/// <param name="Warnings">Links that look like a typo for a page the package ships.</param>
public sealed record PackageWikiLoad(PackageManifest Manifest, IReadOnlyList<PackageManifestIssue> Warnings);
