using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

public class PackageLifecycleRuntimeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task StoredHookReportsRealParserVerdictWithoutClaimingBusinessSuccess(int mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		AnySharpContainer home = god;
		var target = await mediator.Send(new CreateThingCommand("lifecycle-verdict-" + Guid.NewGuid().ToString("N"), home, god, home));
		try
		{
			var script = mode switch
			{
				0 => "&HOOK_DONE me=yes",
				1 => "nonexistent_lifecycle_command_57219; &HOOK_DONE me=yes",
				3 => "&HOOK_DONE me=yes; nonexistent_lifecycle_command_57219",
				_ => "&HOOK_DONE me=before; think [strcat(foo,bar"
			};
			await mediator.Send(new SetAttributeCommand(target, ["AINSTALL"], MarkupText.Plain(script), god));
			var legacy = Factory.Services.GetRequiredService<IPackageLifecycleRunner>();
			if (legacy is not IPackageLifecycleObserver observer) throw new InvalidOperationException("Actual lifecycle runner lacks verdict capability");
			var verdict = await observer.RunObservedLifecycleAsync(target.ToString(), "AINSTALL");
			var marker = await mediator.CreateStream(new GetAttributeQuery(target, ["HOOK_DONE"])).LastOrDefaultAsync();
			if (mode == 2)
			{
				await Assert.That(verdict is PackageLifecycleOutcome.ParseFailure or PackageLifecycleOutcome.CommandFailure).IsTrue();
				if (verdict == PackageLifecycleOutcome.ParseFailure)
					await Assert.That(marker is null).IsTrue().Because("a root parse failure ran none of the list");
				else
					await Assert.That(marker?.Value.ToPlainText()).IsEqualTo("before")
						.Because("an evaluated command failed after a preceding command already changed the object");
			}
			else
			{
				await Assert.That(verdict).IsEqualTo(mode == 0 ? PackageLifecycleOutcome.Parsed : PackageLifecycleOutcome.CommandFailure);
				// A failed command does not roll back or stop the other commands in the list.
				await Assert.That(marker?.Value.ToPlainText()).IsEqualTo("yes");
			}
		}
		finally { await mediator.Send(new DeleteObjectCommand(target)); }
	}
	[Test]
	[Arguments(PackageObjectAction.NoChange, "yes")]
	[Arguments(PackageObjectAction.Attach, null)]
	public async Task FirstInstallRunsTheHookOnlyOnThePackagesOwnObjects(PackageObjectAction action, string? expected)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		AnySharpContainer home = god;
		var target = await mediator.Send(new CreateThingCommand("lifecycle-owner-" + Guid.NewGuid().ToString("N"), home, god, home));
		try
		{
			// The object another package installed, seeding its data in AINSTALL; a bridge attaching to it is a first install too.
			await mediator.Send(new SetAttributeCommand(target, ["AINSTALL"], MarkupText.Plain("&HOOK_DONE me=yes"), god));
			var changeset = new PackageChangeset("lifecycle-bridge", null, "1.0.0", PackageRevisionKind.Install,
				[new PackageObjectChange("hints", action, PackageObjectType.Thing, "Hints", target.ToString())],
				[], [], [], [], []);

			await Factory.Services.GetRequiredService<IPackageLifecycleRunner>()
				.RunLifecycleAsync(changeset, new Dictionary<string, string>());

			var marker = await mediator.CreateStream(new GetAttributeQuery(target, ["HOOK_DONE"])).LastOrDefaultAsync();
			await Assert.That(marker?.Value.ToPlainText()).IsEqualTo(expected);
		}
		finally { await mediator.Send(new DeleteObjectCommand(target)); }
	}

	[Test]
	[NotInParallel]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ActualCoordinatorRetainsStoredHookObservation(bool malformed)
	{
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		AnySharpContainer home = god;
		var target = await mediator.Send(new CreateThingCommand("coordinator-verdict-" + Guid.NewGuid().ToString("N"), home, god, home));
		PackageOperationExecution<bool>? owned = null;
		Exception? failure = null;
		try
		{
			var script = malformed ? "&HOOK_DONE me=before; think [strcat(foo,bar"
				: "nonexistent_lifecycle_command_57219; &HOOK_DONE me=yes";
			await mediator.Send(new SetAttributeCommand(target, ["AINSTALL"], MarkupText.Plain(script), god));
			var coordinator = Factory.Services.GetRequiredService<IPackageOperationRunner>();
			if (coordinator is not IPackageOperationLifetime lifetime) throw new InvalidOperationException("Actual coordinator lacks retained lifetime");
			owned = await lifetime.StartAsync("verdict-test", _ =>
			{
				if (!PackageLifecycleDeferral.TryDefer(target.ToString(), "AINSTALL")) throw new InvalidOperationException("Not collecting");
				return Task.FromResult(false);
			}, deadline.Token);
			await Assert.That((await owned.Completion.WaitAsync(deadline.Token)).Expect<PackageOperationRan<bool>>().Result).IsFalse();
			var report = await owned.Observation.WaitAsync(deadline.Token);
			await Assert.That(report.Body).IsEqualTo(PackageTaskObservation.Returned);
			await Assert.That(report.SchedulerReleaseObservationSupported).IsTrue();
			await Assert.That(report.BodyRelease is not null).IsTrue();
			await Assert.That(report.HooksCollected).IsEqualTo(1);
			await Assert.That(report.HooksAdmitted).IsEqualTo(1);
			await Assert.That(report.HooksReleased).IsEqualTo(1);
			await Assert.That(report.HooksWithoutReleaseObservation).IsEqualTo(0);
			await Assert.That(report.HookQueueOutcomes.Values.Sum()).IsEqualTo(1);
			var marker = await mediator.CreateStream(new GetAttributeQuery(target, ["HOOK_DONE"])).LastOrDefaultAsync();
			if (malformed)
			{
				await Assert.That(report.LifecycleOutcomes.GetValueOrDefault(PackageLifecycleOutcome.ParseFailure)
					+ report.LifecycleOutcomes.GetValueOrDefault(PackageLifecycleOutcome.CommandFailure)).IsEqualTo(1);
				if (report.LifecycleOutcomes.ContainsKey(PackageLifecycleOutcome.ParseFailure))
					await Assert.That(marker is null).IsTrue();
				else await Assert.That(marker?.Value.ToPlainText()).IsEqualTo("before");
			}
			else
			{
				await Assert.That(report.LifecycleOutcomes.GetValueOrDefault(PackageLifecycleOutcome.CommandFailure)).IsEqualTo(1);
				await Assert.That(marker?.Value.ToPlainText()).IsEqualTo("yes");
			}
		}
		catch (Exception error) { failure = error; throw; }
		finally
		{
			try
			{
				if (owned is not null)
				{
					// The observer deadline cannot authorize deletion while the actual coordinator is live.
					using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
					try { await Task.WhenAll(owned.Completion, owned.Observation).WaitAsync(cleanup.Token); }
					catch (OperationCanceledException) when (failure is not null && owned.Completion.IsCanceled && owned.Observation.IsCompleted) { }
					if (failure is not null)
					{
						var report = await owned.Observation;
						TestDiagnostics.WriteLine($"coordinator verdict cleanup: coordinator={report.Coordinator}, body={report.Body}, admission={report.BodyAdmission}, hooks={report.HooksReleased}/{report.HooksAdmitted}");
					}
				}
				await mediator.Send(new DeleteObjectCommand(target));
			}
			catch (Exception cleanupError) when (failure is not null)
			{
				// Preserve both failures. An unfinished join never reaches the deletion above.
				throw new AggregateException(failure, cleanupError);
			}
		}
	}

}
