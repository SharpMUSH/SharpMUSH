using SharpMUSH.Configuration.Options;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Parser-layer implementation of <see cref="IPackageLifecycleRunner"/>: after a
/// successful apply, runs a package's <c>AINSTALL</c> (first install) / <c>AUPDATE</c>
/// (upgrade) attribute as a <b>command list</b> under the package object itself, via
/// the shared <see cref="StartupAttributeRunner.RunObjectAttributeObservedAsync"/> (the same
/// path <c>@STARTUP</c> uses). Command-parsing — not function evaluation — is required
/// because lifecycle scripts contain commands (<c>@function</c>, <c>@hook</c>, …).
/// Lives here (not in <c>SharpMUSH.Library</c>) because running softcode requires the parser.
/// </summary>
public class PackageLifecycleRunner(
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	ILogger<PackageLifecycleRunner> logger,
	IOptionsWrapper<SharpMUSHOptions> options) : IPackageLifecycleRunner, IPackageLifecycleObserver
{
	/// <summary>God (#1): the base parser identity and caller for lifecycle scripts.</summary>
	private static readonly DBRef God = new(1);


	/// <inheritdoc />
	public async Task RunLifecycleAsync(
		PackageChangeset changeset,
		IReadOnlyDictionary<string, string> createdObjects,
		CancellationToken cancellationToken = default)
	{
		// Install → AINSTALL (first install only); Upgrade → AUPDATE (never on first install).
		if (changeset.LifecycleHook is not { } attribute)
		{
			return;
		}

		// Every object the package created (resolved at apply time) plus its already-existing objects carried on the
		// changeset. An object it only attaches to keeps its hook to itself.
		var objids = new HashSet<string>(createdObjects.Values, StringComparer.Ordinal);
		foreach (var change in changeset.Objects.Where(changeset.RunsLifecycleOn))
		{
			var objid = change.Objid ?? createdObjects.GetValueOrDefault(change.Ref);
			if (objid is not null)
			{
				objids.Add(objid);
			}
		}

		foreach (var objid in objids)
		{
			await RunLifecycleAsync(objid, attribute, cancellationToken);
		}
	}

	/// <inheritdoc />
	public async Task RunLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default)
		=> await RunObservedLifecycleAsync(objId, attribute, cancellationToken);

	/// <inheritdoc />
	public async Task<PackageLifecycleOutcome> RunObservedLifecycleAsync(string objId, string attribute, CancellationToken cancellationToken = default)
	{
		if (PackageLifecycleDeferral.TryDefer(objId, attribute)) return PackageLifecycleOutcome.Deferred;
		var parent = ExecutionBudget.Current;
		try
		{
			// Without an ambient budget, merely linking a request would install an infinite budget
			// and suppress the parser's configured default. Own that same default explicitly here.
			using var configured = parent is null
				? ExecutionBudget.FromMilliseconds(options.CurrentValue.Limit.QueueEntryCpuTime, cancellationToken) : null;
			using var scope = configured is not null ? configured.Enter() : ExecutionBudget.EnterLinked(cancellationToken);
			return await RunGuardedAsync(objId, attribute, parent);
		}
		catch (OperationCanceledException exception) { return ReportFailure(exception, objId, attribute, CancellationOutcome(parent)); }
		catch (Exception exception)
		{
			return ReportFailure(exception, objId, attribute, PackageLifecycleOutcome.Failed);
		}
	}

	private PackageLifecycleOutcome ReportFailure(Exception exception, string objId, string attribute, PackageLifecycleOutcome outcome)
	{
		logger.LogError(exception, "Package lifecycle '{Attribute}' on {ObjId} threw; install continues.", attribute, objId);
		return outcome;
	}

	private static void CheckBudget(ExecutionBudget? parent)
	{
		parent?.ThrowIfExceeded();
		ExecutionBudget.Current?.ThrowIfExceeded();
	}

	private static PackageLifecycleOutcome CancellationOutcome(ExecutionBudget? parent)
		=> ExecutionBudget.Current?.IsExpired == true || parent?.IsExpired == true
			? PackageLifecycleOutcome.ExecutionLimit : PackageLifecycleOutcome.Cancelled;

	// Classify while the linked/owned budget is still alive, before the caller restores its parent.
	private async Task<PackageLifecycleOutcome> RunGuardedAsync(string objId, string attribute, ExecutionBudget? parent)
	{
		try
		{
			CheckBudget(parent);
			if (HelperFunctions.ParseDbRef(objId) is not DBRef dbref) return PackageLifecycleOutcome.InvalidTarget;
			var foundGod = await mediator.Send(new GetObjectNodeQuery(God), ExecutionBudget.CurrentToken);
			CheckBudget(parent);
			if (foundGod is not AnySharpObject god)
			{
				logger.LogWarning("Package lifecycle '{Attribute}' on {ObjId} skipped: God (#1) not found.", attribute, objId);
				return PackageLifecycleOutcome.MissingExecutor;
			}
			var foundTarget = await mediator.Send(new GetObjectNodeQuery(dbref), ExecutionBudget.CurrentToken);
			CheckBudget(parent);
			if (foundTarget is not AnySharpObject target)
				return PackageLifecycleOutcome.MissingTarget;
			var basedParser = parser.Push(ParserState.RootFor(god.Object().DBRef));
			var verdict = await StartupAttributeRunner.RunObjectAttributeObservedAsync(basedParser, attributeService, target, attribute, god);
			// The parser can return a deadline failure instead of throwing; cancellation wins over
			// its ordinary verdict. Check the original deadline too when a linked timer races it.
			if (verdict is ObjectAttributeRunStatus.Parsed or ObjectAttributeRunStatus.NoVerdict
				or ObjectAttributeRunStatus.ParseFailure or ObjectAttributeRunStatus.CommandFailure)
				logger.LogInformation("Ran package lifecycle '{Attribute}' on {ObjId}.", attribute, objId);
			CheckBudget(parent);
			return verdict switch
			{
				ObjectAttributeRunStatus.Absent => PackageLifecycleOutcome.Absent,
				ObjectAttributeRunStatus.Empty => PackageLifecycleOutcome.Empty,
				ObjectAttributeRunStatus.Unavailable => PackageLifecycleOutcome.AttributeUnavailable,
				ObjectAttributeRunStatus.NoVerdict => PackageLifecycleOutcome.NoVerdict,
				ObjectAttributeRunStatus.Parsed => PackageLifecycleOutcome.Parsed,
				ObjectAttributeRunStatus.ParseFailure => PackageLifecycleOutcome.ParseFailure,
				ObjectAttributeRunStatus.CommandFailure => PackageLifecycleOutcome.CommandFailure,
				_ => PackageLifecycleOutcome.Unknown
			};
		}
		catch (OperationCanceledException exception) { return ReportFailure(exception, objId, attribute, CancellationOutcome(parent)); }
		catch (Exception exception)
		{
			return ReportFailure(exception, objId, attribute, PackageLifecycleOutcome.Failed);
		}
	}
}
