using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A <c>{{...}}</c> token found in manifest text. <see cref="Ref"/> is null
/// when the token body is not a valid ref — which the manifest validator
/// treats as a hard error (decision 20.11: every double-braced token must be
/// a valid ref; literal <c>{{</c> must be escaped as <c>{{{{</c>).
/// </summary>
/// <param name="Raw">The token text including braces, as written.</param>
/// <param name="Ref">The parsed ref, or null when the body is malformed.</param>
public sealed record PackageRefToken(string Raw, PackageRef? Ref);

/// <summary>
/// Finds mustache refs in attribute values, lock values, and ref-only fields
/// (decision 20.11). Shared by manifest validation and the apply engine's
/// substitution pass.
/// </summary>
public static partial class PackageRefScanner
{
	// Exactly-double braces only: the lookarounds reject any token adjacent to
	// a third brace, which is what makes {{{{ an escape (it contains no
	// exactly-double pair) and leaves MUSHcode brace groups like {{a},{b}}
	// unmatched (their bodies contain braces).
	[GeneratedRegex(@"(?<!\{)\{\{(?!\{)(?<body>[^{}]*)\}\}")]
	private static partial Regex TokenRegex();

	[GeneratedRegex(
		@"^(?:\$(?<wk>[a-z_][a-z0-9_]*)|\?(?<cfg>[a-z_][a-z0-9_]*)|(?<pkg>[a-z][a-z0-9-]*)/(?<xref>[a-z_][a-z0-9_]*)|(?<name>[a-z_][a-z0-9_]*))$",
		RegexOptions.IgnoreCase)]
	private static partial Regex BodyRegex();

	/// <summary>
	/// Finds every exactly-double-braced token in <paramref name="text"/>,
	/// parsing each body into a <see cref="PackageRef"/> where valid.
	/// Escaped openings (<c>{{{{</c>) and brace-containing bodies never match.
	/// </summary>
	public static IEnumerable<PackageRefToken> Scan(string text)
	{
		foreach (Match match in TokenRegex().Matches(text))
		{
			yield return new PackageRefToken(match.Value, ParseBody(match.Groups["body"].Value));
		}
	}

	/// <summary>
	/// Parses a field that must be exactly one ref token (as used in
	/// <c>parent:</c> / <c>location:</c> / <c>destination:</c>). Returns null
	/// when the text is not a single valid <c>{{...}}</c> token.
	/// </summary>
	public static PackageRef? ParseSingle(string text)
	{
		var trimmed = text.Trim();
		if (trimmed.Length < 4 || !trimmed.StartsWith("{{") || !trimmed.EndsWith("}}"))
		{
			return null;
		}

		var body = trimmed[2..^2];
		return body.Contains('{') || body.Contains('}') ? null : ParseBody(body);
	}

	private static PackageRef? ParseBody(string body)
	{
		var match = BodyRegex().Match(body);
		if (!match.Success)
		{
			return null;
		}

		if (match.Groups["wk"].Success)
		{
			return new PackageRef(PackageRefKind.WellKnown, match.Groups["wk"].Value.ToLowerInvariant());
		}

		if (match.Groups["cfg"].Success)
		{
			return new PackageRef(PackageRefKind.Configure, match.Groups["cfg"].Value.ToLowerInvariant());
		}

		if (match.Groups["pkg"].Success)
		{
			return new PackageRef(
				PackageRefKind.Internal,
				match.Groups["xref"].Value.ToLowerInvariant(),
				match.Groups["pkg"].Value.ToLowerInvariant());
		}

		return new PackageRef(PackageRefKind.Internal, match.Groups["name"].Value.ToLowerInvariant());
	}
}
