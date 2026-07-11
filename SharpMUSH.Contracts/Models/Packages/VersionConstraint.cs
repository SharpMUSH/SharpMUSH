namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A version constraint on a package dependency, e.g. <c>&gt;=1.0</c>,
/// <c>&gt;=1.0 &lt;2.0</c>, or a bare version for an exact match.
/// Multiple space- or comma-separated clauses are conjunctive (all must hold).
/// </summary>
/// <param name="Clauses">The individual comparison clauses, all of which must be satisfied.</param>
public sealed record VersionConstraint(IReadOnlyList<VersionConstraintClause> Clauses)
{
	/// <summary>
	/// A constraint satisfied by any release version (empty clause list).
	/// Note: per the prerelease rule on <see cref="IsSatisfiedBy"/>, this never
	/// matches a prerelease — prereleases are only selected by explicit opt-in.
	/// </summary>
	public static VersionConstraint Any { get; } = new([]);

	/// <summary>
	/// Attempts to parse a constraint expression. Supported operators:
	/// <c>&gt;=</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&lt;</c>, <c>=</c>, <c>==</c>;
	/// a bare version means exact match. Returns false on any unparsable clause.
	/// </summary>
	public static bool TryParse(string? input, out VersionConstraint constraint)
	{
		constraint = Any;
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		var clauses = new List<VersionConstraintClause>();
		foreach (var token in input.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var (op, rest) = token switch
			{
				_ when token.StartsWith(">=") => (VersionComparison.GreaterOrEqual, token[2..]),
				_ when token.StartsWith("<=") => (VersionComparison.LessOrEqual, token[2..]),
				_ when token.StartsWith("==") => (VersionComparison.Exact, token[2..]),
				_ when token.StartsWith('>') => (VersionComparison.Greater, token[1..]),
				_ when token.StartsWith('<') => (VersionComparison.Less, token[1..]),
				_ when token.StartsWith('=') => (VersionComparison.Exact, token[1..]),
				_ => (VersionComparison.Exact, token)
			};

			if (!PackageVersion.TryParse(rest, out var version))
			{
				return false;
			}

			clauses.Add(new VersionConstraintClause(op, version));
		}

		if (clauses.Count == 0)
		{
			return false;
		}

		constraint = new VersionConstraint(clauses);
		return true;
	}

	/// <summary>
	/// Returns true when <paramref name="version"/> satisfies every clause.
	/// Prerelease rule (node-semver, decision 20.16): a prerelease version
	/// additionally requires some clause whose version carries a prerelease on
	/// the same [major, minor, patch] tuple — so <c>&gt;=1.0</c> never matches
	/// <c>2.0.0-beta</c>, but <c>&gt;=1.2.3-alpha</c> matches <c>1.2.3-beta</c>.
	/// </summary>
	public bool IsSatisfiedBy(PackageVersion version) => IsSatisfiedBy(version, includePrereleases: false);

	/// <summary>
	/// As <see cref="IsSatisfiedBy(PackageVersion)"/>, but
	/// <paramref name="includePrereleases"/> disables the prerelease gate —
	/// used for <c>conflicts:</c> matching, where an installed prerelease must
	/// still count as a conflict.
	/// </summary>
	public bool IsSatisfiedBy(PackageVersion version, bool includePrereleases)
	{
		var clausesHold = Clauses.All(clause => clause.Comparison switch
		{
			VersionComparison.Exact => version.CompareTo(clause.Version) == 0,
			VersionComparison.Greater => version.CompareTo(clause.Version) > 0,
			VersionComparison.GreaterOrEqual => version.CompareTo(clause.Version) >= 0,
			VersionComparison.Less => version.CompareTo(clause.Version) < 0,
			VersionComparison.LessOrEqual => version.CompareTo(clause.Version) <= 0,
			_ => false
		});

		if (!clausesHold)
		{
			return false;
		}

		if (version.Prerelease is null || includePrereleases)
		{
			return true;
		}

		return Clauses.Any(clause => clause.Version.Prerelease is not null
			&& clause.Version.Major == version.Major
			&& clause.Version.Minor == version.Minor
			&& clause.Version.Patch == version.Patch);
	}

	public override string ToString() =>
		Clauses.Count == 0
			? "*"
			: string.Join(" ", Clauses.Select(c => c.ToString()));
}

/// <summary>A single comparison clause within a <see cref="VersionConstraint"/>.</summary>
/// <param name="Comparison">The comparison operator.</param>
/// <param name="Version">The version the operator compares against.</param>
public sealed record VersionConstraintClause(VersionComparison Comparison, PackageVersion Version)
{
	public override string ToString() => Comparison switch
	{
		VersionComparison.Exact => $"={Version}",
		VersionComparison.Greater => $">{Version}",
		VersionComparison.GreaterOrEqual => $">={Version}",
		VersionComparison.Less => $"<{Version}",
		VersionComparison.LessOrEqual => $"<={Version}",
		_ => Version.ToString()
	};
}

/// <summary>Comparison operators usable in a version constraint clause.</summary>
public enum VersionComparison
{
	Exact,
	Greater,
	GreaterOrEqual,
	Less,
	LessOrEqual
}
