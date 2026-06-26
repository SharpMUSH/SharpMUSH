using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using System.Collections.Concurrent;
using System.Text;
using Testcontainers.Nats;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Shared helpers used by both <see cref="BaseBenchmark"/> and <see cref="MemgraphBaseBenchmark"/>.
/// </summary>
internal static class BenchmarkHelpers
{
	private const string NatsImage = "nats:2.14-alpine";
	private const int MaxPayloadBytes = 6 * 1024 * 1024;
	private const string NatsConfigPath = "/etc/nats/nats.conf";
	private static readonly byte[] NatsConfig = Encoding.UTF8.GetBytes(
		$"max_payload: {MaxPayloadBytes}\njetstream: true\n");

	/// <summary>Starts a NATS container with JetStream enabled on a random host port.</summary>
	public static async Task<IContainer> StartNatsContainerAsync()
	{
		var container = new NatsBuilder(NatsImage)
			.WithResourceMapping(NatsConfig, NatsConfigPath)
			.WithCommand("-c", NatsConfigPath)
			.WithReuse(false)
			.Build();

		await container.StartAsync().ConfigureAwait(false);
		return container;
	}

	/// <summary>
	/// Creates a fully configured <see cref="IMUSHCodeParser"/> bound to connection handle 1 (#1).
	/// </summary>
	public static async Task<IMUSHCodeParser?> CreateTestParser(
		ISharpDatabase database,
		IServiceProvider services)
	{
		var realOne = await database.GetObjectNodeAsync(new DBRef(1)).ConfigureAwait(false);
		var one = realOne.Object()!.DBRef;

		var parser = services.GetRequiredService<IMUSHCodeParser>();
		return parser.FromState(new ParserState(
			Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
			IterationRegisters: new ConcurrentStack<IterationWrapper<MString>>(),
			RegexRegisters: new ConcurrentStack<Dictionary<string, MString>>(),
			SwitchStack: new ConcurrentStack<MString>(),
			ExecutionStack: new ConcurrentStack<Execution>(),
			EnvironmentRegisters: [],
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: "think",
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: one,
			Enactor: one,
			Caller: one,
			Handle: 1
		));
	}
}
