using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Reads the stylesheets and pictures a theme package's themes name (format 1.21), once, before the manifest is
/// planned: the plan carries them, so the review screen and the apply see the same files. Each stylesheet is checked
/// as a staff-written one is (<see cref="ThemeStylesheet.Validate"/>), with its pictures named by their path in the
/// package.
/// </summary>
public static partial class PackageThemeFiles
{
	/// <summary>The largest picture a theme may carry.</summary>
	public const int MaxImageBytes = 4 * 1024 * 1024;

	/// <summary>The most picture bytes one package may carry in all.</summary>
	public const long MaxTotalBytes = 32L * 1024 * 1024;

	/// <summary>
	/// The address a stylesheet's pictures are checked against before the game has stored them; the apply replaces it
	/// with where each is served.
	/// </summary>
	private const string CheckedAddress = "/api/wiki-assets/package-theme/";

	/// <summary>True when a theme's stylesheet or a picture has yet to be read.</summary>
	public static bool NeedsFiles(PackageManifest manifest) => manifest.PortalThemes.Any(theme => !theme.Loaded);

	/// <summary>
	/// <paramref name="manifest"/> with every theme's stylesheet and pictures read from <paramref name="files"/>, or
	/// every file that is missing, too large, not UTF-8 text, a picture that is not what its name says or an SVG that
	/// runs script, and every stylesheet problem.
	/// </summary>
	public static async Task<Result<PackageManifest>> LoadAsync(
		PackageManifest manifest, IPackageFileSource? files, CancellationToken cancellationToken = default)
	{
		if (!NeedsFiles(manifest))
		{
			return manifest;
		}

		if (files is null)
		{
			return new Error<string>($"{manifest.Name} ships theme files, and there is nothing to read them from.");
		}

		var problems = new List<string>();
		var themes = new List<PackageThemeSpec>();
		var remaining = MaxTotalBytes;
		foreach (var theme in manifest.PortalThemes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var images = new List<PackageThemeImage>();
			foreach (var image in theme.Images)
			{
				switch (await files.ReadFileAsync(image.File, Math.Min(MaxImageBytes, remaining), cancellationToken))
				{
					case byte[] bytes when Problem(image.File, bytes) is { } problem:
						problems.Add($"{theme.Id}: {problem}");
						break;
					case byte[] bytes:
						remaining -= bytes.LongLength;
						images.Add(image with { Content = bytes });
						break;
					case NotFound:
						problems.Add($"{theme.Id}: {image.File} is not in the package.");
						break;
					case PackageFileTooLarge large when large.Length > MaxImageBytes:
						problems.Add($"{theme.Id}: {image.File} is larger than {MaxImageBytes / (1024 * 1024)} MB.");
						break;
					case PackageFileTooLarge:
						return new Error<string>(
							$"Theme files cannot be read: the package's pictures are larger than {MaxTotalBytes / (1024 * 1024)} MB in all.");
					case Error<string> error:
						return new Error<string>($"Theme files cannot be read: {error.Value}");
				}
			}

			string? stylesheet = null;
			if (theme.StylesheetFile is { } sheetFile)
			{
				switch (await files.ReadFileAsync(sheetFile, ThemeStylesheet.MaxLength * 4L, cancellationToken))
				{
					case byte[] bytes:
						switch (Decode(sheetFile, bytes))
						{
							case string css:
								stylesheet = css;
								problems.AddRange(CheckStylesheet(css, theme).Select(problem => $"{theme.Id}: {sheetFile}: {problem}"));
								break;
							case Error<string> error:
								problems.Add($"{theme.Id}: {error.Value}");
								break;
						}

						break;
					case NotFound:
						problems.Add($"{theme.Id}: {sheetFile} is not in the package.");
						break;
					case PackageFileTooLarge:
						problems.Add($"{theme.Id}: {sheetFile} is longer than a stylesheet may be ({ThemeStylesheet.MaxLength} characters).");
						break;
					case Error<string> error:
						return new Error<string>($"Theme files cannot be read: {error.Value}");
				}
			}

			themes.Add(theme with { Images = images, Stylesheet = stylesheet });
		}

		return problems.Count > 0
			? new Error<string>($"Theme files cannot be used: {string.Join(" ", problems)}")
			: manifest with { Themes = themes };
	}

	/// <summary>The bytes the manifest's loaded pictures and stylesheets hold, for a bound on what a plan keeps in memory.</summary>
	public static long LoadedBytes(PackageManifest manifest)
		=> manifest.PortalThemes.Sum(theme => theme.Images.Sum(image => (long)(image.Content?.Length ?? 0))
			+ 2L * (theme.Stylesheet?.Length ?? 0));

	/// <summary>
	/// <paramref name="css"/> with each picture <paramref name="theme"/> carries pointed at <paramref name="address"/>'s
	/// answer for it; every other <c>url()</c> is left as written.
	/// </summary>
	public static string PointAt(string css, PackageThemeSpec theme, Func<PackageThemeImage, string> address)
		=> ThemeStylesheet.RewriteUrls(css, target => Carried(theme, target) is { } image ? address(image) : null);

	/// <summary>The problems with a packaged stylesheet: a picture it names that the theme does not carry, then what <see cref="ThemeStylesheet.Validate"/> finds.</summary>
	public static IReadOnlyList<string> CheckStylesheet(string css, PackageThemeSpec theme)
	{
		var problems = ThemeStylesheet.UrlTargets(css)
			.Where(target => IsPackagePath(target) && Carried(theme, target) is null)
			.Distinct(StringComparer.Ordinal)
			.Select(target => $"url({target}) names a picture the theme does not list in 'images'.")
			.ToList();
		// A path the theme does not carry is reported above, so it is checked as if it were stored, not again as a
		// foreign address.
		problems.AddRange(ThemeStylesheet.Validate(ThemeStylesheet.RewriteUrls(css,
			target => Carried(theme, target) is { } image ? CheckedAddress + image.File
				: IsPackagePath(target) && SafePath().IsMatch(target) ? CheckedAddress + target
				: null)));
		return problems;
	}

	/// <summary>The picture <paramref name="theme"/> carries at <paramref name="target"/> (a path in the package, <c>./</c> allowed), or null.</summary>
	private static PackageThemeImage? Carried(PackageThemeSpec theme, string target)
		=> PackageFilePath.Normalize(target.StartsWith("./", StringComparison.Ordinal) ? target[2..] : target) is { } path
			? theme.Images.FirstOrDefault(image => image.File == path)
			: null;

	/// <summary>The characters a theme's file path may use: nothing that could end a quoted <c>url()</c> or start a comment.</summary>
	[GeneratedRegex("^[A-Za-z0-9._/-]+$")]
	public static partial Regex SafePath();

	/// <summary>Whether a <c>url()</c> target is a path in the package rather than an address or a data URL.</summary>
	private static bool IsPackagePath(string target)
		=> target.Length > 0 && !target.StartsWith('/') && !target.StartsWith('#') && !target.Contains(':');

	/// <summary>Why <paramref name="bytes"/> cannot be the picture <paramref name="file"/> says it is, or null.</summary>
	private static string? Problem(string file, byte[] bytes)
	{
		var type = PackageThemeImageTypes.For(file);
		if (bytes.Length == 0)
		{
			return $"{file} is empty.";
		}

		if (type == "image/svg+xml")
		{
			string text;
			try
			{
				text = new UTF8Encoding(false, true).GetString(bytes);
			}
			catch (DecoderFallbackException)
			{
				return $"{file} is not UTF-8 text.";
			}

			return !text.Contains("<svg", StringComparison.OrdinalIgnoreCase) ? $"{file} is not an SVG picture."
				: SvgContent.LooksActive(text) ? $"{file} holds script, which a theme's picture may not."
				: null;
		}

		return Matches(type, bytes) ? null : $"{file} is not the kind of picture its name says.";
	}

	/// <summary>Whether <paramref name="bytes"/> start the way a file of <paramref name="type"/> does.</summary>
	private static bool Matches(string? type, byte[] bytes)
	{
		bool Starts(params byte[] magic) => bytes.Length >= magic.Length && bytes.AsSpan(0, magic.Length).SequenceEqual(magic);
		bool At(int offset, string text) => bytes.Length >= offset + text.Length
			&& bytes.AsSpan(offset, text.Length).SequenceEqual(Encoding.ASCII.GetBytes(text));

		return type switch
		{
			"image/png" => Starts(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
			"image/jpeg" => Starts(0xFF, 0xD8, 0xFF),
			"image/gif" => At(0, "GIF87a") || At(0, "GIF89a"),
			"image/webp" => At(0, "RIFF") && At(8, "WEBP"),
			"image/avif" => At(4, "ftyp") && (At(8, "avif") || At(8, "avis")),
			_ => false
		};
	}

	private static Result<string> Decode(string path, byte[] bytes)
	{
		try
		{
			var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿').Replace("\r\n", "\n");
			return text.Length > ThemeStylesheet.MaxLength
				? new Error<string>($"{path} is longer than a stylesheet may be ({ThemeStylesheet.MaxLength} characters).")
				: text;
		}
		catch (DecoderFallbackException)
		{
			return new Error<string>($"{path} is not UTF-8 text.");
		}
	}
}

/// <summary>Whether SVG text could run script when opened on its own.</summary>
public static partial class SvgContent
{
	/// <summary>
	/// True when <paramref name="svg"/> could run script or load a page: a <c>script</c> or <c>foreignObject</c> element
	/// in any namespace, an event handler attribute, a link that is not to a fragment or a <c>data:image</c> URL, or a
	/// <c>javascript:</c> URL anywhere once XML entities are read. SVG that is not well-formed XML, or declares a DTD,
	/// counts as active: what a browser makes of it cannot be checked.
	/// </summary>
	public static bool LooksActive(string svg)
	{
		if (ActivePattern().IsMatch(svg))
		{
			return true;
		}

		try
		{
			using var reader = XmlReader.Create(new StringReader(svg),
				new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.Element && ActiveElement(reader.LocalName))
				{
					return true;
				}

				if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA && Scripted(reader.Value))
				{
					return true;
				}

				if (reader.NodeType == XmlNodeType.Element && reader.MoveToFirstAttribute())
				{
					do
					{
						if (reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
							|| Scripted(reader.Value)
							|| reader.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase) && !SafeLink(reader.Value))
						{
							return true;
						}
					} while (reader.MoveToNextAttribute());

					reader.MoveToElement();
				}
			}

			return false;
		}
		catch (XmlException)
		{
			return true;
		}
	}

	private static bool ActiveElement(string name)
		=> name.Equals("script", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("foreignObject", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("handler", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("iframe", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("embed", StringComparison.OrdinalIgnoreCase)
			|| name.Equals("object", StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether a value names a script URL once whitespace and control characters are taken out, as a browser does.</summary>
	private static bool Scripted(string value)
	{
		var squeezed = new string(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
		return squeezed.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
			|| squeezed.Contains("vbscript:", StringComparison.OrdinalIgnoreCase);
	}

	private static bool SafeLink(string value)
	{
		var link = value.Trim();
		return link.StartsWith('#') || link.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && !link.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase);
	}

	[GeneratedRegex(@"<script|javascript:|on\w+\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex ActivePattern();
}
