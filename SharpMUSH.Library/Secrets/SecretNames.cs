using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Secrets;

/// <summary>
/// How a secret is named, and where an operator puts a value that overrides the stored one.
/// </summary>
public static partial class SecretNames
{
	/// <summary>The longest name allowed.</summary>
	public const int MaxLength = 64;

	/// <summary>The prefix of the environment variable that overrides a secret.</summary>
	public const string EnvironmentPrefix = "SHARPMUSH_SECRET_";

	/// <summary>
	/// Lower-case letters and digits, in parts joined by <c>.</c>: <c>sql.password</c>, <c>discord.token</c>,
	/// <c>webhook.mailrelay.signing</c>. A name is also a file name in the secrets directory, so it never holds a
	/// path separator or starts with a dot. <c>.</c> is the only separator so that no two names share an
	/// environment variable.
	/// </summary>
	public static bool IsValid(string name) => name.Length <= MaxLength && NamePattern().IsMatch(name);

	/// <summary>
	/// The environment variable that overrides <paramref name="name"/>: upper case, with <c>.</c> written as
	/// <c>_</c>. <c>sql.password</c> is <c>SHARPMUSH_SECRET_SQL_PASSWORD</c>.
	/// </summary>
	public static string EnvironmentVariable(string name)
		=> EnvironmentPrefix + name.ToUpperInvariant().Replace('.', '_');

	[GeneratedRegex(@"^[a-z0-9]+(?:\.[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
