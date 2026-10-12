using System.Security.Cryptography;
using System.Text.Json;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.CodeAnalysis.Catalog;

/// <summary>The executor is resolved by the host, never accepted as a transport argument.</summary>
public interface IAnalysisCatalog
{
	ValueTask<AnalysisCatalogSnapshot> GetSnapshotAsync(AnySharpObject executor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Captures live metadata without delegates or attribute values, then applies the runtime permission gates.
/// No metadata cache is shared between principals. Permission reads happen after releasing the mutation gate.
/// </summary>
public sealed class LiveAnalysisCatalog(
	LibraryService<string, FunctionDefinition> functions,
	LibraryService<string, CommandDefinition> commands,
	IUserDefinedFunctionService users, ILockService locks) : IAnalysisCatalog
{
	private readonly string _epoch = Guid.NewGuid().ToString("N");
	private sealed record FunctionCandidate(string Name, string[] Aliases, SharpFunctionAttribute Attribute, bool System);
	private sealed record CommandCandidate(string Name, string[] Aliases, SharpCommandAttribute Attribute, bool System);

	public async ValueTask<AnalysisCatalogSnapshot> GetSnapshotAsync(AnySharpObject executor, CancellationToken cancellationToken = default)
	{
		using var budget = ExecutionBudget.EnterLinked(cancellationToken);
		var identity = executor.Object().DBRef;
		if (!identity.IsObjid) throw new ArgumentException("A host-resolved full character identity is required.", nameof(executor));
		// localfun resolves through the caller's owner, rather than the caller object's key.
		var owner = (await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		if (!owner.IsObjid) throw new ArgumentException("A full owner identity is required.", nameof(executor));
		var captured = functions.Capture(functionEntries => commands.Capture(commandEntries =>
		{
			var functionMetadata = functionEntries.GroupBy(entry => entry.Value.LibraryInformation.Attribute).Select(group =>
			{
				var attribute = group.Key;
				var names = group.Select(entry => entry.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
				var name = names.FirstOrDefault(name => name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase)) ?? names[0];
				return new FunctionCandidate(name, names.Where(alias => !alias.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray(),
					new SharpFunctionAttribute
					{
						Name = name, Flags = attribute.Flags, MinArgs = attribute.MinArgs, MaxArgs = attribute.MaxArgs,
						SideEffectMinArgs = attribute.SideEffectMinArgs, ParameterNames = attribute.ParameterNames.ToArray(), Restrict = attribute.Restrict.ToArray()
					},
					group.Any(entry => entry.Value.IsSystem));
			}).ToArray();
			var commandMetadata = commandEntries.GroupBy(entry => entry.Value.LibraryInformation.Attribute).Select(group =>
			{
				var attribute = group.Key;
				var names = group.Select(entry => entry.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
				var name = names.FirstOrDefault(name => name.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase)) ?? names[0];
				return new CommandCandidate(name, names.Where(alias => !alias.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray(),
					new SharpCommandAttribute
					{
						Name = name, MinArgs = attribute.MinArgs, MaxArgs = attribute.MaxArgs,
						Behavior = attribute.Behavior, CommandLock = attribute.CommandLock, ParameterNames = attribute.ParameterNames.ToArray(),
						ArgumentNames = attribute.ArgumentNames.ToArray(), Switches = attribute.Switches?.ToArray()
					}, group.Any(entry => entry.Value.IsSystem));
			}).ToArray();
			return (Functions: functionMetadata, Commands: commandMetadata, Users: users.CaptureCatalog(owner));
		}));
		var visible = new List<AnalysisCatalogEntry>();
		var compiledNames = new HashSet<string>(captured.Functions.SelectMany(entry => entry.Aliases.Prepend(entry.Name)), StringComparer.OrdinalIgnoreCase);
		foreach (var candidate in captured.Functions)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await FunctionDispatcher.CheckPermissionAsync(candidate.Attribute, executor) is not null) continue;
			var permittedNames = new List<string>();
			foreach (var name in candidate.Aliases.Prepend(candidate.Name))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await executor.SatisfiesFunctionRestriction(captured.Users.BuiltinRestrictions.GetValueOrDefault(name)))
					permittedNames.Add(name);
			}
			if (permittedNames.Count == 0) continue;
			var canonical = permittedNames[0];
			visible.Add(FunctionEntry(canonical, candidate.System ? "compiled" : "runtime", candidate.Attribute,
				permittedNames.Skip(1).ToArray(), captured.Users.BuiltinRestrictions.GetValueOrDefault(canonical)));
		}
		foreach (var candidate in captured.Commands)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!await CommandRestrictions.PermitsAsync(candidate.Attribute, executor) ||
				(!string.IsNullOrEmpty(candidate.Attribute.CommandLock) &&
				 !await locks.Evaluate(candidate.Attribute.CommandLock, executor, executor))) continue;
			visible.Add(new AnalysisCatalogEntry(candidate.Name, "command", candidate.System ? "compiled" : "runtime",
				candidate.Attribute.MinArgs, Maximum(candidate.Attribute.MaxArgs), candidate.Aliases,
				candidate.Attribute.ArgumentNames.Length > 0 ? candidate.Attribute.ArgumentNames : candidate.Attribute.ParameterNames,
				candidate.Attribute.Switches ?? [], candidate.Attribute.Behavior.ToString(),
				string.IsNullOrEmpty(candidate.Attribute.CommandLock) ? [] : [candidate.Attribute.CommandLock]));
		}
		foreach (var definition in captured.Users.Global.Concat(captured.Users.Local))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (compiledNames.Contains(definition.Name)) continue;
			var attribute = new SharpFunctionAttribute
			{
				Name = definition.Name, Flags = FunctionFlags.Regular,
				MinArgs = definition.MinArgs, MaxArgs = definition.MaxArgs, ParameterNames = definition.ArgumentNames,
				Restrict = string.IsNullOrWhiteSpace(definition.Restriction) ? [] : [definition.Restriction]
			};
			if (await FunctionDispatcher.CheckPermissionAsync(attribute, executor) is not null) continue;
			visible.Add(FunctionEntry(definition.Name, definition.Owner is null ? "user-defined" : "owner-local", attribute, [], null));
		}
		cancellationToken.ThrowIfCancellationRequested();
		var ordered = visible.OrderBy(entry => entry.Kind, StringComparer.Ordinal).ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
		var visibleNames = new HashSet<string>(ordered.Where(entry => entry.Kind == "function")
			.SelectMany(entry => entry.Aliases.Prepend(entry.Name)), StringComparer.OrdinalIgnoreCase);
		var overlays = captured.Users.BuiltinRestrictions.Where(entry => visibleNames.Contains(entry.Key))
			.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToArray();
		var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Executor = identity.ToString(), Owner = owner.ToString(), Entries = ordered, Overlays = overlays })));
		return new AnalysisCatalogSnapshot(_epoch, revision, "live", ordered);
	}

	private static int? Maximum(int max) => max == int.MaxValue ? null : max;
	private static AnalysisCatalogEntry FunctionEntry(string name, string origin, SharpFunctionAttribute attribute, string[] aliases, string? overlay)
		=> new(name, "function", origin, attribute.MinArgs, Maximum(attribute.MaxArgs), aliases, attribute.ParameterNames, [],
			attribute.Flags.ToString(), overlay is null ? attribute.Restrict : [.. attribute.Restrict, overlay]);
}
