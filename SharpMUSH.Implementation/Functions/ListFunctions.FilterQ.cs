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
	/// <summary>
	/// filter() plus reject-capture. The register comes FIRST (setq()/setr() convention) because
	/// filter()'s argument positions 4+ carry PennMUSH-compatible extra predicate arguments
	/// (%1, %2, ...), so a register could not be appended without breaking ported softcode.
	/// </summary>
	[SharpFunction(Name = "filterq", MinArgs = 3, MaxArgs = 36, Flags = FunctionFlags.Regular, ParameterNames = ["register", "attribute", "list", "delimiter", "osep"])]
	public static async ValueTask<CallState> FilterQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var registerName = MModule.plainText(parser.CurrentState.Arguments["0"].Message!)!;
		if (string.IsNullOrWhiteSpace(registerName))
		{
			return new CallState(ErrorMessages.Returns.BadRegName);
		}

		var rawAttrArg = parser.CurrentState.Arguments["1"].Message!;
		var rawAttrStr = MModule.plainText(rawAttrArg)!;

		var delim = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 3, MModule.single(" "));
		var sep = await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 4, delim);
		var list = MModule.splitList(delim, parser.CurrentState.Arguments["2"].Message!);

		var matches = new List<MString>();
		var rejects = new List<MString>();

		if (HelperFunctions.IsLambdaOrApply(rawAttrStr))
		{
			var lambdaResults = await EvaluateLambdaOrApplyForEachItemAsync(parser, executor, rawAttrArg, list);
			foreach (var (item, result) in list.Zip(lambdaResults, (item, result) => (item, result)))
			{
				if (result.ToPlainText() == "1")
				{
					matches.Add(item);
				}
				else
				{
					rejects.Add(item);
				}
			}
		}
		else
		{
			var objAttr = HelperFunctions.SplitOptionalObjectAndAttr(rawAttrStr);
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

			// Extra args (positions 5+) reach the predicate as %1, %2, ... — filter()'s positions
			// 4+ shifted one to the right by the leading register.
			var environmentRegisters = new Dictionary<string, CallState>();
			for (var i = 5; i < parser.CurrentState.ArgumentsOrdered.Count; i++)
			{
				environmentRegisters[(i - 4).ToString()] = parser.CurrentState.ArgumentsOrdered[i.ToString()];
			}

			foreach (var item in list)
			{
				var newParser = parser.Push(parser.CurrentState with
				{
					Arguments = new Dictionary<string, CallState> { { "0", new CallState(item) } },
					EnvironmentRegisters = new Dictionary<string, CallState>(environmentRegisters)
					{
						["0"] = new CallState(item)
					}
				});

				if ((await newParser.FunctionParse(attrValue))!.Message!.ToPlainText() == "1")
				{
					matches.Add(item);
				}
				else
				{
					rejects.Add(item);
				}
			}
		}

		if (!parser.CurrentState.AddRegister(registerName.ToUpper(), MModule.multipleWithDelimiter(sep, rejects)))
		{
			return new CallState(ErrorMessages.Returns.BadRegName);
		}

		return new CallState(MModule.multipleWithDelimiter(sep, matches));
	}
}
