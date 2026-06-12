using System.Text;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// Substitutes <c>{{ref}}</c> tokens in attribute/lock values with resolved
/// values, honoring the <c>{{{{</c> escape (which emits a literal <c>{{</c>).
/// Used by the plan engine (to compare resolved-new against baselines) and by
/// the apply engine (final substitution).
/// </summary>
public static class PackageRefSubstitution
{
	/// <summary>
	/// Walks <paramref name="text"/> left to right: <c>{{{{</c> emits a
	/// literal <c>{{</c>; an exactly-double-braced valid ref token is replaced
	/// with <paramref name="resolver"/>'s value, or left in place (and
	/// reported) when the resolver returns null; everything else copies
	/// verbatim. Manifest validation guarantees malformed tokens never reach
	/// this point.
	/// </summary>
	/// <param name="text">The value to substitute into.</param>
	/// <param name="resolver">Maps a ref to its replacement, or null when not yet resolvable.</param>
	/// <param name="unresolved">Refs the resolver could not supply (token left verbatim).</param>
	/// <returns>The substituted text.</returns>
	public static string Substitute(string text, Func<PackageRef, string?> resolver, out IReadOnlyList<PackageRef> unresolved)
	{
		var pending = new List<PackageRef>();
		var result = new StringBuilder(text.Length);
		var i = 0;

		while (i < text.Length)
		{
			if (i + 3 < text.Length && text[i] == '{' && text[i + 1] == '{' && text[i + 2] == '{' && text[i + 3] == '{')
			{
				result.Append("{{");
				i += 4;
				continue;
			}

			if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{')
			{
				var close = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
				var body = close > 0 ? text[(i + 2)..close] : null;
				var reference = body is not null && !body.Contains('{') && !body.Contains('}')
					? PackageRefScanner.ParseSingle($"{{{{{body}}}}}")
					: null;

				if (reference is not null)
				{
					var resolved = resolver(reference);
					if (resolved is null)
					{
						pending.Add(reference);
						result.Append(text, i, close + 2 - i);
					}
					else
					{
						result.Append(resolved);
					}

					i = close + 2;
					continue;
				}
			}

			result.Append(text[i]);
			i++;
		}

		unresolved = pending;
		return result.ToString();
	}
}
