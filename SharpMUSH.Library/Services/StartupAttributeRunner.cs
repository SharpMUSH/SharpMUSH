using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Shared logic for running every object's <c>@STARTUP</c> attribute as a command list. This is the
/// pass used both by <c>@restart/all</c> and by the boot-time hosted service, so global side effects
/// of <c>@STARTUP</c> (notably re-registering <c>@function</c> global user-defined functions, which
/// are commands and must therefore be command-parsed) re-establish identically whether triggered
/// manually or at server start.
/// </summary>
public static class StartupAttributeRunner
{
	/// <summary>
	/// Streams all typed objects and runs each one's non-inherited <c>STARTUP</c> attribute as a
	/// command list under the given <paramref name="executor"/>. Errors from individual objects are
	/// swallowed (an object's <c>@STARTUP</c> failing must never abort the pass for the rest).
	/// </summary>
	/// <param name="parser">A live parser to evaluate softcode with.</param>
	/// <param name="mediator">Mediator used to stream the object set.</param>
	/// <param name="attributeService">Attribute service used to read the STARTUP attribute.</param>
	/// <param name="executor">The executor under whose authority STARTUP runs (typically God, #1).</param>
	public static async ValueTask RunAllAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IAttributeService attributeService,
		AnySharpObject executor)
	{
		await foreach (var obj in mediator.CreateStream(new GetAllTypedObjectsQuery()))
		{
			try
			{
				await RunObjectAttributeAsync(parser, attributeService, obj, "STARTUP", executor);
			}
			catch
			{
				// Ignore errors from @STARTUP — they're non-fatal and must not stop the pass.
			}
		}
	}

	/// <summary>
	/// Runs the attribute and reports its local read/parse verdict. Exceptions retain their original
	/// meaning. No raw attribute, parser state or error text is retained in the returned metadata.
	/// </summary>
	public static async ValueTask<ObjectAttributeRunStatus> RunObjectAttributeObservedAsync(
		IMUSHCodeParser parser, IAttributeService attributeService, AnySharpObject obj, string attribute, AnySharpObject caller)
	{
		var found = await attributeService.GetAttributeAsync(caller, obj, attribute, IAttributeService.AttributeMode.Read, parent: false);
		if (found is not SharpAttribute[] chain)
			return found is None ? ObjectAttributeRunStatus.Absent : ObjectAttributeRunStatus.Unavailable;
		if (chain.Length == 0 || chain.Any(row => row.IsUnreadable || row.IsMetadataUnreadable))
			return ObjectAttributeRunStatus.Unavailable;
		var value = chain[^1].Value;
		if (value.Length == 0) return ObjectAttributeRunStatus.Empty;
		var runParser = parser.Push(parser.CurrentState with
		{
			Executor = obj.Object().DBRef,
			Enactor = obj.Object().DBRef,
			Caller = caller.Object().DBRef
		});
		return await runParser.CommandListParse(value) switch
		{
			null => ObjectAttributeRunStatus.NoVerdict,
			{ IsParseFailure: true } => ObjectAttributeRunStatus.ParseFailure,
			{ HadErrors: true } => ObjectAttributeRunStatus.CommandFailure,
			_ => ObjectAttributeRunStatus.Parsed
		};
	}

	/// <summary>
	/// Legacy projection: true means parsing was attempted, including an error or absent parser
	/// verdict. Absent, empty and unavailable attributes are false; execution exceptions propagate.
	/// </summary>
	public static async ValueTask<bool> RunObjectAttributeAsync(IMUSHCodeParser parser,
		IAttributeService attributeService, AnySharpObject obj, string attribute, AnySharpObject caller)
		=> await RunObjectAttributeObservedAsync(parser, attributeService, obj, attribute, caller) is
			ObjectAttributeRunStatus.Parsed or ObjectAttributeRunStatus.NoVerdict
			or ObjectAttributeRunStatus.ParseFailure or ObjectAttributeRunStatus.CommandFailure;
}
