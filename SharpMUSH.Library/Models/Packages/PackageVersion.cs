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

		var text = input.Trim();
		string? prerelease = null;
		var dash = text.IndexOf('-');
		if (dash >= 0)
		{
			prerelease = text[(dash + 1)..];
			text = text[..dash];
			if (prerelease.Length == 0)
			{
				return false;
			}
		}

		var parts = text.Split('.');
		if (parts.Length is < 1 or > 3)
		{
			return false;
		}

		var numbers = new int[3];
		for (var i = 0; i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
			{
				return false;
			}
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
		for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
		{
			var aNumeric = long.TryParse(a[i], out var aNumber);
			var bNumeric = long.TryParse(b[i], out var bNumber);
			var cmp = (aNumeric, bNumeric) switch
			{
				(true, true) => aNumber.CompareTo(bNumber),
				(true, false) => -1,
				(false, true) => 1,
				_ => string.CompareOrdinal(a[i], b[i])
			};

			if (cmp != 0) return cmp;
		}

		return a.Length.CompareTo(b.Length);
	}

	public override string ToString() =>
		Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
