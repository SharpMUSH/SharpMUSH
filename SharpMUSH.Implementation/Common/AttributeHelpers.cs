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
	// TODO: Cache the Attribute Configuration for performance.

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