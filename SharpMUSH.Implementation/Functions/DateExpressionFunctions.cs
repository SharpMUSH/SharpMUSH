using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Time;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "DATEPARSE", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["expression", "timezone", "reference", "output"])]
	public async ValueTask<CallState> DateParse(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var normalized = args.TryGetValue("3", out var outputArg) && outputArg.Message.ToPlainText().Trim().Equals("normalized", StringComparison.OrdinalIgnoreCase);
		var precision = TimePrecision.Seconds;
		if (!normalized && !TryPrecision(args, "3", out precision)) return "#-1 INVALID PRECISION";
		var reference = DateTimeOffset.UtcNow;
		if (args.TryGetValue("2", out var referenceArg) && referenceArg.Message.ToPlainText().Length != 0
			&& !TimePrecisions.TryParseInstant(referenceArg.Message.ToPlainText(), out reference)) return "#-1 INVALID REFERENCE TIME";
		var zone = args.TryGetValue("1", out var zoneArg) ? zoneArg.Message.ToPlainText() : string.Empty;
		if (string.IsNullOrWhiteSpace(zone))
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			var attr = await AttributeService.GetAttributeAsync(executor, executor, "TZ", IAttributeService.AttributeMode.Read, true);
			zone = attr is SharpAttribute[] attributes && attributes.Length > 0 ? attributes[^1].Value.ToPlainText() : "UTC";
			if (!TimeZones.TryFind(zone, out _)) zone = "UTC";
		}
		return DateExpressionParser.Parse(args["0"].Message.ToPlainText(), zone, reference) switch
		{
			ResolvedDateExpression value => normalized ? value.Normalized : TimePrecisions.Format(value.Instant.ToUnixTimeMilliseconds(), precision),
			DateExpressionFailure failure => $"#-1 {failure.Kind.ToString().ToUpperInvariant()} DATE: {failure.Message}"
		};
	}
}
