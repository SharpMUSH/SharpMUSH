using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services.DatabaseConversion.Softcode;

/// <summary>
/// Renames imported attributes whose names SharpMUSH refuses, such as TinyMUX's and RhostMUSH's <c>MERIT.LANGUAGE_()</c>,
/// and rewrites the code that names them. Each character outside <see cref="HelperFunctions.AttributeNameCharacters"/>
/// becomes <c>_</c>, a backtick that would start, end or double up a tree name becomes <c>_</c> too, and a name that
/// would then clash with another gets <c>_2</c>, <c>_3</c> and so on. Code is rewritten wherever the old name stands
/// whole, in any case; a name the code builds at run time is not seen. A name that reads as a function call
/// (<c>NAME()</c>, an identifier and balanced parentheses) or has unbalanced parentheses is renamed, but code is not
/// rewritten for it: every call of that function would be taken for it.
/// </summary>
internal sealed partial class AttributeNameRewriter
{
	private readonly Dictionary<string, string> _renames;
	private readonly Regex? _references;

	private AttributeNameRewriter(Dictionary<string, string> renames)
	{
		_renames = renames;
		var followed = renames.Keys.Where(Followed).OrderByDescending(name => name.Length).Select(Regex.Escape).ToList();
		_references = followed.Count == 0
			? null
			: new Regex(
				$@"(?<![A-Za-z0-9_.`\-])(?:{string.Join("|", followed)})(?![A-Za-z0-9_.`\-])",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	/// <summary>
	/// Whether code naming <paramref name="name"/> is rewritten: not when it reads as a function call, or when its
	/// parentheses do not balance, so the pattern would match the start of calls.
	/// </summary>
	public static bool Followed(string name)
		=> !CallShaped().IsMatch(name) && name.Count(c => c == '(') == name.Count(c => c == ')');

	/// <summary>Each refused name, upper case, with the name it is imported under.</summary>
	public IReadOnlyDictionary<string, string> Renames => _renames;

	/// <summary>
	/// A rewriter for the attribute <paramref name="names"/> an import brings in, in any case; no rename takes one of
	/// them or of <paramref name="reserved"/> (the server's built-in attribute names).
	/// </summary>
	public static AttributeNameRewriter For(IEnumerable<string> names, IEnumerable<string>? reserved = null)
	{
		var all = names.Select(name => name.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
		var taken = all.Where(IsValid).Concat((reserved ?? []).Select(name => name.ToUpperInvariant())).ToHashSet(StringComparer.Ordinal);
		var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var name in all.Where(name => !IsValid(name)).Order(StringComparer.Ordinal))
		{
			var stem = Valid(name);
			var renamed = stem;
			for (var n = 2; !taken.Add(renamed); n++)
			{
				renamed = $"{stem}_{n}";
			}

			renames[name] = renamed;
		}

		return new AttributeNameRewriter(renames);
	}

	/// <summary>Whether SharpMUSH takes <paramref name="name"/> as an attribute name.</summary>
	public static bool IsValid(string name)
		=> name.Length > 0 && NameCharacters().IsMatch(name)
			&& !name.StartsWith('`') && !name.EndsWith('`') && !name.Contains("``", StringComparison.Ordinal);

	/// <summary>The import report's line for one rename.</summary>
	public static string Note(string from, string to)
		=> $"Attribute {from}: imported as {to}, as SharpMUSH takes no ( ) or other such characters in an attribute name; " +
			(Followed(from)
				? "code that names it whole is rewritten, a name built at run time is not"
				: "code naming it is not rewritten, as it reads like a function call; change it by hand");

	/// <summary>The name <paramref name="name"/> is imported under: itself, or its rename.</summary>
	public string Name(string name) => _renames.GetValueOrDefault(name, name);

	/// <summary><paramref name="code"/> with every renamed attribute it names whole written under its new name.</summary>
	public (string Text, bool Changed) Rewrite(string code)
	{
		if (_references is null)
		{
			return (code, false);
		}

		var changed = false;
		var text = _references.Replace(code, match =>
		{
			changed = true;
			return _renames[match.Value];
		});
		return (text, changed);
	}

	private static string Valid(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var c in name)
		{
			var tree = c == '`' && builder.Length > 0 && builder[^1] != '`';
			builder.Append(tree || (c != '`' && NameCharacter().IsMatch(c.ToString())) ? c : '_');
		}

		if (builder.Length > 0 && builder[^1] == '`')
		{
			builder[^1] = '_';
		}

		return builder.Length == 0 ? "_" : builder.ToString();
	}

	[GeneratedRegex("^" + HelperFunctions.AttributeNameCharacters + "+$")]
	private static partial Regex NameCharacters();

	[GeneratedRegex("^" + HelperFunctions.AttributeNameCharacters + "$")]
	private static partial Regex NameCharacter();

	[GeneratedRegex(@"^[A-Za-z0-9_]+\(.*\)$")]
	private static partial Regex CallShaped();
}
