using SharpMUSH.Library.Services;

namespace SharpMUSH.Client.Models.Wiki;

/// <summary>Builds local wiki journeys from structured state; no free-form return URL is accepted.</summary>
public static class WikiJourneyLinks
{
	private static readonly string[] ContextKeys =
		["browseCollection", "browseQ", "browseCategory", "browseSkip", "membersCollection", "lang", "returnUrl"];

	public static string Collection(WikiBrowseState state)
	{
		state = state.Normalize();
		var query = new Dictionary<string, string>();
		Add(query, "q", state.Search);
		Add(query, "category", state.Category);
		if (state.Skip != 0) query["skip"] = state.Skip.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Add(query, "lang", state.Locale);
		return Append("/wiki/collections/" + Uri.EscapeDataString(state.Collection), query);
	}

	/// <summary>Canonical page destinations include biographies on character profiles outside the wiki.</summary>
	public static string Page(string ns, string slug, WikiBrowseState? state, string? membersCollection = null,
		int? membersSkip = null, string? locale = null)
	{
		var path = WikiRoutes.PathFor(ns, slug);
		return WikiRoutes.IsCharacterProfile(ns)
			? WikiLocaleLinks.WithLocale(path, locale ?? state?.Normalize().Locale)
			: Article(path, state, membersCollection, membersSkip, locale);
	}

	public static string Article(string path, WikiBrowseState? state, string? membersCollection = null,
		int? membersSkip = null, string? locale = null)
	{
		state = state?.Normalize();
		if (!path.StartsWith("/wiki/", StringComparison.Ordinal)) path = "/wiki";
		var fragmentStart = path.IndexOf('#');
		var fragment = fragmentStart < 0 ? string.Empty : path[fragmentStart..];
		if (fragmentStart >= 0) path = path[..fragmentStart];
		var query = WikiBrowseState.ReadQuery(path);
		var authoredLocale = state is null && locale is null ? query.GetValueOrDefault("lang") : null;
		var queryStart = path.IndexOf('?');
		if (queryStart >= 0) path = path[..queryStart];
		foreach (var key in ContextKeys) query.Remove(key);
		if (state is not null)
		{
			query["browseCollection"] = state.Collection;
			Add(query, "browseQ", state.Search);
			Add(query, "browseCategory", state.Category);
			if (state.Skip != 0) query["browseSkip"] = state.Skip.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		Add(query, "membersCollection", membersCollection);
		if (membersSkip is { } offset)
		{
			query.Remove("membersSkip");
			offset = Math.Max(0, offset) / WikiBrowseState.PageSize * WikiBrowseState.PageSize;
			if (offset != 0) query["membersSkip"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		Add(query, "lang", locale ?? state?.Locale ?? authoredLocale);
		return Append(path, query) + fragment;
	}

	private static void Add(Dictionary<string, string> query, string key, string? value)
	{
		if (!string.IsNullOrEmpty(value)) query[key] = value;
	}

	private static string Append(string path, Dictionary<string, string> query) => query.Count == 0 ? path
		: path + "?" + string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
}
