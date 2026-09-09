namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A semantic version for softcode packages. Supports MAJOR, MAJOR.MINOR,
/// and MAJOR.MINOR.PATCH forms with an optional prerelease suffix
/// (e.g. <c>2.4.1</c>, <c>1.0</c>, <c>3.0.0-beta.1</c>).
/// Missing components default to zero.
/// </summary>
/// <param name="Major">Major version component.</param>
/// <param name="Minor">Minor version component (0 when omitted).</param>
/// <param name="Patch">Patch version component (0 when omitted).</param>
/// <param name="Prerelease">Prerelease suffix without the leading dash, or null for a release version.</param>
public sealed record PackageVersion(int Major, int Minor, int Patch, string? Prerelease = null)
	: IComparable<PackageVersion>
{
	/// <summary>
	/// Attempts to parse a version string. Accepts 1–3 dot-separated numeric
	/// components and an optional <c>-prerelease</c> suffix.
	/// </summary>
	public static bool TryParse(string? input, out PackageVersion version)
	{
		version = new PackageVersion(0, 0, 0);
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		var text = input.AsSpan().Trim();
		string? prerelease = null;
		var dash = text.IndexOf('-');
		if (dash >= 0)
		{
			if (dash == text.Length - 1)
			{
				return false;
			}

			prerelease = text[(dash + 1)..].ToString();
			text = text[..dash];
		}

		Span<int> numbers = [0, 0, 0];
		var filled = 0;
		foreach (var part in text.Split('.'))
		{
			if (filled == numbers.Length || !int.TryParse(text[part], out numbers[filled]) || numbers[filled] < 0)
			{
				return false;
			}

			filled++;
		}

		version = new PackageVersion(numbers[0], numbers[1], numbers[2], prerelease);
		return true;
	}

	/// <summary>
	/// Compares by numeric components; a prerelease version sorts before the
	/// corresponding release. Prerelease identifiers follow SemVer 2.0.0
	/// item 11 (decision 20.16): dot-separated, numeric identifiers compared
	/// numerically and always lower than alphanumeric ones, and on an equal
	/// prefix the version with fewer identifiers is lower —
	/// alpha &lt; alpha.1 &lt; alpha.beta &lt; beta &lt; beta.2 &lt; beta.11 &lt; rc.1.
	/// </summary>
	public int CompareTo(PackageVersion? other)
	{
		if (other is null) return 1;

		var cmp = Major.CompareTo(other.Major);
		if (cmp != 0) return cmp;
		cmp = Minor.CompareTo(other.Minor);
		if (cmp != 0) return cmp;
		cmp = Patch.CompareTo(other.Patch);
		if (cmp != 0) return cmp;

		return (Prerelease, other.Prerelease) switch
		{
			(null, null) => 0,
			(null, _) => 1,
			(_, null) => -1,
			var (mine, theirs) => ComparePrerelease(mine, theirs)
		};
	}

	private static int ComparePrerelease(string mine, string theirs)
	{
		var a = mine.Split('.');
		var b = theirs.Split('.');
		return a.Zip(b, CompareIdentifier).FirstOrDefault(cmp => cmp != 0, a.Length.CompareTo(b.Length));
	}

	private static int CompareIdentifier(string mine, string theirs)
	{
		var mineNumeric = long.TryParse(mine, out var mineNumber);
		var theirsNumeric = long.TryParse(theirs, out var theirsNumber);
		return (mineNumeric, theirsNumeric) switch
		{
			(true, true) => mineNumber.CompareTo(theirsNumber),
			(true, false) => -1,
			(false, true) => 1,
			_ => string.CompareOrdinal(mine, theirs)
		};
	}

	public override string ToString() =>
		Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
