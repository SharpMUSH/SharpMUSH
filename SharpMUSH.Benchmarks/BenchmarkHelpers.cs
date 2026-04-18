using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Shared helpers used by both <see cref="BaseBenchmark"/> and <see cref="MemgraphBaseBenchmark"/>.
/// </summary>
internal static class BenchmarkHelpers
{
	private static readonly byte[] NatsConfig =
		"max_payload: 6291456\njetstream: true\n"u8.ToArray();

	/// <summary>Starts a NATS 2 container with JetStream enabled on a random host port.</summary>
	public static async Task<IContainer> StartNatsContainerAsync()
	{
		var container = new ContainerBuilder("nats:2-alpine")
			.WithPortBinding(4222, true)
			.WithResourceMapping(NatsConfig, "/etc/nats/nats.conf")
			.WithCommand("-c", "/etc/nats/nats.conf")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
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

		var mockPublisher = Substitute.For<IPublisher>();
		var simpleConnectionService = new ConnectionService(mockPublisher);
		await simpleConnectionService.Register(
			1, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8).ConfigureAwait(false);
		await simpleConnectionService.Bind(1, one).ConfigureAwait(false);

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
