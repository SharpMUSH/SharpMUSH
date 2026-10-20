using Microsoft.Extensions.DependencyInjection;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task FixtureTransactionFactoryBeginsBeforeExposingWrites()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		await using var writes = await factory.CreateAsync(new DBRef(7));
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
	}

	[Test]
	public async Task FixtureTransactionFactoryRetainsUntilActualDisposalEvenAfterCommit()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		await using var writes = await factory.CreateAsync(new DBRef(7));
		writes.Commit();
		await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
		await writes.DisposeAsync();
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
	}
	[Test]
	public async Task FixtureTransactionFactoryRejectsExpiredBudgetBeforeBegin()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		using (var budget = new ExecutionBudget(TimeSpan.Zero))
		using (budget.Enter())
			await Assert.That(async () => await factory.CreateAsync(new DBRef(7))).Throws<OperationCanceledException>();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixtureTransactionFactoryInvalidManagerReleasesBegunLifetime()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		await Assert.That(async () => await factory.CreateAsync(new DBRef(0))).Throws<InvalidOperationException>();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
	}

	[Test]
	public async Task FixtureTransactionFactoryRegistrationRefusesForeignObjectStore()
	{
		await using var host = NewHost();
		await using var other = NewHost();
		await host.OpenAsync();
		await other.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var foreign = other.Services.GetRequiredService<LightningDatabase>();
		var registrations = new ServiceCollection();
		registrations.AddSingleton(provider);
		registrations.AddSingleton<IObjectStore>(foreign);
		registrations.AddPackageWriteTransactions<LightningDatabase>();
		await using var mixed = registrations.BuildServiceProvider();
		await Assert.That(() => mixed.GetRequiredService<IPackageWriteTransactionFactory>()).Throws<InvalidOperationException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await Assert.That(PackageState(foreign).SequenceEqual(UnknownPackageState(other.FixtureId, 0))).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FixtureTransactionFactoryJoinsCancelledNativeBegin(bool ambient)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		var started = NewSignal();
		using var release = new ManualResetEventSlim(false);
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, ambient ? cancellation.Token : CancellationToken.None);
		using var scope = budget.Enter();
		var blocker = Task.Run(async () => await provider.Store.WriteAsync(_ =>
		{
			started.TrySetResult();
			if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Factory native BEGIN barrier");
		}));
		Task<PackageWriteTransaction>? pending = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
			pending = factory.CreateAsync(new DBRef(7), ambient ? CancellationToken.None : cancellation.Token).AsTask();
			cancellation.Cancel();
			await Assert.That(pending.IsCompleted).IsFalse();
			await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
		}
		finally
		{
			release.Set();
			var errors = new List<Exception>();
			try { await blocker; } catch (Exception error) { errors.Add(error); }
			if (pending is not null)
			{
				try { await using var admitted = await pending; }
				catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
				catch (Exception error) { errors.Add(error); }
			}
			if (errors.Count != 0) throw new AggregateException(errors);
		}
		if (pending is not { } attempted) throw new InvalidOperationException("No factory attempt");
		await Assert.That(async () => await attempted).Throws<OperationCanceledException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FixtureTransactionFactoryRetainsThroughActualUndoAndRepeatedDispose(bool fail)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var services = host.Services;
		var provider = services.GetRequiredService<LightningDatabase>();
		var factory = services.GetRequiredService<IPackageWriteTransactionFactory>();
		var mediator = services.GetRequiredService<IMediator>();
		var writes = await factory.CreateAsync(new DBRef(7));
		await mediator.Send(new SetAttributeCommand(new DBRef(7), ["FACTORY_UNDO"], MarkupText.Plain("original"), writes.PackageManager));
		await writes.SetAttributeAsync(new DBRef(7), ["FACTORY_UNDO"], MarkupText.Plain("changed"), writes.PackageManager, CancellationToken.None);
		var started = NewSignal();
		var release = NewSignal();
		writes.Track("owned undo barrier", async () =>
		{
			started.TrySetResult();
			await release.Task;
			if (fail) throw new ArithmeticException("Owned inverse refused");
		});
		var dispose = writes.DisposeAsync().AsTask();
		Task? duplicate = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
			duplicate = writes.DisposeAsync().AsTask();
			await Assert.That(dispose.IsCompleted || duplicate.IsCompleted).IsFalse();
			await Assert.That(async () => await host.CloseAsync()).Throws<SemanticFixtureOperationInProgressException>();
			await Assert.That((await provider.GetAttributeAsync(new DBRef(7), ["FACTORY_UNDO"]).LastAsync()).Value.ToPlainText()).IsEqualTo("changed");
		}
		finally
		{
			release.TrySetResult();
			var errors = new List<Exception>();
			try { await dispose; } catch (PackageCompensationException) when (fail) { } catch (Exception error) { errors.Add(error); }
			if (duplicate is not null)
			{
				try { await duplicate; } catch (PackageCompensationException) when (fail) { } catch (Exception error) { errors.Add(error); }
			}
			if (errors.Count != 0) throw new AggregateException(errors);
		}
		if (fail) await Assert.That(async () => await writes.DisposeAsync()).Throws<PackageCompensationException>();
		else await writes.DisposeAsync();
		await Assert.That((await provider.GetAttributeAsync(new DBRef(7), ["FACTORY_UNDO"]).LastAsync()).Value.ToPlainText()).IsEqualTo("original");
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That((await reopened.GetAttributeAsync(new DBRef(7), ["FACTORY_UNDO"]).LastAsync()).Value.ToPlainText()).IsEqualTo("original");
	}

}
