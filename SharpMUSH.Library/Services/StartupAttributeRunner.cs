using Mediator;
using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
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
	/// Runs a single object's non-inherited attribute as a <b>command list</b> under the object's
	/// own authority (Executor = Enactor = <paramref name="obj"/>, Caller = <paramref name="caller"/>).
	/// Command-parsing (not function evaluation) is required because these attributes — <c>STARTUP</c>,
	/// package <c>AINSTALL</c>/<c>AUPDATE</c> — typically contain commands such as <c>@function</c> and
	/// <c>@hook</c>. Returns <c>false</c> (a no-op) when the attribute is absent or empty.
	/// </summary>
	public static async ValueTask<bool> RunObjectAttributeAsync(
		IMUSHCodeParser parser,
		IAttributeService attributeService,
		AnySharpObject obj,
		string attribute,
		AnySharpObject caller)
	{
		var attr = await attributeService.GetAttributeAsync(
			caller, obj, attribute, IAttributeService.AttributeMode.Read, parent: false);

		if (!attr.IsAttribute)
		{
			return false;
		}

		var value = attr.AsAttribute.Last().Value;
		if (MarkupStringModule.getLength(value) == 0)
		{
			return false;
		}

		// Run under the object itself so `%!`/`me` resolve to the object whose attribute this is
		// (e.g. `@function header=%!,HEADER` targets the package object, which controls its own attrs).
		var runParser = parser.Push(parser.CurrentState with
		{
			Executor = obj.Object().DBRef,
			Enactor = obj.Object().DBRef,
			Caller = caller.Object().DBRef
		});

		await runParser.CommandListParse(value);
		return true;
	}
}
