using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SamplePlugin;

/// <summary>
/// The single entry type for this fixture plugin. Authoring is identical to in-tree engine code:
/// derive from <see cref="PluginBase"/>, mark with <see cref="SharpPluginAttribute"/>, and write
/// ordinary <see cref="SharpCommandAttribute"/>/<see cref="SharpFunctionAttribute"/> methods.
/// PluginBase reflects the generator-produced Generated.CommandLibrary/FunctionLibrary in this assembly.
/// </summary>
[SharpPlugin]
public sealed class SamplePlugin : PluginBase
{
	public override string Id => "sample";
	public override string Version => "1.0.0";

	[SharpCommand(Name = "+PING", MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Ping(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Services are resolved at call time from the parser's provider — the supported, unload-friendly pattern.
		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var notify = parser.ServiceProvider.GetRequiredService<INotifyService>();

		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		await notify.Notify(executor, "Pong from the sample plugin!", executor);

		return new CallState("Pong from the sample plugin!");
	}

	[SharpFunction(Name = "pluginadd", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> PluginAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var a = decimal.Parse(args["0"].Message!.ToPlainText());
		var b = decimal.Parse(args["1"].Message!.ToPlainText());
		return ValueTask.FromResult(new CallState((a + b).ToString(System.Globalization.CultureInfo.InvariantCulture)));
	}
}
