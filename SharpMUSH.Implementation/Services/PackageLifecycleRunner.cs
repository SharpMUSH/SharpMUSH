using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
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
/// the shared <see cref="StartupAttributeRunner.RunObjectAttributeAsync"/> (the same
/// path <c>@STARTUP</c> uses). Command-parsing — not function evaluation — is required
/// because lifecycle scripts contain commands (<c>@function</c>, <c>@hook</c>, …).
/// Lives here (not in <c>SharpMUSH.Library</c>) because running softcode requires the parser.
/// </summary>
public class PackageLifecycleRunner(
	IMediator mediator,
	IAttributeService attributeService,
	IMUSHCodeParser parser,
	ILogger<PackageLifecycleRunner> logger) : IPackageLifecycleRunner
{
	/// <summary>God (#1): the identity every lifecycle script runs as.</summary>
	private static readonly DBRef God = new(1);

	/// <inheritdoc />
	public async Task RunLifecycleAsync(
		PackageChangeset changeset,
		IReadOnlyDictionary<string, string> createdObjects,
		CancellationToken cancellationToken = default)
	{
		// Install → AINSTALL (first install only); Upgrade → AUPDATE (never on first install).
		var attribute = changeset.Kind switch
		{
			PackageRevisionKind.Install => "AINSTALL",
			PackageRevisionKind.Upgrade => "AUPDATE",
			_ => null
		};
		if (attribute is null)
		{
			return;
		}

		// Every object the package created or attaches to: created objids (resolved
		// at apply time) plus already-existing/attach targets carried on the changeset.
		var objids = new HashSet<string>(createdObjects.Values, StringComparer.Ordinal);
		foreach (var change in changeset.Objects)
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
	{
		try
		{
			var dbref = SharpMUSH.Library.Services.PackageInstallService.ParseObjid(objId);
			if (dbref is null)
			{
				return;
			}

			var godNode = await mediator.Send(new GetObjectNodeQuery(God), cancellationToken);
			if (godNode.IsNone)
			{
				logger.LogWarning("Package lifecycle '{Attribute}' on {ObjId} skipped: God (#1) not found.", attribute, objId);
				return;
			}

			var targetNode = await mediator.Send(new GetObjectNodeQuery(dbref.Value), cancellationToken);
			if (targetNode.IsNone)
			{
				return;
			}

			var god = godNode.Known;
			var target = targetNode.Known;

			// Establish a God base parser state (there is no ambient parser on the install path),
			// then run the lifecycle attribute as a COMMAND LIST under the package object itself.
			// AINSTALL/AUPDATE contain commands (@function/@hook/@set/&attr) — they must be
			// command-parsed, not function-evaluated — and run as the object so `%!`/`me` resolve
			// to it (e.g. `@function header=%!,HEADER` targets the package object's own attribute).
			var basedParser = parser.Push(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				SwitchStack: [],
				ExecutionStack: [],
				EnvironmentRegisters: new Dictionary<string, CallState>(),
				CurrentEvaluation: null,
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: god.Object().DBRef,
				Enactor: god.Object().DBRef,
				Caller: god.Object().DBRef,
				Handle: null,
				ParseMode: ParseMode.Default,
				HttpResponse: null,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()));

			var ran = await StartupAttributeRunner.RunObjectAttributeAsync(
				basedParser, attributeService, target, attribute, god);

			if (ran)
			{
				logger.LogInformation("Ran package lifecycle '{Attribute}' on {ObjId}.", attribute, objId);
			}
		}
		catch (Exception ex)
		{
			// A bad lifecycle script must never fail the install — log and move on.
			logger.LogError(ex, "Package lifecycle '{Attribute}' on {ObjId} threw; install continues.", attribute, objId);
		}
	}
}
