using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace CommandOnlyPlugin;

/// <summary>
/// A deliberately minimal Phase 3 fixture: it derives from <see cref="PluginBase"/> and contributes
/// <b>only</b> one command and one function — no <see cref="IServiceRegistrar"/>, <see cref="IFlagSource"/>,
/// <see cref="IMigrationSource"/> or <see cref="IBridgeSubscriptionSource"/>. That makes it the canonical
/// <i>unloadable</i> plugin: its entire footprint lives in the command/function libraries the manager owns,
/// so its collectible <c>AssemblyLoadContext</c> can be torn down and reclaimed by the GC at runtime.
/// </summary>
[SharpPlugin]
public sealed class CommandOnlyPlugin : PluginBase
{
	public override string Id => "command-only";
	public override string Version => "1.0.0";

	[SharpCommand(Name = "+UNLOADME", MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> UnloadMe(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult(new Option<CallState>(new CallState("command-only plugin says hello")));

	[SharpFunction(Name = "unloadme", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> UnloadMeFn(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState("42"));
}
