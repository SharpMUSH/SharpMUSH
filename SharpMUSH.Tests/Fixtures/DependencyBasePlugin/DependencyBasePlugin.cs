using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace DependencyBasePlugin;

/// <summary>A behavior another plugin adds to this one, as a plugin adds a Scene behavior.</summary>
public interface IGreetingBehavior
{
	string Greet(string name);
}

/// <summary>The behaviors other plugins added, in order.</summary>
public static class GreetingBehaviors
{
	private static readonly List<IGreetingBehavior> Behaviors = [];

	public static void Add(IGreetingBehavior behavior)
	{
		lock (Behaviors) Behaviors.Add(behavior);
	}

	public static IReadOnlyList<IGreetingBehavior> All
	{
		get { lock (Behaviors) return [.. Behaviors]; }
	}
}

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
