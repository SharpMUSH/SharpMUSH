using DependencyBasePlugin;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace DependentPlugin;

/// <summary>A behavior defined here, against the dependency's interface.</summary>
public sealed class LoudGreeting : IGreetingBehavior
{
	public string Greet(string name) => $"HELLO {name.ToUpperInvariant()}";
}

/// <summary>
/// Declares <c>dependency-base</c> in plugin.json and does not carry its DLL: it starts only when the loader shares
/// that plugin's assembly, and its behavior reaches the dependency only when both see one
/// <see cref="IGreetingBehavior"/> type.
/// </summary>
[SharpPlugin]
public sealed class DependentSamplePlugin : PluginBase
{
	public override string Id => "dependent-sample";

	public override IReadOnlyList<string> Dependencies => ["dependency-base"];

	public override void Initialize(IServiceProvider services)
	{
		base.Initialize(services);
		GreetingBehaviors.Add(new LoudGreeting());
	}

	[SharpFunction(Name = "dependentgreet", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> DependentGreet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(new LoudGreeting().Greet("dependent")));
}
