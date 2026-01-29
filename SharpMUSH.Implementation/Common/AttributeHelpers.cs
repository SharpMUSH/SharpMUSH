using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

public static class AttributeHelpers
{
	// Future optimization: Consider caching attribute configuration values to reduce lookups

	/// <summary>
	/// Evaluates a @*format attribute on an object, providing a standardized way to handle display formatting.
	/// This method checks if a format attribute exists, and if so, evaluates it with the provided arguments.
	/// If the attribute doesn't exist or evaluation fails, returns the default value.
	/// </summary>
	/// <remarks>
	/// Format attributes are used throughout SharpMUSH to customize the display of various game elements.
	/// Common format attributes include:
	/// - @nameformat: Formats object name display when viewed from inside a room
	/// - @descformat: Formats @describe output when looking at objects  
	/// - @idescformat: Formats @idescribe output when looking inside objects
	/// - @conformat: Formats contents/inventory list display
	/// - @exitformat: Formats exits display in rooms
	/// - @chatformat: Formats channel messages (per-channel, per-player)
	/// - @pageformat: Formats incoming pages
	/// - @outpageformat: Formats outgoing pages
	/// 
	/// Each format attribute receives arguments via the %0, %1, etc. substitutions, which are passed
	/// in via the formatArgs dictionary. The specific arguments vary by format type - consult the
	/// documentation for each format attribute to understand what arguments it expects.
	/// 
	/// Example usage:
	/// <code>
	/// var formatArgs = new Dictionary&lt;string, CallState&gt;
	/// {
	///     ["0"] = new CallState(baseDescription)
	/// };
	/// 
	/// var formattedDesc = await AttributeHelpers.EvaluateFormatAttribute(
	///     attributeService, parser, executor, target, "DESCFORMAT", 
	///     formatArgs, baseDescription, checkParents: false);
	/// </code>
	/// </remarks>
	/// <param name="attributeService">The attribute service</param>
	/// <param name="parser">Parser with current state (can be null for non-parser contexts)</param>
	/// <param name="executor">The object executing the evaluation</param>
	/// <param name="target">The object to check for the format attribute</param>
	/// <param name="formatAttributeName">Name of the format attribute (e.g., "NAMEFORMAT", "DESCFORMAT", "CONFORMAT")</param>
	/// <param name="formatArgs">Dictionary of arguments to pass to the format attribute (%0, %1, etc.)</param>
	/// <param name="defaultValue">Default value to return if attribute doesn't exist or evaluation fails</param>
	/// <param name="checkParents">Whether to check parent objects for the attribute (default: false)</param>
	/// <returns>The formatted result or default value</returns>
	public static async ValueTask<MString> EvaluateFormatAttribute(
		IAttributeService attributeService,
		IMUSHCodeParser? parser,
		AnySharpObject executor,
		AnySharpObject target,
		string formatAttributeName,
		Dictionary<string, CallState> formatArgs,
		MString defaultValue,
		bool checkParents = false)
	{
		try
		{
			// Try to get the format attribute
			var attrResult = await attributeService.GetAttributeAsync(
				executor,
				target,
				formatAttributeName,
				IAttributeService.AttributeMode.Read,
				checkParents);

			// If attribute doesn't exist or is empty, return default
			if (attrResult.IsError || attrResult.IsNone)
			{
				return defaultValue;
			}

			var attribute = attrResult.AsAttribute.Last();
			if (MModule.getLength(attribute.Value) == 0)
			{
				return defaultValue;
			}

			// Evaluate the format attribute with the provided arguments
			var result = await attributeService.EvaluateAttributeFunctionAsync(
				parser!,
				executor,
				target,
				formatAttributeName,
				formatArgs,
				evalParent: checkParents,
				ignorePermissions: false);

			// If evaluation returns empty, use default
			return MModule.getLength(result) > 0 ? result : defaultValue;
		}
		catch
		{
			// On any error, return the default value
			return defaultValue;
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
		var ga = await GetGenderAttribute(attributeService, onObject, genderAttribute);
		return await EvaluatePronounIndicatingAttribute(attributeService, mediator, parser, pronounAttribute,
			defaultEvaluator(ga));
	}

	/// <summary>
	/// Gets the gender attribute that indicates the pronoun. This is typically 'SEX' for legacy reasons.
	/// </summary>
	/// <param name="attributeService"></param>
	/// <param name="onObject"></param>
	/// <param name="attr"></param>
	/// <returns></returns>
	private static async ValueTask<string> GetGenderAttribute(IAttributeService attributeService,
		AnySharpObject onObject, string? attr)
	{
		var attribute = await attributeService.GetAttributeAsync(
			onObject,
			onObject,
			string.IsNullOrWhiteSpace(attr) ? "SEX" : attr,
			IAttributeService.AttributeMode.Read);

		return attribute.IsAttribute
			? attribute.AsAttribute.Last().Value.ToPlainText()
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

		var split = HelperFunctions.SplitObjectAndAttr(evaluationAttribute);

		// Is None
		if (split.IsT1) return defaultValue;

		var (obj, attr) = split.AsT0;

		var directObject = await mediator.Send(new GetObjectNodeQuery(DBRef.Parse(obj)));

		if (directObject.IsNone) return defaultValue;

		var known = directObject.Known;

		return await attributeService.EvaluateAttributeFunctionAsync(
			parser, executor, known, attr,
			new Dictionary<string, CallState>
			{
				{ "0", new CallState(defaultValue) }
			}, ignorePermissions: true);
	}
}