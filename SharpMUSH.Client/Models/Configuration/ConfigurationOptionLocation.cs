using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Client.Models.Configuration;

/// <summary>One declared field key, never a selector, read from a configuration deep link.</summary>
public static class ConfigurationOptionLocation
{
	private const string Prefix = "#config-option-";

	public static string? Read(string uri)
	{
		var fragment = new Uri(uri).Fragment;
		if (!fragment.StartsWith(Prefix, StringComparison.Ordinal)) return null;
		var key = Uri.UnescapeDataString(fragment[Prefix.Length..]);
		return ConfigurationOption.ValidKey(key) ? key : null;
	}

	/// <summary>Field identification is presentation state and never changes an application's request.</summary>
	public static string LoadIdentity(string uri) => Read(uri) is null ? uri : uri[..uri.IndexOf('#')];
}
