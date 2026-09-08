using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

public class LockRecursionTests
{
	private static IOptionsMonitor<SharpMUSHOptions> Options(uint maxDepth)
	{
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		var config = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		options.CurrentValue.Returns(config with { Limit = config.Limit with { MaxDepth = maxDepth } });
		return options;
	}

	[Test]
	public async Task ChainBeyondMaxDepthDenies()
	{
		var parser = Substitute.For<IBooleanExpressionParser>();
		var service = new LockService(parser, Options(10));
		var obj = new TestObjectFactory().CreateThing(1, "Unlocker");
		parser.Compile(Arg.Any<string>()).Returns(call =>
		{
			var remaining = int.Parse(call.Arg<string>());
			return (AnySharpObject gated, AnySharpObject unlocker) =>
				remaining == 0 ? ValueTask.FromResult(true) : service.Evaluate((remaining - 1).ToString(), gated, unlocker);
		});

		await Assert.That(await service.Evaluate("100", obj, obj)).IsFalse();
	}

	[Test]
	[Arguments(0, 0, true)]
	[Arguments(0, 1, false)]
	[Arguments(2, 1, true)]
	[Arguments(2, 2, true)]
	[Arguments(2, 3, false)]
	public async Task IndirectChainHonorsConfiguredBoundary(int maxDepth, int hops, bool expected)
	{
		using var fixture = new IndirectFixture((uint)maxDepth);
		var target = fixture.Add("End", "#TRUE");
		for (var i = 0; i < hops; i++)
			target = fixture.Add($"Link{i}", $"@{target.Object().Name}");

		await Assert.That(await fixture.Service.Evaluate(LockType.Basic, target, target)).IsEqualTo(expected);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task IndirectCyclesDenyAndDoNotPoisonLaterChecks(bool mutual)
	{
		using var fixture = new IndirectFixture(3);
		var first = fixture.Add("First", mutual ? "@Second" : "@First");
		fixture.Add("Second", "@First");

		await Assert.That(await fixture.Service.Evaluate(LockType.Basic, first, first)).IsFalse();
		await Assert.That(await fixture.Service.Evaluate("#TRUE", first, first)).IsTrue();
	}

	[Test]
	public async Task SiblingIndirectChecksHaveIndependentDepthBudgets()
	{
		using var fixture = new IndirectFixture(1);
		var end = fixture.Add("End", "#TRUE");
		await Assert.That(await fixture.Service.Evaluate("@End&@End", end, end)).IsTrue();
	}

	[Test]
	public async Task ConcurrentEvaluationsDoNotShareDepth()
	{
		var parser = Substitute.For<IBooleanExpressionParser>();
		var service = new LockService(parser, Options(0));
		var obj = new TestObjectFactory().CreateThing(1, "Unlocker");
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.Compile("wait").Returns(_ => async (AnySharpObject _, AnySharpObject _) =>
		{
			entered.SetResult();
			await release.Task;
			return true;
		});
		var pending = service.Evaluate("wait", obj, obj).AsTask();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(await service.Evaluate("#TRUE", obj, obj)).IsTrue();
		}
		finally
		{
			release.SetResult();
		}
		await Assert.That(await pending).IsTrue();
	}

	[Test]
	public async Task ExceptionRestoresDepthForSiblingEvaluation()
	{
		var parser = Substitute.For<IBooleanExpressionParser>();
		var service = new LockService(parser, Options(1));
		var obj = new TestObjectFactory().CreateThing(1, "Unlocker");
		parser.Compile("throw").Returns(_ => (AnySharpObject _, AnySharpObject _) =>
			throw new InvalidOperationException("test failure"));
		parser.Compile("outer").Returns(_ => async (AnySharpObject gated, AnySharpObject unlocker) =>
		{
			try
			{
				await service.Evaluate("throw", gated, unlocker);
			}
			catch (InvalidOperationException)
			{
				return await service.Evaluate("#TRUE", gated, unlocker);
			}
			return false;
		});
		await Assert.That(await service.Evaluate("outer", obj, obj)).IsTrue();
	}

	[Test]
	public async Task ChannelLockUsesTheSameDepthBoundary()
	{
		using var fixture = new IndirectFixture(1);
		var end = fixture.Add("End", "#TRUE");
		fixture.Add("Link", "@End");
		var channel = CreateChannel();
		await Assert.That(await fixture.Service.Evaluate("@End", channel, end)).IsTrue();
		await Assert.That(await fixture.Service.Evaluate("@Link", channel, end)).IsFalse();
	}

	[Test]
	public async Task NestedUnlockedChannelCannotBypassDepthBoundary()
	{
		var parser = Substitute.For<IBooleanExpressionParser>();
		var service = new LockService(parser, Options(0));
		var obj = new TestObjectFactory().CreateThing(1, "Unlocker");
		var channel = CreateChannel();
		parser.Compile("channel").Returns(_ => (AnySharpObject _, AnySharpObject unlocker) =>
			service.Evaluate("#TRUE", channel, unlocker));
		await Assert.That(await service.Evaluate("channel", obj, obj)).IsFalse();
	}

	private static SharpChannel CreateChannel()
	{
		var owner = new TestObjectFactory().CreatePlayer(100, "Owner").AsT0;
		return new SharpChannel
		{
			Name = MarkupText.Plain("Test"),
			Owner = new DotNext.Threading.AsyncLazy<SharpPlayer>(_ => Task.FromResult(owner)),
			Members = new(() => AsyncEnumerable.Empty<SharpChannel.MemberAndStatus>()),
			Privs = []
		};
	}

	private sealed class IndirectFixture : IDisposable
	{
		private readonly TestObjectFactory _factory = new();
		private readonly Dictionary<string, AnySharpObject> _objects = new();
		private readonly FusionCache _cache = new(new FusionCacheOptions());
		public LockService Service { get; }

		public IndirectFixture(uint maxDepth)
		{
			var services = Substitute.For<ILockEvaluationServices>();
			var parser = new BooleanExpressionParser(services, Substitute.For<IMediator>(), _cache);
			Service = new LockService(parser, Options(maxDepth));
			services.LocateAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<LocateFlags>())
				.Returns(call => new ValueTask<AnyOptionalSharpObjectOrError>(_objects[call.Arg<string>()].AsT3));
			services.EvaluateLock(Arg.Any<string>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
				.Returns(call => Service.Evaluate(call.Arg<string>(), call.ArgAt<AnySharpObject>(1), call.ArgAt<AnySharpObject>(2)));
		}

		public AnySharpObject Add(string name, string expression)
		{
			var obj = _factory.CreateThing(_objects.Count + 1, name);
			obj.Object().Locks = ImmutableDictionary<string, SharpLockData>.Empty.Add("Basic", new SharpLockData(expression));
			_objects.Add(name, obj);
			return obj;
		}

		public void Dispose() => _cache.Dispose();
	}
}
