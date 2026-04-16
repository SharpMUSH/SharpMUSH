using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using System.Text;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Localization service backed by the embedded Notifications.resx resource file.
/// The ResourceManager automatically falls back to the neutral (English) resource
/// when no culture-specific file exists for the requested locale.
/// </summary>
public class LocalizationService : ILocalizationService
{
	private readonly ResourceManager _resourceManager;
	private readonly ConcurrentDictionary<string, CompositeFormat> _formatCache = new();

	public LocalizationService()
	{
		// The resource name is: RootNamespace + "." + folder + "." + filename-without-extension
		// For SharpMUSH.Library/Resources/Notifications.resx → "SharpMUSH.Library.Resources.Notifications"
		_resourceManager = new ResourceManager(
			"SharpMUSH.Library.Resources.Notifications",
			typeof(LocalizationService).Assembly);

		AvailableLocales = DiscoverAvailableLocales();
	}

	/// <inheritdoc />
	public IReadOnlyList<string> AvailableLocales { get; }

	/// <inheritdoc />
	public string Get(string key, string? locale = null)
	{
		var culture = ResolveCulture(locale);
		return _resourceManager.GetString(key, culture) ?? key;
	}

	/// <inheritdoc />
	public string Format(string key, string? locale, params object[] args)
	{
		var template = Get(key, locale);
		if (args.Length == 0)
			return template;

		var compositeFormat = _formatCache.GetOrAdd(template, static t => CompositeFormat.Parse(t));
		return string.Format(null, compositeFormat, args);
	}

	/// <inheritdoc />
	public bool HasTranslation(string key, string locale)
	{
		try
		{
			var culture = CultureInfo.GetCultureInfo(locale);
			return _resourceManager.GetString(key, culture) is not null;
		}
		catch (CultureNotFoundException)
		{
			return false;
		}
		catch (MissingManifestResourceException)
		{
			return false;
		}
	}

	/// <summary>
	/// Maps a locale tag to a <see cref="CultureInfo"/>.
	/// Null / empty / "en" all resolve to <see cref="CultureInfo.InvariantCulture"/>
	/// so that <see cref="ResourceManager"/> returns the neutral (English) resource.
	/// </summary>
	private static CultureInfo ResolveCulture(string? locale)
	{
		if (string.IsNullOrEmpty(locale) || locale.Equals("en", StringComparison.OrdinalIgnoreCase))
			return CultureInfo.InvariantCulture;

		try
		{
			return CultureInfo.GetCultureInfo(locale);
		}
		catch (CultureNotFoundException)
		{
			return CultureInfo.InvariantCulture;
		}
	}

	/// <summary>
	/// Discovers available locales by scanning for satellite assembly directories
	/// next to the executing assembly (e.g. <c>fr/SharpMUSH.Library.resources.dll</c>).
	/// Always includes "en" as the baseline neutral locale.
	/// </summary>
	private static IReadOnlyList<string> DiscoverAvailableLocales()
	{
		var locales = new List<string> { "en" };

		var assemblyLocation = typeof(LocalizationService).Assembly.Location;
		if (string.IsNullOrEmpty(assemblyLocation))
			return locales;

		var baseDir = Path.GetDirectoryName(assemblyLocation);
		if (baseDir is null || !Directory.Exists(baseDir))
			return locales;

		var satelliteName = Path.GetFileNameWithoutExtension(assemblyLocation) + ".resources.dll";

		foreach (var subDir in Directory.EnumerateDirectories(baseDir))
		{
			var dirName = Path.GetFileName(subDir);
			if (File.Exists(Path.Combine(subDir, satelliteName)))
			{
				try
				{
					// Verify this is actually a valid culture name
					_ = CultureInfo.GetCultureInfo(dirName);
					locales.Add(dirName);
				}
				catch (CultureNotFoundException)
				{
					// Not a valid culture directory — skip
				}
			}
		}

		return locales;
	}
}
