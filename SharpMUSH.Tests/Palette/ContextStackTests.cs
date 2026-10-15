using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Palette;

/// <summary>
/// The context stack on a clock the test moves: a push goes on top, an old context sinks by halving every
/// 15 minutes, and falls off below the floor, past its lifetime, or past the depth.
/// </summary>
public class ContextStackTests
{
	private sealed class ManualClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static readonly DBRef Player = new(5000, 1_700_000_000_000);

	[Test]
	public async ValueTask APush_GoesOnTop_AndAPushedNameMovesBackUp()
	{
		var clock = new ManualClock();
		var stack = new ContextStack(clock);
		stack.Push(Player, "bboards");
		clock.Now += TimeSpan.FromMinutes(1);
		stack.Push(Player, "scene_here", new Dictionary<string, string> { ["scene"] = "12" });
		clock.Now += TimeSpan.FromMinutes(1);
		stack.Push(Player, "BBOARDS");

		var read = stack.Read(Player);
		await Assert.That(read.Select(context => context.Name)).IsEquivalentTo(new[] { "bboards", "scene_here" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(read[1].Context.Values["scene"]).IsEqualTo("12");
	}

	[Test]
	public async ValueTask AContext_HalvesEveryFifteenMinutes_AndFallsOffBelowTheFloor()
	{
		var clock = new ManualClock();
		var stack = new ContextStack(clock);
		stack.Push(Player, "who-where");

		clock.Now += TimeSpan.FromMinutes(15);
		await Assert.That(stack.Read(Player).Single().Weight).IsEqualTo(0.5).Within(1e-9);

		clock.Now += TimeSpan.FromMinutes(45);
		await Assert.That(stack.Read(Player).Single().Weight).IsEqualTo(0.0625).Within(1e-9);

		clock.Now += TimeSpan.FromMinutes(5);
		await Assert.That(stack.Read(Player)).IsEmpty().Because("0.5^(65/15) is below 0.05");
	}

	[Test]
	public async ValueTask AContext_FallsOffAtItsLifetime()
	{
		var clock = new ManualClock();
		var stack = new ContextStack(clock);
		stack.Push(Player, "my_turn", lifetime: TimeSpan.FromSeconds(60), info: "Queue a defence.");

		clock.Now += TimeSpan.FromSeconds(59);
		await Assert.That(stack.Read(Player).Single().Context.Info).IsEqualTo("Queue a defence.");

		clock.Now += TimeSpan.FromSeconds(1);
		await Assert.That(stack.Read(Player)).IsEmpty();
	}

	[Test]
	public async ValueTask AFullStack_DropsItsBottom()
	{
		var clock = new ManualClock();
		var stack = new ContextStack(clock);
		for (var i = 0; i <= ContextWeights.Depth; i++)
		{
			stack.Push(Player, $"c{i}");
			clock.Now += TimeSpan.FromSeconds(1);
		}

		var read = stack.Read(Player);
		await Assert.That(read.Count).IsEqualTo(ContextWeights.Depth);
		await Assert.That(read[0].Name).IsEqualTo($"c{ContextWeights.Depth}");
		await Assert.That(read.Any(context => context.Name == "c0")).IsFalse();
	}

	[Test]
	public async ValueTask Drop_TakesANameOff_AndSaysWhetherItWasThere()
	{
		var stack = new ContextStack(new ManualClock());
		stack.Push(Player, "combat");

		await Assert.That(stack.Drop(Player, "Combat")).IsTrue();
		await Assert.That(stack.Drop(Player, "combat")).IsFalse();
		await Assert.That(stack.Read(Player)).IsEmpty();
	}

	[Test]
	public async ValueTask ARecycledNumber_StartsWithAnEmptyStack()
	{
		var stack = new ContextStack(new ManualClock());
		stack.Push(Player, "combat");

		await Assert.That(stack.Read(Player with { CreationMilliseconds = 1_800_000_000_000 })).IsEmpty();
	}
}
