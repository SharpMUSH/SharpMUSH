using System.Globalization;
using System.Resources;
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

	public LocalizationService()
	{
		// The resource name is: RootNamespace + "." + folder + "." + filename-without-extension
		// For SharpMUSH.Library/Resources/Notifications.resx → "SharpMUSH.Library.Resources.Notifications"
		_resourceManager = new ResourceManager(
			"SharpMUSH.Library.Resources.Notifications",
			typeof(LocalizationService).Assembly);
	}

	/// <inheritdoc />
	public IReadOnlyList<string> AvailableLocales { get; } = ["en"];

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
		return args.Length > 0 ? string.Format(template, args) : template;
	}

	/// <inheritdoc />
	public bool HasTranslation(string key, string locale)
	{
		try
		{
			var culture = CultureInfo.GetCultureInfo(locale);
			return _resourceManager.GetString(key, culture) is not null;
		}
		catch
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
}
