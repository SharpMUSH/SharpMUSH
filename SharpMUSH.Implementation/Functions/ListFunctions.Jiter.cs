using MoreLinq.Extensions;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "jiter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["attributes", "input", "osep"])]
	public static async ValueTask<CallState> JuxtaposedIteration(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var attrListStr = MModule.plainText(parser.CurrentState.Arguments["0"].Message!)!;
		var tokens = attrListStr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		// jiter fans ONE input across every attribute: each is evaluated with the same %0,
		// side by side (contrast chain(), which threads each result into the next step).
		var input = parser.CurrentState.Arguments["1"].Message ?? MModule.empty();
		var osep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 2, MModule.single(" "));

		if (tokens.Length == 0)
		{
			return CallState.Empty;
		}

		var results = new List<MString>(tokens.Length);

		foreach (var objAttr in tokens.Select(HelperFunctions.SplitOptionalObjectAndAttr))
		{
			if (objAttr is { IsT1: true, AsT1: false })
			{
				return new CallState(ErrorMessages.Returns.ObjectAttributeString);
			}

			var (dbref, attrName) = objAttr.AsT0;
			dbref ??= executor.ToString();

			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, dbref, LocateFlags.All);
			if (!locate.IsValid())
			{
				return CallState.Empty;
			}

			var located = locate.WithoutError().WithoutNone();

			var maybeAttr = await AttributeService!.GetAttributeAsync(
				executor, located, attrName, mode: IAttributeService.AttributeMode.Execute, parent: true);
			if (maybeAttr.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchAttribute);
			}

			if (maybeAttr.IsError)
			{
				return new CallState(maybeAttr.AsError.Value);
			}

			var attrValue = maybeAttr.AsAttribute.Last().Value;

			var env = new Dictionary<string, CallState> { ["0"] = new CallState(input) };

			var stepParser = parser.Push(parser.CurrentState with
			{
				Arguments = new Dictionary<string, CallState>(env),
				EnvironmentRegisters = env
			});

			results.Add((await stepParser.FunctionParse(attrValue))!.Message!);
		}

		return new CallState(MModule.multipleWithDelimiter(osep, results));
	}
}
