using System.Text;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// The PCRE spellings PennMUSH's regular expressions accept that .NET's engine reads differently or
/// refuses: POSIX classes inside a bracket expression (<c>[[:alnum:]]</c>), and Python-style named
/// groups (<c>(?P&lt;name&gt;...)</c>, <c>(?P=name)</c>).
/// </summary>
public static class PcreSyntax
{
	private static readonly Dictionary<string, string> PosixClasses = new(StringComparer.Ordinal)
	{
		["alnum"] = "a-zA-Z0-9",
		["alpha"] = "a-zA-Z",
		["ascii"] = @"\x00-\x7F",
		["blank"] = @" \t",
		["cntrl"] = @"\x00-\x1F\x7F",
		["digit"] = "0-9",
		["graph"] = @"\x21-\x7E",
		["lower"] = "a-z",
		["print"] = @"\x20-\x7E",
		["punct"] = @"!-/:-@\[-`{-~",
		["space"] = @"\t\n\v\f\r ",
		["upper"] = "A-Z",
		["word"] = @"\w",
		["xdigit"] = "0-9A-Fa-f",
	};

	/// <summary><paramref name="pattern"/> with its PCRE-only spellings rewritten for .NET.</summary>
	public static string ToDotNet(string pattern)
	{
		if (!pattern.Contains("[:", StringComparison.Ordinal) && !pattern.Contains("(?P", StringComparison.Ordinal))
		{
			return pattern;
		}

		var result = new StringBuilder(pattern.Length);
		var inClass = false;
		for (var i = 0; i < pattern.Length; i++)
		{
			var c = pattern[i];
			if (c == '\\' && i + 1 < pattern.Length)
			{
				result.Append(c).Append(pattern[++i]);
				continue;
			}

			if (inClass)
			{
				if (c == '[' && TryPosixClass(pattern, i, out var members, out var end))
				{
					result.Append(members);
					i = end;
					continue;
				}

				if (c == ']') inClass = false;
				result.Append(c);
				continue;
			}

			if (c == '[')
			{
				inClass = true;
				result.Append(c);
				// A ']' first in the class, after an optional '^', is a member, not the end.
				if (i + 1 < pattern.Length && pattern[i + 1] == '^') result.Append(pattern[++i]);
				if (i + 1 < pattern.Length && pattern[i + 1] == ']') result.Append(pattern[++i]);
				continue;
			}

			if (string.CompareOrdinal(pattern, i, "(?P<", 0, 4) == 0)
			{
				result.Append("(?<");
				i += 3;
				continue;
			}

			if (string.CompareOrdinal(pattern, i, "(?P=", 0, 4) == 0 && pattern.IndexOf(')', i) is var close and > 0)
			{
				result.Append(@"\k<").Append(pattern, i + 4, close - i - 4).Append('>');
				i = close;
				continue;
			}

			result.Append(c);
		}

		return result.ToString();
	}

	/// <summary>A <c>[:name:]</c> at <paramref name="start"/>, as the members it stands for.</summary>
	private static bool TryPosixClass(string pattern, int start, out string members, out int end)
	{
		members = string.Empty;
		end = pattern.IndexOf(":]", start + 2, StringComparison.Ordinal);
		if (start + 1 >= pattern.Length || pattern[start + 1] != ':' || end < 0) return false;

		var found = PosixClasses.TryGetValue(pattern[(start + 2)..end], out var value);
		members = value ?? string.Empty;
		end += 1;
		return found;
	}
}
