using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SamplePlugin;

/// <summary>A marker service this fixture registers via <see cref="IServiceRegistrar"/> so the
/// integration test can assert plugin DI ran during the pre-build container construction.</summary>
public sealed class SamplePluginService
{
	public string Marker => "sample-plugin-service";
}

/// <summary>
/// The single entry type for this fixture plugin. Authoring is identical to in-tree engine code:
/// derive from <see cref="PluginBase"/>, mark with <see cref="SharpPluginAttribute"/>, and write
/// ordinary <see cref="SharpCommandAttribute"/>/<see cref="SharpFunctionAttribute"/> methods.
/// PluginBase reflects the generator-produced Generated.CommandLibrary/FunctionLibrary in this assembly.
///
/// Phase 2a: it also exercises the four contribution seams — <see cref="IServiceRegistrar"/> (a DI
/// service), <see cref="IFlagSource"/> (one flag), and <see cref="IBridgeSubscriptionSource"/> (a bridge
/// subscription that records that it ran). (No <see cref="IMigrationSource"/>: an Arango migration
/// assembly is heavy to fixture; the flag seam proves the same migration plumbing end-to-end.)
/// </summary>
[SharpPlugin]
public sealed class SamplePlugin : PluginBase, IServiceRegistrar, IFlagSource, IBridgeSubscriptionSource
{
	/// <summary>Set true once <see cref="RunAsync"/> is invoked by the host bridge service.</summary>
	public static volatile bool BridgeSubscriptionRan;

	public override string Id => "sample";
	public override string Version => "1.0.0";

	/// <summary>IServiceRegistrar: applied pre-build into the host IServiceCollection.</summary>
	public void RegisterServices(IServiceCollection services)
		=> services.AddSingleton<SamplePluginService>();

	/// <summary>IFlagSource: seeded into the active DB's flag set during migration.</summary>
	public IEnumerable<PluginFlag> Flags =>
	[
		new PluginFlag(
			Name: "SAMPLE_PLUGIN",
			Symbol: "p",
			Aliases: [],
			SetPermissions: ["wizard"],
			UnsetPermissions: ["wizard"],
			TypeRestrictions: ["ROOM", "PLAYER", "EXIT", "THING"])
	];

	/// <summary>IBridgeSubscriptionSource: run by NatsBridgeService alongside the built-ins.</summary>
	public Task RunAsync(object natsConnection, object hubContext, CancellationToken ct)
	{
		BridgeSubscriptionRan = true;
		// A real subscription would loop on nats.SubscribeAsync(...) until ct; this fixture just records
		// that it was invoked, then idles until shutdown so it mirrors a long-lived subscription.
		return Task.Delay(Timeout.Infinite, ct);
	}

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
