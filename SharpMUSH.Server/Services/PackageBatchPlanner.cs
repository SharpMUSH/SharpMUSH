using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>A resolved candidate from the existing package sources, never a second dependency registry.</summary>
public sealed record PackageBatchCandidate(PackageBatchSource Source, PackageManifest Manifest, string Commit);

/// <summary>One read-only projection of authoritative package and discovered plugin metadata.</summary>
public sealed record PackageBatchProjection(IReadOnlyList<InstalledPackageRecord> Installed,
	IReadOnlyList<PackageDependencyRecord> Dependencies, IReadOnlyList<PackageBatchCandidate> Candidates,
	IReadOnlyList<PluginStatusDto> Plugins, IReadOnlyDictionary<string, IReadOnlyList<string>> PluginDependencies);

/// <summary>Pure planning: only explicit intents become steps; dependency closure is offered separately.</summary>
public static class PackageBatchPlanner
{
	public static PackageBatchPlanResponse Plan(PackageBatchPlanRequest request, PackageBatchProjection projection)
	{
		var blockers = new List<PackageBatchBlocker>();
		var proposals = new Dictionary<string, PackageBatchProposal>(StringComparer.OrdinalIgnoreCase);
		var intents = new Dictionary<string, PackageBatchIntent>(StringComparer.OrdinalIgnoreCase);
		var installed = projection.Installed.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
		var candidates = projection.Candidates.GroupBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
		var plugins = projection.Plugins.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
		var prerequisites = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		var selected = new Dictionary<string, PackageBatchCandidate>(StringComparer.OrdinalIgnoreCase);
		var visiting = new List<string>();
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var intent in request.Intents)
		{
			if (string.IsNullOrWhiteSpace(intent.TargetId) || !Enum.IsDefined(intent.Action))
			{
				Block(intent.TargetId, "invalid-intent", "A target and supported action are required.");
				continue;
			}

			var key = Key(intent);
			if (!intents.TryAdd(key, intent))
				Block(intent.TargetId, "contradictory-intents", $"Submit only one requested action for {intent.TargetId}.");
			prerequisites.TryAdd(key, new(StringComparer.OrdinalIgnoreCase));
		}

		foreach (var intent in intents.Values.OrderBy(i => Key(i), StringComparer.OrdinalIgnoreCase).ToList())
		{
			switch (intent.Action)
			{
				case PackageBatchAction.Install:
					VisitInstall(intent);
					break;
				case PackageBatchAction.Uninstall:
					VisitRemoval(intent);
					break;
				case PackageBatchAction.PluginOn:
				case PackageBatchAction.PluginOff:
					VisitPlugin(intent);
					break;
			}
		}

		// Reuse the install engine's exact version/conflict semantics against desired registry state.
		var projected = installed.Values.Where(p => !Requested(p.Id, PackageBatchAction.Uninstall)).ToList();
		foreach (var pair in selected.Where(p => Requested(p.Key, PackageBatchAction.Install)))
		{
			projected.RemoveAll(p => Same(p.Id, pair.Key));
			projected.Add(new(pair.Key, pair.Value.Manifest.Version.ToString(), "projection", null,
				pair.Value.Commit, null, DateTimeOffset.UnixEpoch, 0));
		}

		var running = projection.Plugins.Where(IsRunning).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		// The graph permits a reviewed plugin-on; execution still checks actual running state and pauses.
		foreach (var plugin in intents.Values.Where(i => i.Action == PackageBatchAction.PluginOn)) running.Add(plugin.TargetId);
		foreach (var plugin in intents.Values.Where(i => i.Action == PackageBatchAction.PluginOff)) running.Remove(plugin.TargetId);
		foreach (var candidate in selected.Values.Where(c => Requested(c.Manifest.Name, PackageBatchAction.Install)))
		{
			foreach (var issue in PackagePlanService.CheckDependenciesAndConflicts(candidate.Manifest, projected, running))
			{
				if (issue.IsConflict)
					Block(candidate.Manifest.Name, "package-conflict", $"{candidate.Manifest.Name} conflicts with {issue.PackageId} {issue.InstalledVersion}.");
			}
		}

		var sorted = PackageDependencyOrder.For(intents.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
			key => prerequisites[key].OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
		if (!blockers.Any(b => b.Code == "dependency-cycle"))
			foreach (var cycle in sorted.Cycles)
				Block(intents[cycle[0]].TargetId, "dependency-cycle", $"Dependency cycle: {string.Join(" -> ", cycle.Select(k => intents[k].TargetId))}");
		var ordered = sorted.Ordered;
		var state = JsonSerializer.Serialize(new
		{
			Installed = projection.Installed.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
			Dependencies = projection.Dependencies.OrderBy(p => p.PackageId).ThenBy(p => p.DependsOnId),
			Plugins = projection.Plugins.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
			PluginDependencies = projection.PluginDependencies.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
		});
		var steps = ordered.Select(key =>
		{
			var intent = intents[key];
			var current = IsPackage(intent) ? installed.GetValueOrDefault(intent.TargetId) : null;
			var candidate = intent.Action == PackageBatchAction.Install ? selected.GetValueOrDefault(intent.TargetId) : null;
			if (candidate is not null) intent = intent with { Source = candidate.Source };
			var plugin = !IsPackage(intent) ? plugins.GetValueOrDefault(intent.TargetId) : null;
			var identity = JsonSerializer.Serialize(new { intent.Action, intent.TargetId, candidate, State = state });
			return new PackageBatchStep(Hash(identity), intent, "Explicitly requested", current?.Version, current?.CurrentRevision,
				candidate?.Manifest.Version.ToString(), candidate?.Commit, plugin,
				RequiresRestart: intent.Action == PackageBatchAction.PluginOn && plugin is not null && !IsRunning(plugin)
					|| intent.Action == PackageBatchAction.PluginOff && plugin is not null && IsRunning(plugin) && !plugin.Unloadable
					|| candidate?.Manifest.Kind == PackageKind.Plugin);
		}).ToList();
		return new(steps, proposals.Values.ToList(), blockers.Distinct().ToList(), null, null);

		void Block(string target, string code, string message) => blockers.Add(new(target, code, message));
		bool Requested(string id, PackageBatchAction action) => intents.TryGetValue(Key(id, action), out var found) && found.Action == action;
		void Before(PackageBatchIntent first, PackageBatchIntent second)
		{
			if (intents.ContainsKey(Key(first)) && intents.ContainsKey(Key(second))) prerequisites[Key(second)].Add(Key(first));
		}
		void Propose(PackageBatchIntent intent, string reason)
		{
			proposals.TryAdd(Key(intent), new(intent, reason));
			Block(intent.TargetId, "prerequisite-not-accepted", reason);
		}
		PackageBatchCandidate? Candidate(PackageBatchIntent intent)
		{
			var matches = candidates.GetValueOrDefault(intent.TargetId)?.Where(c => intent.Source is null || c.Source == intent.Source).ToList() ?? [];
			if (matches.Count == 1) return matches[0];
			Block(intent.TargetId, "missing-source", matches.Count > 1
				? $"Choose a source for {intent.TargetId}." : $"No available source for {intent.TargetId}.");
			return null;
		}
		void VisitInstall(PackageBatchIntent intent)
		{
			var id = intent.TargetId;
			var cycleAt = visiting.FindIndex(p => Same(p, id));
			if (cycleAt >= 0)
			{
				Block(id, "dependency-cycle", $"Dependency cycle: {string.Join(" -> ", visiting.Skip(cycleAt).Append(id))}");
				return;
			}
			if (!visited.Add("install:" + id)) return;
			var candidate = Candidate(intent);
			if (candidate is null) return;
			selected[id] = candidate;
			visiting.Add(id);
			foreach (var dependency in candidate.Manifest.Dependencies.OrderBy(d => d.PackageId, StringComparer.OrdinalIgnoreCase))
			{
				if (Requested(dependency.PackageId, PackageBatchAction.Uninstall))
				{
					Block(id, "contradictory-dependency", $"{id} requires {dependency.PackageId}, which is requested for uninstall.");
					continue;
				}
				if (!Requested(dependency.PackageId, PackageBatchAction.Install)
					&& installed.TryGetValue(dependency.PackageId, out var current)
					&& PackageVersion.TryParse(current.Version, out var version)
					&& dependency.Constraint.IsSatisfiedBy(version, includePrereleases: true)) continue;
				var next = intents.GetValueOrDefault(Key(dependency.PackageId, PackageBatchAction.Install))
					?? new(dependency.PackageId, PackageBatchAction.Install);
				var found = Candidate(next);
				if (found is null) continue;
				next = next with { Source = found.Source };
				if (!dependency.Constraint.IsSatisfiedBy(found.Manifest.Version, includePrereleases: true))
					Block(id, "incompatible-version", $"{id} requires {dependency.PackageId} {dependency.Constraint}; source provides {found.Manifest.Version}.");
				if (!Requested(dependency.PackageId, PackageBatchAction.Install)) Propose(next, $"{id} requires {dependency.PackageId} {dependency.Constraint}.");
				VisitInstall(next);
				Before(next, intent);
			}
			foreach (var pluginId in candidate.Manifest.RequiredPlugins)
			{
				var next = new PackageBatchIntent(pluginId, PackageBatchAction.PluginOn);
				if (Requested(pluginId, PackageBatchAction.PluginOff)) Block(id, "contradictory-dependency", $"{id} requires plugin {pluginId}, requested off.");
				else if (!plugins.TryGetValue(pluginId, out var plugin)) Block(id, "missing-plugin", $"{id} requires unavailable plugin {pluginId}.");
				else if (!IsRunning(plugin) && !Requested(pluginId, PackageBatchAction.PluginOn)) Propose(next, $"{id} requires plugin {pluginId} running.");
				Before(next, intent);
			}
			foreach (var conflict in candidate.Manifest.Conflicts.Where(d => Requested(d.PackageId, PackageBatchAction.Uninstall)))
				Before(new(conflict.PackageId, PackageBatchAction.Uninstall), intent);
			visiting.RemoveAt(visiting.Count - 1);
		}
		void VisitRemoval(PackageBatchIntent intent)
		{
			if (!visited.Add("remove:" + intent.TargetId)) return;
			if (!installed.ContainsKey(intent.TargetId)) Block(intent.TargetId, "not-installed", $"{intent.TargetId} is not installed.");
			foreach (var dependent in projection.Dependencies.Where(d => Same(d.DependsOnId, intent.TargetId) && installed.ContainsKey(d.PackageId)))
			{
				var next = new PackageBatchIntent(dependent.PackageId, PackageBatchAction.Uninstall);
				if (!Requested(dependent.PackageId, PackageBatchAction.Uninstall)) Propose(next, $"{dependent.PackageId} depends on {intent.TargetId}.");
				VisitRemoval(next);
				Before(next, intent);
			}
		}
		void VisitPlugin(PackageBatchIntent intent)
		{
			if (!visited.Add("plugin:" + intent.TargetId)) return;
			if (!plugins.TryGetValue(intent.TargetId, out var plugin))
			{
				Block(intent.TargetId, "missing-plugin", $"Plugin {intent.TargetId} is unavailable.");
				return;
			}
			if (intent.Action == PackageBatchAction.PluginOff)
			{
				foreach (var package in plugin.Dependents)
				{
					var removal = new PackageBatchIntent(package, PackageBatchAction.Uninstall);
					if (!Requested(package, PackageBatchAction.Uninstall)) Propose(removal, $"{package} requires plugin {plugin.Id}.");
					VisitRemoval(removal);
					Before(removal, intent);
				}
				foreach (var dependent in projection.PluginDependencies.Where(p => p.Value.Contains(plugin.Id, StringComparer.OrdinalIgnoreCase)))
				{
					if (!plugins.TryGetValue(dependent.Key, out var current) || !current.Enabled) continue;
					var next = new PackageBatchIntent(dependent.Key, PackageBatchAction.PluginOff);
					if (!Requested(next.TargetId, next.Action)) Propose(next, $"Plugin {dependent.Key} requires {plugin.Id}.");
					VisitPlugin(next);
					Before(next, intent);
				}
			}
			else
			{
				foreach (var required in projection.PluginDependencies.GetValueOrDefault(plugin.Id) ?? [])
				{
					var next = new PackageBatchIntent(required, PackageBatchAction.PluginOn);
					if (Requested(required, PackageBatchAction.PluginOff)) Block(plugin.Id, "contradictory-dependency", $"Plugin {plugin.Id} requires {required}, requested off.");
					else if (!plugins.TryGetValue(required, out var current)) Block(plugin.Id, "missing-plugin", $"Plugin {required} is unavailable.");
					else if (!current.Enabled && !Requested(required, next.Action)) Propose(next, $"Plugin {plugin.Id} requires {required}.");
					VisitPlugin(next);
					Before(next, intent);
				}
			}
		}
	}

	public static bool IsRunning(PluginStatusDto plugin) => plugin.Pending == PluginPendingChange.Stops
		|| plugin.Status == PluginBootStatus.Loaded && plugin.Pending != PluginPendingChange.Starts;
	private static bool IsPackage(PackageBatchIntent intent) => intent.Action is PackageBatchAction.Install or PackageBatchAction.Uninstall;
	private static string Key(PackageBatchIntent intent) => Key(intent.TargetId, intent.Action);
	private static string Key(string target, PackageBatchAction action) => (action is PackageBatchAction.Install or PackageBatchAction.Uninstall ? "package:" : "plugin:") + target;
	private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
	private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
