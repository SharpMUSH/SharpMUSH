using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

public class SurrealCancellationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RejectedExpandedDataWritesAreNotReportedAsDurable(bool objectData)
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=writefailure;Database=test{Guid.NewGuid():N}")
			.AddInMemoryProvider();
		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var table = objectData ? "object_data" : "server_data";
		var definition = await client.RawQuery($"DEFINE TABLE {table} SCHEMALESS; DEFINE FIELD data ON TABLE {table} TYPE string ASSERT false;");
		await Assert.That(definition.HasErrors).IsFalse();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		await Assert.That(async () =>
		{
			if (objectData) await database.SetExpandedObjectData("Object/10", "reality", new { Value = 1 });
			else await database.SetExpandedServerData("reality", new { Value = 1 });
		}).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task CompletedQueriesReleaseCallerCancellationRegistrations()
	{
		var services = new ServiceCollection();
		services.AddSurreal($"Endpoint=mem://;Namespace=cancellation;Database=test{Guid.NewGuid():N}")
			.AddInMemoryProvider();
		await using var provider = services.BuildServiceProvider();
		var client = provider.GetRequiredService<ISurrealDbClient>();
		await client.Connect();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		using var cancellation = new CancellationTokenSource();
		for (var index = 0; index < 3; index++)
		{
			await database.SetExpandedServerData("completed", new { Value = index }, cancellation.Token);
		}

		// Embedded SDK 0.9.0 otherwise invokes disposed per-query timeout sources here.
		cancellation.Cancel();
		await Assert.That(cancellation.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async Task ActiveQueryReceivesCallerCancellation()
	{
		var client = Substitute.For<ISurrealDbClient>();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		var response = new TaskCompletionSource<SurrealDb.Net.Models.Response.SurrealDbResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken forwarded = default;
		CancellationTokenRegistration registration = default;
		client.RawQuery(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			forwarded = call.ArgAt<CancellationToken>(2);
			registration = forwarded.Register(() => response.TrySetCanceled(forwarded));
			return response.Task;
		});
		using var cancellation = new CancellationTokenSource();
		var write = database.SetExpandedServerData("active", new { Value = 1 }, cancellation.Token).AsTask();
		using var registrationLifetime = registration;
		cancellation.Cancel();
		var cancelled = false;
		try { await write; }
		catch (OperationCanceledException) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
		await Assert.That(forwarded.IsCancellationRequested).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ActiveExportAndImportReceiveCallerCancellation(bool importing)
	{
		var client = Substitute.For<ISurrealDbClient>();
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken forwarded = default;
		CancellationTokenRegistration registration = default;
		Task Wait(CancellationToken token)
		{
			forwarded = token;
			registration = token.Register(() => completion.TrySetCanceled(token));
			return completion.Task;
		}
		client.Import(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Wait(call.ArgAt<CancellationToken>(1)));
		client.Export(Arg.Any<SurrealDb.Net.Models.ExportOptions?>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			await Wait(call.ArgAt<CancellationToken>(1));
			return "";
		});
		using var cancellation = new CancellationTokenSource();
		var operation = SurrealRequestCancellation.RunAsync(async token =>
		{
			if (importing) await client.Import("CREATE test:1;", token);
			else await client.Export(cancellationToken: token);
		}, cancellation.Token);
		using var registrationLifetime = registration;
		cancellation.Cancel();
		var cancelled = false;
		try { await operation; }
		catch (OperationCanceledException) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
		await Assert.That(forwarded.IsCancellationRequested).IsTrue();
	}

	[Test]
	public async Task FailedReadIsNotReportedAsMissingDurableState()
	{
		var client = Substitute.For<ISurrealDbClient>();
		client.RawQuery(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<SurrealDb.Net.Models.Response.SurrealDbResponse>(new IOException("provider unavailable")));
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		await Assert.ThrowsAsync<IOException>(async () => await database.GetExpandedServerData<object>("durable"));
	}

	[Test]
	public async Task AlreadyCancelledWriteDoesNotReachTheClient()
	{
		var client = Substitute.For<ISurrealDbClient>();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var cancelled = false;
		try
		{
			await database.SetExpandedServerData("cancelled", new { Value = 1 }, cancellation.Token);
		}
		catch (OperationCanceledException) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
		await client.DidNotReceiveWithAnyArgs().RawQuery(default!, default, default);
	}
}
