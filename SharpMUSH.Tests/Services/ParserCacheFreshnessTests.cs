using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

public class ParserCacheFreshnessFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
	internal CacheTagOrderingTests.PausingMemoryCache Memory { get; } = new(CacheTags.InheritedAttributes);
	internal CapturingAttributeStore Capture { get; private set; } = null!;

	protected override Action<IServiceCollection> ConfigureTestServices => services =>
	{
		var descriptor = services.Last(item => item.ServiceType == typeof(IAttributeStore));
		services.Remove(descriptor);
		services.Add(new ServiceDescriptor(typeof(IAttributeStore), provider =>
		{
			var inner = (IAttributeStore)(descriptor.ImplementationInstance
				?? descriptor.ImplementationFactory?.Invoke(provider)
				?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
			var proxy = DispatchProxy.Create<IAttributeStore, CapturingAttributeStore>();
			Capture = (CapturingAttributeStore)(object)proxy;
			Capture.Inner = inner;
			return proxy;
		}, descriptor.Lifetime));
		services.RemoveAll<IFusionCache>();
		services.AddSingleton(_ => Memory);
		services.AddSingleton<IFusionCache>(provider => new FusionCache(new FusionCacheOptions
		{
			DefaultEntryOptions = CacheEntryProfiles.Tagged,
		}, provider.GetRequiredService<CacheTagOrderingTests.PausingMemoryCache>()));
	};

	/// <summary>Forwards every provider call unchanged; one selected real inherited read is held after materializing.</summary>
	public class CapturingAttributeStore : DispatchProxy
	{
		public IAttributeStore Inner { get; set; } = null!;
		public DBRef Target { get; set; }
		private int _armed;
		public TaskCompletionSource<string> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public void Arm() => Interlocked.Exchange(ref _armed, 1);

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			try
			{
				var result = targetMethod!.Invoke(Inner, args);
				if (targetMethod.Name == nameof(IAttributeStore.GetAttributeWithInheritanceAsync)
					&& args is [DBRef target, string[] path, ..]
					&& target.Number == Target.Number && path.SequenceEqual(new[] { "CNT" })
					&& Interlocked.Exchange(ref _armed, 0) == 1)
				{
					return Hold((IAsyncEnumerable<AttributeWithInheritance>)result!, (CancellationToken)args[^1]!);
				}
				return result;
			}
			catch (TargetInvocationException exception) when (exception.InnerException is not null)
			{
				ExceptionDispatchInfo.Throw(exception.InnerException);
				throw;
			}
		}

		private async IAsyncEnumerable<AttributeWithInheritance> Hold(IAsyncEnumerable<AttributeWithInheritance> source,
			[EnumeratorCancellation] CancellationToken token)
		{
			var snapshot = await source.ToArrayAsync(token);
			Captured.SetResult(snapshot.Last().Attributes.Last().Value.ToPlainText());
			await Release.Task.WaitAsync(token);
			foreach (var value in snapshot) yield return value;
		}
	}
}

public class ParserCacheFreshnessTests
{
	[ClassDataSource<ParserCacheFreshnessFactory>(Shared = SharedType.PerTestSession)]
	public required ParserCacheFreshnessFactory Factory { get; init; }

	[Test]
	public async Task ALateInheritedAttributeFillCannotPoisonALaterParserGet()
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var counter = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "CacheCounter");
		var unrelated = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "OtherCounter");
		await Run($"&CNT {counter}=49");
		await Get(counter);
		await Get(counter);
		Factory.Capture.Target = counter;

		// The older writer has completed its pre-write tag store; hold only the FINAL marker commit.
		Factory.Memory.Arm(commitsToSkip: 1);
		var olderWriter = Task.Run(async () => await Run($"&OTHER {unrelated}=1"));
		Task<string>? olderReader = null;
		try
		{
			await Factory.Memory.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
			Factory.Capture.Arm();
			olderReader = Get(counter);
			await Assert.That(await Factory.Capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo("49");
			// A literal typed write avoids joining the deliberately held get() as inc(get()) would.
			await Run($"&CNT {counter}=50");
		}
		finally
		{
			Factory.Memory.Release.Set();
			try { await olderWriter; }
			finally { Factory.Capture.Release.TrySetResult(); }
		}
		var original = await olderReader!;
		var stored = await Factory.Capture.Inner.GetAttributeAsync(counter, ["CNT"]).LastAsync();
		var independent = await Get(counter);
		Console.WriteLine($"Parser freshness: captured49, original={original}, stored={stored.Value.ToPlainText()}, independent={independent}");
		await Assert.That(stored.Value.ToPlainText()).IsEqualTo("50");
		await Assert.That(independent).IsEqualTo("50")
			.Because("the parser must not reuse the inherited-attribute49 cached after typed write50");

		async Task Run(string line) => await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain(line));
		async Task<string> Get(DBRef target)
			=> (await Factory.CommandParser.EvaluateAsync(MarkupText.Plain($"get({target}/CNT)"))).ToPlainText();
	}
}
