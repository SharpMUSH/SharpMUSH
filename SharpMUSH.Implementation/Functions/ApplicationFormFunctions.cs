using System.Text.Json;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "appfields", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["field declaration", "JSON fields"])]
	public ValueTask<CallState> ApplicationFields(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		try
		{
			using var declaration = JsonDocument.Parse(parser.CurrentState.Arguments["0"].Message.ToPlainText());
			using var fields = JsonDocument.Parse(parser.CurrentState.Arguments["1"].Message.ToPlainText());
			return ValueTask.FromResult<CallState>(ApplicationDraftService.ValidateFields(declaration.RootElement, fields.RootElement).GetRawText());
		}
		catch (JsonException)
		{
			return ValueTask.FromResult<CallState>("#-1 INVALID APPLICATION FIELD JSON");
		}
	}
}
