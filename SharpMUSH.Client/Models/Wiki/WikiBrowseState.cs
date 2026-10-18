using System.Globalization;

namespace SharpMUSH.Client.Models.Wiki;

/// <summary>Submitted collection browsing state. URLs carry destinations, never authorization.</summary>
public sealed record WikiBrowseState(string Collection, string? Search = null, string? Category = null,
	int Skip = 0, string? Locale = null)
{
	public const int PageSize = 50;
	public const int MaxSearchLength = 200;
	private const int LastOffset = int.MaxValue / PageSize * PageSize;

	public WikiBrowseState Normalize() => this with
	{
		Collection = string.IsNullOrWhiteSpace(Collection) ? "game" : Collection.Trim(),
		Search = Text(Search, MaxSearchLength),
		Category = Text(Category, 200),
		Skip = Skip < 0 ? 0 : Skip / PageSize * PageSize,
		Locale = string.IsNullOrWhiteSpace(Locale) ? null : Locale.Trim()
	};

	public WikiBrowseState Previous()
	{
		var state = Normalize();
		return state with { Skip = Math.Max(0, state.Skip - PageSize) };
	}

	public WikiBrowseState Next()
	{
		var state = Normalize();
		return state with { Skip = state.Skip >= LastOffset ? LastOffset : checked(state.Skip + PageSize) };
	}

	public static int ParseSkip(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
		? Math.Max(0, offset) / PageSize * PageSize : 0;

	public static WikiBrowseState FromUri(string uri, string? collection = null)
	{
		var query = ReadQuery(uri);
		if (collection is null)
		{
			var path = new Uri(uri, UriKind.Absolute).AbsolutePath;
			collection = path.StartsWith("/wiki/collections/", StringComparison.Ordinal)
				? Uri.UnescapeDataString(path["/wiki/collections/".Length..]) : "game";
		}
		return new WikiBrowseState(collection, query.GetValueOrDefault("q"), query.GetValueOrDefault("category"),
			ParseSkip(query.GetValueOrDefault("skip")), query.GetValueOrDefault("lang")).Normalize();
	}

	/// <summary>Reads raw values before typed conversion; the last occurrence wins, including an empty value.</summary>
	public static Dictionary<string, string> ReadQuery(string uri)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var fragment = uri.IndexOf('#');
		if (fragment >= 0) uri = uri[..fragment];
		var start = uri.IndexOf('?');
		if (start < 0) return result;
		foreach (var pair in uri[(start + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var equals = pair.IndexOf('=');
			var key = equals < 0 ? pair : pair[..equals];
			var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
			result[Decode(key)] = Decode(value);
		}
		return result;
	}

	private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
	private static string? Text(string? value, int length)
	{
		value = value?.Trim();
		if (string.IsNullOrEmpty(value)) return null;
		if (value.Length <= length) return value;
		if (char.IsHighSurrogate(value[length - 1])) length--;
		return value[..length].TrimEnd();
	}
}
