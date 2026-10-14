using System.Text.Json;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "syntaxdiagnostics", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object/attribute", "type"])]
	public async ValueTask<CallState> SyntaxDiagnostics(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		if (HelperFunctions.SplitDbRefAndOptionalAttr(parser.CurrentState.Arguments["0"].Message.ToPlainText())
			is not { Object: var objectName, Attribute: { } attributeName })
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "SYNTAXDIAGNOSTICS"));
		var requested = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message.ToPlainText().Trim();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, objectName,
			LocateFlags.All, async target =>
			{
				var found = await AttributeService.GetAttributeAsync(executor, target, attributeName,
					IAttributeService.AttributeMode.Read, parent: false);
				if (found is not SharpAttribute[] attributes) return new CallState(ErrorMessages.Returns.NoSuchAttribute);
				var attribute = attributes.Last();
				var parseType = requested?.ToLowerInvariant() switch
				{
					"command" or "commands" => ParseType.CommandList,
					"function" or "functions" => ParseType.Function,
					"" or null when attribute.SyntaxParseType() is { } inferred => inferred,
					_ => (ParseType?)null
				};
				if (parseType is null) return new CallState("#-1 SYNTAX TYPE MUST BE FUNCTION OR COMMAND");
				var diagnostics = parser.GetDiagnostics(attribute.Value, parseType.Value)
					.Select(diagnostic => new
					{
						severity = diagnostic.Severity.ToString().ToLowerInvariant(),
						line = diagnostic.Range.Start.Line + 1,
						column = diagnostic.Range.Start.Character + 1,
						message = diagnostic.Message
					});
				return new CallState(JsonSerializer.Serialize(diagnostics));
			});
	}
}
