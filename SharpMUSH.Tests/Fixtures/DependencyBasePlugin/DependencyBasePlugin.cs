using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace DependencyBasePlugin;

/// <summary>
/// The plugin another declares as a dependency. Its function makes it unloadable, so the manager's refusal to
/// unload it under a loaded dependent can be shown.
/// </summary>
[SharpPlugin]
public sealed class DependencyBasePlugin : PluginBase
{
	public override string Id => "dependency-base";

	[SharpFunction(Name = "greetall", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> GreetAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(string.Join(" ", GreetingBehaviors.All.Select(b => b.Greet("you")))));
}
