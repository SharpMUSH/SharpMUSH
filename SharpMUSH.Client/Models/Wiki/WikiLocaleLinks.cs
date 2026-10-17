namespace SharpMUSH.Client.Models.Wiki;

/// <summary>Keeps the reader's explicit wiki locale on navigation links without overriding default-locale requests.</summary>
public static class WikiLocaleLinks
{
	public static string? FromUri(string uri)
	{
		var locale = System.Web.HttpUtility.ParseQueryString(new Uri(uri).Query)["lang"];
		return string.IsNullOrWhiteSpace(locale) ? null : locale;
	}

	public static string WithLocale(string path, string? locale) => string.IsNullOrWhiteSpace(locale)
		? path
		: path + (path.Contains('?') ? "&" : "?") + "lang=" + Uri.EscapeDataString(locale);
}
