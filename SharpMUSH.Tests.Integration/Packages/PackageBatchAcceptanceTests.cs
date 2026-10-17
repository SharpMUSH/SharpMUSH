using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

public sealed class PackageBatchAcceptanceFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
	public PackageBatchInstallFault Fault { get; } = new();
	protected override Action<IServiceCollection>? ConfigureTestServices => services =>
	{
		services.RemoveAll<IPackageInstallService>();
		services.AddSingleton<PackageInstallService>();
		services.AddSingleton<IPackageInstallService>(provider => new PackageBatchFaultingInstaller(
			provider.GetRequiredService<PackageInstallService>(),
			provider.GetRequiredService<IPackageRegistryService>(), Fault));
	};
}

public sealed class PackageBatchInstallFault
{
	public sealed record Target(string Prerequisite, string Dependent);
	public volatile Target? Armed;
	public int Failures;
}

/// <summary>Refuses only the later installer call after the real prerequisite and its registry state commit.</summary>
internal sealed class PackageBatchFaultingInstaller(IPackageInstallService real, IPackageRegistryService registry,
	PackageBatchInstallFault fault) : IPackageInstallService
{
	public Task<PackageChangeset> PlanAsync(PackageManifest manifest, IReadOnlyDictionary<string, string>? configureAnswers = null,
		CancellationToken cancellationToken = default) => real.PlanAsync(manifest, configureAnswers, cancellationToken);
	public async Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest, PackageApplyRequest request,
		CancellationToken cancellationToken = default, IPackageFileSource? fileSource = null)
	{
		if (fault.Armed is { } armed && manifest.Name == armed.Dependent
			&& await registry.GetInstalledPackageAsync(armed.Prerequisite) is InstalledPackageRecord)
		{
			Interlocked.Increment(ref fault.Failures);
			return new Error<string>("Fixture installer refused after the prerequisite committed.");
		}
		return await real.ApplyAsync(manifest, request, cancellationToken, fileSource);
	}
	public Task<Result<Success>> UninstallAsync(string packageId, bool force = false, CancellationToken cancellationToken = default) =>
		real.UninstallAsync(packageId, force, cancellationToken);
	public Task<Result<PackageRollbackResult>> RollbackAsync(string packageId, int revision, CancellationToken cancellationToken = default) =>
		real.RollbackAsync(packageId, revision, cancellationToken);
	public Task<Found<string>> GetReadmeAsync(string packageId) => real.GetReadmeAsync(packageId);
}

/// <summary>Real HTTP review, Git resolution, database writes and lifecycle execution in an isolated world.</summary>
[NotInParallel]
public class PackageBatchAcceptanceTests
{
	[ClassDataSource<PackageBatchAcceptanceFactory>(Shared = SharedType.PerTestSession)]
	public required PackageBatchAcceptanceFactory Factory { get; init; }

	private IPackageRegistryService Registry => Factory.Services.GetRequiredService<IPackageRegistryService>();

	[Test]
	public async Task UnknownRun_RequiresActualStateRefresh()
	{
		using var http = Factory.CreateHttpClient();
		using var response = await http.GetAsync($"api/packages/batches/{Guid.NewGuid():N}");
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
		await Assert.That(await response.Content.ReadAsStringAsync()).Contains("fresh review");
	}

	[Test]
	public async Task PluginOff_RecordsRestartBoundary_WhileActualRegistrationRemainsRunning()
	{
		using var http = Factory.CreateHttpClient();
		var manager = Factory.Services.GetRequiredService<IPluginManager>();
		var plugins = Factory.Services.GetRequiredService<IPluginAdministration>();
		var before = (await plugins.ListAsync()).Plugins.Single(p => p.Id == "sample");
		await Assert.That(before.Enabled).IsTrue();
		await Assert.That(before.Unloadable).IsFalse();
		await Assert.That(manager.Registration("sample") is not null).IsTrue();
		try
		{
			var preview = await PlanAsync(http, new([new("sample", PackageBatchAction.PluginOff)]));
			await Assert.That(preview.Blockers).IsEmpty();
			var run = await ApplyAndWaitAsync(http, preview);
			await Assert.That(run.State).IsEqualTo(PackageBatchRunState.AwaitingRestart);
			var after = (await plugins.ListAsync()).Plugins.Single(p => p.Id == "sample");
			await Assert.That(after.Enabled).IsFalse();
			await Assert.That(after.Pending).IsEqualTo(PluginPendingChange.Stops);
			await Assert.That(manager.Registration("sample") is not null).IsTrue();
			await Assert.That(after.Commands).IsEqualTo(before.Commands);
		}
		finally
		{
			await plugins.SetEnabledAsync("sample", true);
		}
	}

	[Test, Arguments(false), Arguments(true)]
	public async Task ReviewedDependencyPair_InstallsContentAndHooks_ThenUninstallsInReverseOrder(bool failLaterInstall)
	{
		var suffix = Guid.NewGuid().ToString("N");
		var dependency = $"batch-base-{suffix}";
		var dependent = $"batch-leaf-{suffix}";
		var remoteName = $"batch-fixture-{suffix}";
		var folder = Path.Join(Path.GetTempPath(), remoteName);
		Directory.CreateDirectory(folder);
		using var http = Factory.CreateHttpClient();
		try
		{
			WriteManifest(dependency);
			WriteManifest(dependent, dependency);
			await GitAsync(folder, "init", "-b", "main");
			await GitAsync(folder, "-c", "user.name=Acceptance", "-c", "user.email=acceptance@example.invalid", "add", ".");
			await GitAsync(folder, "-c", "user.name=Acceptance", "-c", "user.email=acceptance@example.invalid", "commit", "-m", "Isolated batch fixtures");
			await Registry.UpsertPackageRemoteAsync(new(remoteName, folder, PackageRemoteTrust.Community, "main"));

			var intents = new[]
			{
				new PackageBatchIntent(dependent, PackageBatchAction.Install, new(remoteName, dependent)),
				new PackageBatchIntent(dependency, PackageBatchAction.Install, new(remoteName, dependency))
			};
			var preview = await PlanAsync(http, new(intents));
			await Assert.That(preview.Blockers).IsEmpty();
			await Assert.That(preview.Steps.Select(s => s.Intent.TargetId).ToArray()).IsEquivalentTo([dependency, dependent]);
			await Assert.That(preview.Steps[0].Intent.TargetId).IsEqualTo(dependency);
			if (failLaterInstall) Factory.Fault.Armed = new(dependency, dependent);
			var installed = await ApplyAndWaitAsync(http, preview);
			if (failLaterInstall)
			{
				await Assert.That(installed.State).IsEqualTo(PackageBatchRunState.Partial);
				await Assert.That(Factory.Fault.Failures).IsGreaterThan(0);
				await Assert.That(installed.Steps[0].State).IsEqualTo(PackageBatchStepState.Completed);
				await Assert.That(installed.Steps[1].State).IsEqualTo(PackageBatchStepState.NeedsReview);
				await Assert.That(await Registry.GetInstalledPackageAsync(dependent) is InstalledPackageRecord).IsFalse();
				var prerequisiteMarker = (await Registry.GetPackageObjectsAsync(dependency)).Single(o => o.Ref == "marker");
				await Assert.That(await ReadAsync(DBRef.Parse(prerequisiteMarker.Objid), "INSTALL_MARKER")).IsEqualTo("installed");
				await Assert.That(await ReadAsync(DBRef.Parse(prerequisiteMarker.Objid), "CONTENT")).IsEqualTo("fixture-content");
				var revision = (await Registry.GetInstalledPackageAsync(dependency)).Expect<InstalledPackageRecord>().CurrentRevision;
				Factory.Fault.Armed = null;
				var retry = await PlanAsync(http, new([intents[0]]));
				await Assert.That(retry.Blockers).IsEmpty();
				await Assert.That(retry.Steps.Count).IsEqualTo(1);
				installed = await ApplyAndWaitAsync(http, retry);
				await Assert.That((await Registry.GetInstalledPackageAsync(dependency)).Expect<InstalledPackageRecord>().CurrentRevision).IsEqualTo(revision);
			}
			await Assert.That(installed.State).IsEqualTo(PackageBatchRunState.Completed);
			var objects = new List<DBRef>();
			foreach (var id in new[] { dependency, dependent })
			{
				await Assert.That(await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord).IsTrue();
				var marker = (await Registry.GetPackageObjectsAsync(id)).Single(o => o.Ref == "marker");
				objects.Add(DBRef.Parse(marker.Objid));
				var mediator = Factory.Services.GetRequiredService<IMediator>();
				var attribute = await mediator.CreateStream(new GetAttributeQuery(DBRef.Parse(marker.Objid), ["INSTALL_MARKER"])).LastOrDefaultAsync();
				await Assert.That(attribute?.Value.ToPlainText()).IsEqualTo("installed");
				var content = await mediator.CreateStream(new GetAttributeQuery(DBRef.Parse(marker.Objid), ["CONTENT"])).LastOrDefaultAsync();
				await Assert.That(content?.Value.ToPlainText()).IsEqualTo("fixture-content");
			}

			var removals = new[]
			{
				new PackageBatchIntent(dependency, PackageBatchAction.Uninstall),
				new PackageBatchIntent(dependent, PackageBatchAction.Uninstall)
			};
			var removePreview = await PlanAsync(http, new(removals));
			var reviewed = await PlanAsync(http, new(removals,
				removePreview.Steps.Select(s => new PackageBatchStepReview(s.StepId, ConfirmUninstall: true)).ToArray()));
			await Assert.That(reviewed.Steps[0].Intent.TargetId).IsEqualTo(dependent);
			await Assert.That((await ApplyAndWaitAsync(http, reviewed)).State).IsEqualTo(PackageBatchRunState.Completed);
			foreach (var id in new[] { dependent, dependency })
			{
				await Assert.That(await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord).IsFalse();
				await Assert.That(await Registry.GetPackageObjectsAsync(id)).IsEmpty();
			}
			foreach (var objid in objects)
			{
				var mediator = Factory.Services.GetRequiredService<IMediator>();
				var retired = (await mediator.Send(new GetObjectNodeQuery(objid))).Object()
					?? throw new InvalidOperationException("The ordinary uninstall should retire objects before garbage collection.");
				var flags = await retired.Flags.Value.Select(flag => flag.Name).ToArrayAsync();
				await Assert.That(flags).Contains("GOING");
				await Assert.That(flags).Contains("HALT");
			}
		}
		finally
		{
			Factory.Fault.Armed = null;
			var installer = Factory.Services.GetRequiredService<IPackageInstallService>();
			await installer.UninstallAsync(dependent);
			await installer.UninstallAsync(dependency);
			await Registry.RemovePackageRemoteAsync(remoteName);
			Directory.Delete(folder, recursive: true);
		}

		void WriteManifest(string id, string? prerequisite = null)
		{
			var path = Path.Join(folder, id);
			Directory.CreateDirectory(path);
			File.WriteAllText(Path.Join(path, "package.yaml"), $"""
				package: {id}
				version: "1.0.0"
				{(prerequisite is null ? "" : $"depends:\n  - {prerequisite}: \">=1.0.0\"")}
				objects:
				  - ref: marker
				    type: thing
				    name: Batch Acceptance Marker
				    attributes:
				      AINSTALL: |-
				        &INSTALL_MARKER me=installed
				      CONTENT: |-
				        fixture-content
				""");
		}
	}

	private async Task<string?> ReadAsync(DBRef objid, string name) =>
		(await Factory.Services.GetRequiredService<IMediator>().CreateStream(new GetAttributeQuery(objid, [name])).LastOrDefaultAsync())?.Value.ToPlainText();

	private static async Task<PackageBatchPlanResponse> PlanAsync(HttpClient http, PackageBatchPlanRequest request)
	{
		using var response = await http.PostAsJsonAsync("api/packages/batches/plan", request);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<PackageBatchPlanResponse>()
			?? throw new InvalidOperationException("The batch preview returned no body.");
	}

	private static async Task<PackageBatchRunResponse> ApplyAndWaitAsync(HttpClient http, PackageBatchPlanResponse preview)
	{
		if (preview.ReviewToken is not { } token) throw new InvalidOperationException("The reviewed batch has no token.");
		using var response = await http.PostAsJsonAsync("api/packages/batches/apply", new PackageBatchApplyRequest(token));
		response.EnsureSuccessStatusCode();
		var run = await response.Content.ReadFromJsonAsync<PackageBatchRunResponse>()
			?? throw new InvalidOperationException("The batch admission returned no body.");
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		while (run.State is PackageBatchRunState.Pending or PackageBatchRunState.Running)
		{
			await Task.Delay(25, timeout.Token);
			run = await http.GetFromJsonAsync<PackageBatchRunResponse>($"api/packages/batches/{run.Id}", timeout.Token)
				?? throw new InvalidOperationException("The admitted batch disappeared.");
		}
		return run;
	}

	private static async Task GitAsync(string folder, params string[] arguments)
	{
		var start = new ProcessStartInfo("git") { WorkingDirectory = folder, RedirectStandardError = true, RedirectStandardOutput = true };
		foreach (var argument in arguments) start.ArgumentList.Add(argument);
		using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start fixture Git.");
		var stderr = process.StandardError.ReadToEndAsync();
		var stdout = process.StandardOutput.ReadToEndAsync();
		await process.WaitForExitAsync();
		await stdout;
		if (process.ExitCode != 0) throw new InvalidOperationException(await stderr);
		await stderr;
	}
}
