namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>Discovery metadata only; configuration values remain behind the destination's authorization.</summary>
public sealed record ApplicationConfigurationTile(
	string Id, string Title, string Description, string? Icon, string Route, string Permission)
{
	/// <summary>Optional field metadata; init-only preserves the original six-argument plugin ABI.</summary>
	public IReadOnlyList<ConfigurationOption>? Options { get; init; }

	/// <summary>Only canonical same-origin admin/application paths, without query strings or escapes.</summary>
	public bool HasSafeRoute() => ConfigurationOptionRoutes.IsSafe(Route);

	public bool Equals(ApplicationConfigurationTile? other) => other is not null
		&& Id == other.Id && Title == other.Title && Description == other.Description && Icon == other.Icon
		&& Route == other.Route && Permission == other.Permission && (Options ?? []).SequenceEqual(other.Options ?? []);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Id); hash.Add(Title); hash.Add(Description); hash.Add(Icon); hash.Add(Route); hash.Add(Permission);
		foreach (var option in Options ?? []) hash.Add(option);
		return hash.ToHashCode();
	}

	public bool HasValidOptions() => Options is null || Options.Count <= 128
		&& Options.All(option => option is not null && option.IsValid())
		&& Options.Select(option => option.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Options.Count;
}
