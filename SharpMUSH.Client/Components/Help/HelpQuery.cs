namespace SharpMUSH.Client.Components.Help;

/// <summary>The help index's filter as its address carries it: <c>/help?q=term</c>.</summary>
public static class HelpQuery
{
	/// <summary>The decoded <c>q</c> value of <paramref name="uri"/>, or empty.</summary>
	public static string From(string uri)
	{
		foreach (var pair in new Uri(uri).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = pair.IndexOf('=');
			if (eq > 0 && pair[..eq] == "q")
			{
				return Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')).Trim();
			}
		}
		return string.Empty;
	}
}
