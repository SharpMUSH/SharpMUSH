namespace SharpMUSH.Server.Services;

/// <summary>
/// What turning a game's bundled packages into the wanted set takes: the packages to install, in dependency
/// order, and the ones to remove, dependents first.
/// </summary>
/// <param name="Install">Packages to install, each after the ones it depends on.</param>
/// <param name="Remove">Packages to uninstall, each before the ones it depends on.</param>
/// <param name="Kept">Packages kept although not asked for, because something wanted depends on them.</param>
public sealed record BundledPackagePlan(IReadOnlyList<string> Install, IReadOnlyList<string> Remove, IReadOnlyList<string> Kept)
{
	/// <param name="wanted">The packages the administrator asked for.</param>
	/// <param name="installed">The bundled packages the game has now.</param>
	/// <param name="dependencies">Each bundled package's bundled dependencies.</param>
	/// <param name="order">Every bundled package, a dependency before its dependents.</param>
	public static BundledPackagePlan For(IEnumerable<string> wanted, IReadOnlySet<string> installed,
		IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies, IReadOnlyList<string> order)
	{
		var asked = wanted.Where(order.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var needed = new HashSet<string>(asked, StringComparer.OrdinalIgnoreCase);
		var pending = new Stack<string>(asked);
		while (pending.TryPop(out var id))
		{
			foreach (var dependency in (dependencies.GetValueOrDefault(id) ?? [])
				.Where(d => order.Contains(d) && !needed.Contains(d)))
			{
				needed.Add(dependency);
				pending.Push(dependency);
			}
		}

		return new BundledPackagePlan(
			order.Where(id => needed.Contains(id) && !installed.Contains(id)).ToList(),
			order.Reverse().Where(id => installed.Contains(id) && !needed.Contains(id)).ToList(),
			order.Where(id => needed.Contains(id) && !asked.Contains(id)).ToList());
	}
}
