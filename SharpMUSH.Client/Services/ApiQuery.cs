namespace SharpMUSH.Client.Services;

/// <summary>Builds a request path with its query string.</summary>
public static class ApiQuery
{
	/// <summary>
	/// <paramref name="path"/> followed by each parameter that has a value, escaped. A null or empty value
	/// is left out, so an unset filter is never sent as an empty one.
	/// </summary>
	public static string Build(string path, params ReadOnlySpan<(string Name, string? Value)> parameters)
	{
		var pairs = new List<string>(parameters.Length);
		foreach (var (name, value) in parameters)
		{
			if (!string.IsNullOrEmpty(value))
				pairs.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
		}

		return pairs.Count == 0 ? path : $"{path}?{string.Join('&', pairs)}";
	}
}
