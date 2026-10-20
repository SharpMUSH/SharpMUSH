using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A helper's direct execution is owned by its transaction from before its first read until its last
/// write and tracking: closing waits for it, independent work is refused, and its own nested writes
/// and tracking are admitted.
/// </summary>
public partial class PackageWriteTransactionTests
{
	private static readonly PackageSettingSpec[] UnresolvedSetting = [new("messages_object", "{{desk}}")];

	private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>A write a helper makes to a store the transaction does not cover, as a wiki or role write is.</summary>
	private async Task ExternalWriteAsync(DBRef dbref, string value) =>
		await Database.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain(value), await PackageManagerAsync());

	[Test]
	public async Task DisposeWaitsForAnAdmittedHelperThenUndoesWhatItTracked()
	{
		var (_, dbref, _) = await SubjectAsync("Admitted Helper", "helper-dispose");
		var writes = await BeginAsync();
		var written = Signal();
		var release = Signal();
		var helper = Task.Run(async () =>
		{
			using var admitted = writes.AdmitHelper();
			await ExternalWriteAsync(dbref, "external");
			written.SetResult();
			await release.Task;
			writes.Track("external TX_VALUE", () => ExternalWriteAsync(dbref, "original"));
		});

		await written.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var dispose = writes.DisposeAsync().AsTask();
		await Task.Delay(100);
		await Assert.That(dispose.IsCompleted).IsFalse();
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("external");
		release.SetResult();
		await helper;
		await dispose;
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original");
	}

	[Test]
	public async Task DisposeWaitsForARealHelperPausedInItsCallback()
	{
		var services = WebAppFactoryArg.Services;
		var writes = await BeginAsync();
		var resolving = Signal();
		using var release = new ManualResetEventSlim(false);
		var notes = new List<string>();
		var helper = Task.Run(() => services.GetRequiredService<IPackageSettingService>().ApplyAsync(
			writes, "helper-callback", UnresolvedSetting, null, _ =>
			{
				resolving.TrySetResult();
				release.Wait(TimeSpan.FromSeconds(30));
				return null;
			}, notes));

		await resolving.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var dispose = writes.DisposeAsync().AsTask();
		await Task.Delay(100);
		await Assert.That(dispose.IsCompleted).IsFalse();
		release.Set();
		await Assert.That(await helper is Error<string>).IsTrue();
		await dispose;
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ClosedTransactionRefusesHelpersBeforeAnyCallbackOrWrite(bool committed)
	{
		var services = WebAppFactoryArg.Services;
		var writes = await BeginAsync();
		if (committed) writes.Commit();
		else await writes.DisposeAsync();
		var callbacks = 0;
		var notes = new List<string>();
		await Assert.That(async () => await services.GetRequiredService<IPackageSettingService>().ApplyAsync(
				writes, "helper-closed", UnresolvedSetting, null, _ => { callbacks++; return null; }, notes))
			.Throws<InvalidOperationException>().WithMessage("The package transaction is no longer open.");
		await Assert.That(async () => await services.GetRequiredService<IPackageWikiService>().ApplyAsync(
				writes, "helper-closed", "1.0", [], [], null, _ => { callbacks++; return null; }, "#7", notes))
			.Throws<InvalidOperationException>().WithMessage("The package transaction is no longer open.");
		await Assert.That(async () => await services.GetRequiredService<IPackageDeclarationService>().ApplyAsync(
				writes, "helper-closed", PackageDeclarations.None, null, notes))
			.Throws<InvalidOperationException>().WithMessage("The package transaction is no longer open.");
		await Assert.That(callbacks).IsEqualTo(0);
		await Assert.That(notes).IsEmpty();
		await writes.DisposeAsync();
	}

	[Test]
	public async Task AdmittedHelperNestsItsOwnWritesAndTrackingAndRefusesEveryoneElse()
	{
		var (_, dbref, _) = await SubjectAsync("Nested Helper", "helper-nested");
		var pm = await PackageManagerAsync();
		var writes = await BeginAsync();
		var held = Signal();
		var release = Signal();
		var helper = Task.Run(async () =>
		{
			using var admitted = writes.AdmitHelper();
			await writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("nested"), pm, CancellationToken.None);
			using (writes.AdmitHelper())
			{
				await writes.SetAttributeAsync(dbref, ["TX_ADDED"], MarkupText.Plain("inner"), pm, CancellationToken.None);
			}
			await ExternalWriteAsync(dbref, "external");
			writes.Track("external TX_VALUE", () => ExternalWriteAsync(dbref, "nested"));
			// Two writes at once from inside the helper are still one too many.
			var first = writes.SetAttributeAsync(dbref, ["TX_SIBLING"], MarkupText.Plain("a"), pm, CancellationToken.None);
			await Assert.That(async () => await writes.SetAttributeAsync(dbref, ["TX_SIBLING"], MarkupText.Plain("b"), pm,
				CancellationToken.None)).Throws<InvalidOperationException>();
			await first;
			await Assert.That(async () => await writes.DisposeAsync()).Throws<InvalidOperationException>();
			held.SetResult();
			await release.Task;
		});

		await held.Task.WaitAsync(TimeSpan.FromSeconds(30));
		await Assert.That(async () => await writes.SetAttributeAsync(dbref, ["TX_OUTSIDE"], MarkupText.Plain("x"), pm,
			CancellationToken.None)).Throws<InvalidOperationException>();
		await Assert.That(() => writes.Track("outside", () => Task.CompletedTask)).Throws<InvalidOperationException>();
		await Assert.That(() => writes.AdmitHelper()).Throws<InvalidOperationException>();
		await Assert.That(() => writes.Commit()).Throws<InvalidOperationException>();
		release.SetResult();
		await helper;

		await writes.DisposeAsync();
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original");
		await Assert.That(await AttributeAsync(dbref, "TX_ADDED")).IsNull();
		await Assert.That(await AttributeAsync(dbref, "TX_SIBLING")).IsNull();
		await Assert.That(await AttributeAsync(dbref, "TX_OUTSIDE")).IsNull();
	}

	[Test]
	public async Task ClosingRefusesAnAdmittedHelpersNextWriteButKeepsItsTracking()
	{
		var (_, dbref, _) = await SubjectAsync("Closing Helper", "helper-closing");
		var pm = await PackageManagerAsync();
		var writes = await BeginAsync();
		var written = Signal();
		var release = Signal();
		Exception? refused = null;
		var helper = Task.Run(async () =>
		{
			using var admitted = writes.AdmitHelper();
			await ExternalWriteAsync(dbref, "external");
			written.SetResult();
			await release.Task;
			writes.Track("external TX_VALUE", () => ExternalWriteAsync(dbref, "original"));
			try { await writes.SetAttributeAsync(dbref, ["TX_ADDED"], MarkupText.Plain("late"), pm, CancellationToken.None); }
			catch (InvalidOperationException error) { refused = error; }
		});

		await written.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var dispose = writes.DisposeAsync().AsTask();
		release.SetResult();
		await helper;
		await dispose;
		await Assert.That(refused?.Message).IsEqualTo("The package transaction is no longer open.");
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original");
		await Assert.That(await AttributeAsync(dbref, "TX_ADDED")).IsNull();
	}

	[Test]
	public async Task CancelledHelperReleasesItsAdmissionAndIsUndone()
	{
		var (_, dbref, _) = await SubjectAsync("Cancelled Helper", "helper-cancelled");
		var pm = await PackageManagerAsync();
		var writes = await BeginAsync();
		using var cancellation = new CancellationTokenSource();
		var written = Signal();
		var helper = Task.Run(async () =>
		{
			using var admitted = writes.AdmitHelper();
			await writes.SetAttributeAsync(dbref, ["TX_VALUE"], MarkupText.Plain("before cancel"), pm, cancellation.Token);
			written.SetResult();
			await Task.Delay(Timeout.Infinite, cancellation.Token);
		});

		await written.Task.WaitAsync(TimeSpan.FromSeconds(30));
		await cancellation.CancelAsync();
		await Assert.That(async () => await helper).Throws<OperationCanceledException>();
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("before cancel");
		await writes.DisposeAsync();
		await Assert.That(await AttributeAsync(dbref, "TX_VALUE")).IsEqualTo("original");
	}
}
