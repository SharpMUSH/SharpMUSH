namespace SharpMUSH.Server.Services;

/// <summary>Deterministic dependency ordering shared by bundled selection and reviewed batches.</summary>
public sealed record PackageDependencyOrder(IReadOnlyList<string> Ordered, IReadOnlyList<IReadOnlyList<string>> Cycles)
{
	public static PackageDependencyOrder For(IEnumerable<string> nodes, Func<string, IEnumerable<string>> dependencies)
	{
		var all = nodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		var allowed = all.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var path = new List<string>();
		var ordered = new List<string>();
		var cycles = new List<IReadOnlyList<string>>();
		foreach (var node in all) Visit(node);
		return new(ordered, cycles);

		void Visit(string node)
		{
			if (done.Contains(node)) return;
			var at = path.FindIndex(p => string.Equals(p, node, StringComparison.OrdinalIgnoreCase));
			if (at >= 0)
			{
				cycles.Add(path.Skip(at).Append(node).ToList());
				return;
			}
			path.Add(node);
			foreach (var dependency in dependencies(node).Where(allowed.Contains)) Visit(dependency);
			path.RemoveAt(path.Count - 1);
			if (done.Add(node)) ordered.Add(node);
		}
	}
}
