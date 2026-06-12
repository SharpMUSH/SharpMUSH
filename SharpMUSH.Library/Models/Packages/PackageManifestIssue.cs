namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A problem found while parsing or validating a package manifest.
/// Errors make the manifest unusable; warnings accompany a successful parse.
/// </summary>
/// <param name="Severity">Whether this issue blocks use of the manifest.</param>
/// <param name="Path">Dotted location within the document (e.g. <c>objects[2].attributes.FN_READ</c>), or empty for document-level issues.</param>
/// <param name="Message">Human-readable description of the problem.</param>
public sealed record PackageManifestIssue(
	PackageManifestIssueSeverity Severity,
	string Path,
	string Message)
{
	public static PackageManifestIssue Error(string path, string message) =>
		new(PackageManifestIssueSeverity.Error, path, message);

	public static PackageManifestIssue Warning(string path, string message) =>
		new(PackageManifestIssueSeverity.Warning, path, message);

	public override string ToString() =>
		Path.Length == 0 ? $"{Severity}: {Message}" : $"{Severity} at {Path}: {Message}";
}

/// <summary>Severity of a <see cref="PackageManifestIssue"/>.</summary>
public enum PackageManifestIssueSeverity
{
	Error,
	Warning
}

/// <summary>
/// Failure result from manifest parsing: the document could not be turned
/// into a usable <see cref="PackageManifest"/>. Contains at least one error,
/// plus any warnings gathered before parsing stopped.
/// </summary>
/// <param name="Issues">All issues found, errors and warnings.</param>
public sealed record PackageManifestFailure(IReadOnlyList<PackageManifestIssue> Issues)
{
	/// <summary>Only the error-severity issues.</summary>
	public IEnumerable<PackageManifestIssue> Errors =>
		Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error);
}

/// <summary>
/// Success result from manifest parsing: a valid manifest plus any
/// non-blocking warnings (e.g. undeclared <c>?configure</c> tokens).
/// </summary>
/// <param name="Manifest">The parsed, validated manifest.</param>
/// <param name="Warnings">Warning-severity issues; never contains errors.</param>
public sealed record ParsedPackageManifest(
	PackageManifest Manifest,
	IReadOnlyList<PackageManifestIssue> Warnings);
