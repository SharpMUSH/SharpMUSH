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
