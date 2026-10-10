using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Common;

/// <summary>
/// PennMUSH's <c>format_msg</c> contract (<c>src/notify.c:1287-1293</c>): an attribute that evaluates
/// to nothing is not the same as no attribute at all. The first means "this target hears nothing" -
/// <c>heard = 0</c>, the line is still delivered to everyone else - and the second means "use the
/// default rendering".
/// </summary>
public record struct Suppressed;

/// <summary>The line to deliver, <see cref="None"/> for the default, or <see cref="Suppressed"/>.</summary>
public union FormattedLine(MString, None, Suppressed);

public static class AttributeHelpers
{
	/// <summary>
	/// PennMUSH's <c>call_attrib</c> (<c>src/utils.c:409-419</c>): evaluates an attribute on
	/// <paramref name="target"/> and returns <paramref name="defaultValue"/> when it is absent or evaluates
	/// to nothing. The attribute is found as <c>fetch_ufun_attrib</c> finds it - on the object, its
	/// <c>@parent</c> chain, then its type ancestor - and with <c>UFUN_IGNORE_PERMS</c>, so whoever
	/// triggers the read need not be able to read or evaluate it.
	/// </summary>
	/// <remarks>
	/// Every caller is a <c>call_attrib</c> or <c>fetch_ufun_attrib(..., UFUN_IGNORE_PERMS)</c> site in
	/// PennMUSH: the look formats (<c>@nameformat</c>, <c>@conformat</c>, <c>@exitformat</c>),
	/// <c>@pageformat</c>/<c>@outpageformat</c> (<c>speech.c:778</c>), <c>@doing</c> (<c>bsd.c:6251</c>)
	/// and a variable exit's <c>DESTINATION</c>/<c>EXITTO</c> (<c>move.c:376-377</c>). Each receives its
	/// arguments as <c>%0</c>, <c>%1</c>, ... from <paramref name="formatArgs"/>.
	/// </remarks>
	/// <param name="attributeService">The attribute service</param>
	/// <param name="parser">Parser carrying the state the attribute body runs in. A caller with no
	/// ambient parse frame builds one with <see cref="ParserState.RootFor"/>; passing a parser whose
	/// state stack is empty throws, because the evaluation reads <c>CurrentState</c>.</param>
	/// <param name="executor">The object on whose behalf the attribute is evaluated</param>
	/// <param name="target">The object to check for the attribute</param>
	/// <param name="formatAttributeName">Name of the attribute (e.g., "NAMEFORMAT", "CONFORMAT", "DOING")</param>
	/// <param name="formatArgs">Dictionary of arguments to pass to the attribute (%0, %1, etc.)</param>
	/// <param name="defaultValue">Default value to return if attribute doesn't exist or evaluation fails</param>
	/// <returns>The formatted result or default value</returns>
	public static async ValueTask<MString> EvaluateFormatAttribute(
		IAttributeService attributeService,
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject target,
		string formatAttributeName,
		Dictionary<string, CallState> formatArgs,
		MString defaultValue)
	{
		// A limit already tripped in this evaluation halts everything after it, so the format would come
		// back as its own unevaluated code.
		if (parser.CurrentState.LimitExceeded?.IsExceeded == true)
		{
			return defaultValue;
		}

		try
		{
			// The evaluator does the lookup itself as #1, and a missing attribute evaluates to nothing.
			var formatted = await attributeService.EvaluateAttributeFunctionAsync(
				parser, executor, target, formatAttributeName, formatArgs,
				evalParent: true, ignorePermissions: true);

			return formatted.Length > 0 ? formatted : defaultValue;
		}
		catch (OperationCanceledException)
		{
			// Budget exhaustion is not "this object has no format attribute"; letting it fall through to
			// the default would hide a runaway @nameformat behind an ordinary-looking room description.
			throw;
		}
		catch
		{
			return defaultValue;
		}
	}

	/// <summary>
	/// The <c>format_msg</c> form of <see cref="EvaluateFormatAttribute"/>, for the notification path
	/// (<c>@chatformat</c>): it keeps the distinction PennMUSH draws between an absent attribute and one
	/// that deliberately produced nothing.
	///
	/// <para><c>notify_anything</c> runs the attribute and then, <c>if (!*buff) heard = 0;</c>
	/// (<c>src/notify.c:1291</c>) — the target is shown nothing and its listen patterns do not fire,
	/// while "the sound must still be propagated to other objects, which may hear something". Collapsing
	/// that to the default rendering, as <see cref="EvaluateFormatAttribute"/> does, takes away the only
	/// way softcode has to mute a channel line for one member.</para>
	/// </summary>
	public static async ValueTask<FormattedLine> EvaluateNotifyFormatAttribute(
		IAttributeService attributeService,
		IMUSHCodeParser parser,
		AnySharpObject executor,
		AnySharpObject target,
		string formatAttributeName,
		Dictionary<string, CallState> formatArgs,
		bool checkParents = false)
	{
		try
		{
			var attrResult = await attributeService.GetAttributeAsync(
				executor,
				target,
				formatAttributeName,
				IAttributeService.AttributeMode.Read,
				checkParents);

			// UFUN_REQUIRE_ATTR (notify.c:1267): no attribute, or an empty one, is not a format at all.
			if (attrResult is not SharpAttribute[] chain || chain.Last().Value.Length == 0)
			{
				return new None();
			}

			var result = await attributeService.EvaluateAttributeFunctionAsync(
				parser,
				executor,
				target,
				formatAttributeName,
				formatArgs,
				evalParent: checkParents,
				ignorePermissions: false);

			return result.Length > 0 ? result : new Suppressed();
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return new None();
		}
	}

	public static async ValueTask<CallState> GetPronoun(
		IAttributeService attributeService,
		IMediator mediator,
		IMUSHCodeParser parser,
		AnySharpObject onObject,
		string? genderAttribute,
		string? pronounAttribute,
		Func<string, string> defaultEvaluator)
	{
		var ga = await GetGenderAttribute(attributeService, await HelperFunctions.GetGod(mediator), onObject,
			genderAttribute);
		return await EvaluatePronounIndicatingAttribute(attributeService, mediator, parser, pronounAttribute,
			defaultEvaluator(ga));
	}

	/// <summary>
	/// Gets the gender attribute that indicates the pronoun. This is typically 'SEX' for legacy reasons.
	/// PennMUSH's <c>get_gender</c> (<c>src/funstr.c:70</c>) is a bare <c>atr_get</c>: inherited, and with
	/// no permission check, so it is read as God rather than as the object itself.
	/// </summary>
	private static async ValueTask<string> GetGenderAttribute(IAttributeService attributeService,
		AnySharpObject god, AnySharpObject onObject, string? attr)
	{
		var attribute = await attributeService.GetAttributeAsync(
			god,
			onObject,
			string.IsNullOrWhiteSpace(attr) ? "SEX" : attr,
			IAttributeService.AttributeMode.Read);

		return attribute is SharpAttribute[] chain
			? chain.Last().Value.ToPlainText()
			: "N";
	}

	/// <summary>
	/// Evaluates a pronoun indicating attribute, if given.
	/// </summary>
	/// <param name="attributeService">Attribute Service</param>
	/// <param name="mediator">Mediator</param>
	/// <param name="parser">Parser with current state</param>
	/// <param name="evaluationAttribute">Attribute to Evaluate</param>
	/// <param name="defaultValue">Default Value</param>
	/// <returns>Result as a CallState</returns>
	private static async ValueTask<CallState> EvaluatePronounIndicatingAttribute(
		IAttributeService attributeService,
		IMediator mediator,
		IMUSHCodeParser parser,
		string? evaluationAttribute,
		string defaultValue)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		if (evaluationAttribute is null) return defaultValue;

		if (HelperFunctions.SplitObjectAndAttr(evaluationAttribute) is not { Object: var obj, Attribute: var attr })
			return defaultValue;

		if (await mediator.Send(new GetObjectNodeQuery(DBRef.Parse(obj))) is not AnySharpObject known) return defaultValue;

		return await attributeService.EvaluateAttributeFunctionResultAsync(
			parser, executor, known, attr,
			new Dictionary<string, CallState>
			{
				{ "0", new CallState(defaultValue) }
			}, ignorePermissions: true);
	}
}