using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>Transforms one admitted utterance, sharing the caller's evaluation budget and restrictions.</summary>
public sealed class SpeechService(IAttributeService attributes, IOptionsWrapper<SharpMUSHOptions> configuration)
{
	public NameFormatter Names { get; } = new(attributes, configuration);
	public async ValueTask<CallState> TransformAsync(IMUSHCodeParser parser, AnySharpObject executor, MString message, string token)
	{
		if (token == "\"" && configuration.CurrentValue.Cosmetic.ChatStripQuote && message.ToPlainText().StartsWith('"')) message = message.Substring(1);
		var result = await parser.With(state => state with
		{
			Enactor = executor.Object().DBRef,
			Registers = new(state.Registers.Reverse().Select(frame => new Dictionary<string, MString>(frame, frame.Comparer)))
		}, scoped => attributes.EvaluateAttributeFunctionResultAsync(scoped, executor, executor, "SPEECHMOD",
			new Dictionary<string, CallState> { ["0"] = new(message), ["1"] = new(token) }, ignorePermissions: true));
		ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();
		if (parser.CurrentState.LimitExceeded?.IsExceeded == true) return result with { HadErrors = true };
		if (result.HadErrors) return result;
		return result.Message is { Length: > 0 } transformed ? new CallState(transformed) : new CallState(message);
	}
}
