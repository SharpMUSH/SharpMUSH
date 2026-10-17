using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

public class PackageBatchRunRegistryTests
{
	private static readonly CapabilityActor Actor = new("initiator");
	private static readonly PackageBatchIntent Intent = new("example", PackageBatchAction.Install, new("remote", "example"));

	[Test]
	public async Task RepeatedApplyOfSameActorAndToken_AdmitsOnlyOneRun()
	{
		var registry = new PackageBatchRunRegistry();
		var plan = Review(registry);
		var first = registry.Admit(plan.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		var second = registry.Admit(plan.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		await Assert.That(first.IsNew).IsTrue();
		await Assert.That(second.IsNew).IsFalse();
		await Assert.That(second.Run.Id).IsEqualTo(first.Run.Id);
	}

	[Test]
	public async Task ReviewBelongsToItsOriginalActor()
	{
		var registry = new PackageBatchRunRegistry();
		var plan = Review(registry);
		await Assert.That(registry.Admit(plan.ReviewToken!, new("other")) is Error<string>).IsTrue();
	}

	[Test]
	public async Task ExpiredReviewCannotBeAdmitted_AndCompletedRunEventuallyBecomesUnknown()
	{
		var clock = new Clock();
		var registry = new PackageBatchRunRegistry(clock, reviewLifetime: TimeSpan.FromMinutes(1), runLifetime: TimeSpan.FromMinutes(2));
		var expired = Review(registry);
		clock.Advance(TimeSpan.FromMinutes(2));
		await Assert.That(registry.Admit(expired.ReviewToken!, Actor) is Error<string>).IsTrue();
		var plan = Review(registry);
		var admitted = registry.Admit(plan.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		registry.Update(admitted.Run.Id, PackageBatchRunState.Completed,
			[new("step", Intent, PackageBatchStepState.Completed)]);
		clock.Advance(TimeSpan.FromMinutes(3));
		await Assert.That(registry.Get(admitted.Run.Id) is NotFound).IsTrue();
	}

	[Test]
	public async Task ActiveRunNeverExpiresOrEvicts_AndCapacityRefusesAnotherAdmission()
	{
		var clock = new Clock();
		var registry = new PackageBatchRunRegistry(clock, capacity: 1, runLifetime: TimeSpan.FromMinutes(1));
		var first = registry.Admit(Review(registry).ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		clock.Advance(TimeSpan.FromDays(2));
		await Assert.That(registry.Get(first.Run.Id) is PackageBatchRunResponse).IsTrue();
		await Assert.That(registry.Admit(Review(registry).ReviewToken!, Actor) is Error<string>).IsTrue();
		await Assert.That(registry.Get(first.Run.Id).Expect<PackageBatchRunResponse>().State).IsEqualTo(PackageBatchRunState.Pending);
	}

	[Test]
	public async Task PublicProgressDoesNotContainConfigureSecrets_AndReviewChoicesAreCopied()
	{
		var registry = new PackageBatchRunRegistry();
		var answers = new Dictionary<string, string> { ["password"] = "secret-do-not-publish" };
		var plan = Review(registry, answers);
		answers["password"] = "changed-after-review";
		var admitted = registry.Admit(plan.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		await Assert.That(admitted.Snapshot.Request.Reviews![0].ConfigureAnswers!["password"]).IsEqualTo("secret-do-not-publish");
		await Assert.That(JsonSerializer.Serialize(registry.Get(admitted.Run.Id).Expect<PackageBatchRunResponse>())).DoesNotContain("secret-do-not-publish");
	}

	[Test]
	public async Task OversizedReviewIsRefusedBeforeItCanBeAdmitted()
	{
		var registry = new PackageBatchRunRegistry();
		var answers = new Dictionary<string, string> { ["large"] = new('x', 4 * 1024 * 1024) };
		var response = registry.CreateReview(Actor, new([Intent], [new("step", answers)]),
			new([new("step", Intent, "Requested")], [], [], null, null), "fingerprint");
		await Assert.That(response is Error<string>).IsTrue();
	}

	[Test]
	public async Task AggregateReviewBudgetRefusesAdditionalSecretsWithoutEvictingActiveRun()
	{
		var registry = new PackageBatchRunRegistry(retainedBytesLimit: 4096);
		var first = Review(registry, new() { ["large"] = new('x', 2600) });
		var admitted = registry.Admit(first.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		var second = registry.CreateReview(Actor, new([Intent], [new("step", new() { ["large"] = new('x', 2600) })]),
			new([new("step", Intent, "Requested")], [], [], null, null), "fingerprint");
		await Assert.That(second is Error<string>).IsTrue();
		await Assert.That(registry.Get(admitted.Run.Id) is PackageBatchRunResponse).IsTrue();
	}

	[Test]
	public async Task ConcurrentAdmissionIsAtomic()
	{
		var registry = new PackageBatchRunRegistry();
		var token = Review(registry).ReviewToken!;
		var admissions = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => registry.Admit(token, Actor).Expect<PackageBatchAdmission>())));
		await Assert.That(admissions.Count(x => x.IsNew)).IsEqualTo(1);
		await Assert.That(admissions.Select(x => x.Run.Id).Distinct().Count()).IsEqualTo(1);
	}

	[Test]
	public async Task ReturnedReviewAndAdmissionCannotChangeRetainedChoices()
	{
		var registry = new PackageBatchRunRegistry();
		var plan = Review(registry, new() { ["password"] = "original" });
		var first = registry.Admit(plan.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		first.Snapshot.Request.Reviews![0].ConfigureAnswers!["password"] = "tampered";
		var replay = registry.Admit(plan.ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		await Assert.That(replay.Snapshot.Request.Reviews![0].ConfigureAnswers!["password"]).IsEqualTo("original");
	}

	[Test]
	public async Task AdmittedTokenSurvivesReviewExpiryButNeverReexecutesAfterRunExpiry()
	{
		var clock = new Clock();
		var registry = new PackageBatchRunRegistry(clock, reviewLifetime: TimeSpan.FromMinutes(1), runLifetime: TimeSpan.FromMinutes(2));
		var token = Review(registry).ReviewToken!;
		var first = registry.Admit(token, Actor).Expect<PackageBatchAdmission>();
		clock.Advance(TimeSpan.FromMinutes(3));
		await Assert.That(registry.Admit(token, Actor).Expect<PackageBatchAdmission>().Run.Id).IsEqualTo(first.Run.Id);
		registry.Update(first.Run.Id, PackageBatchRunState.Completed, []);
		await Assert.That(registry.Admit(token, Actor).Expect<PackageBatchAdmission>().IsNew).IsFalse();
		clock.Advance(TimeSpan.FromMinutes(3));
		await Assert.That(registry.Admit(token, Actor) is Error<string>).IsTrue();
	}

	[Test]
	public async Task FailedAdmissionLeavesReviewReusableWhenCapacityFrees()
	{
		var clock = new Clock();
		var registry = new PackageBatchRunRegistry(clock, capacity: 1, runLifetime: TimeSpan.FromMinutes(1));
		var first = registry.Admit(Review(registry).ReviewToken!, Actor).Expect<PackageBatchAdmission>();
		var token = Review(registry).ReviewToken!;
		await Assert.That(registry.Admit(token, Actor) is Error<string>).IsTrue();
		registry.Update(first.Run.Id, PackageBatchRunState.Completed, []);
		clock.Advance(TimeSpan.FromMinutes(2));
		await Assert.That(registry.Admit(token, Actor).Expect<PackageBatchAdmission>().IsNew).IsTrue();
	}

	[Test]
	public async Task StatusIsBoundToTheEntireActor()
	{
		var registry = new PackageBatchRunRegistry();
		var run = registry.Admit(Review(registry).ReviewToken!, Actor).Expect<PackageBatchAdmission>().Run;
		await Assert.That(registry.Get(run.Id, new("other")) is NotFound).IsTrue();
		await Assert.That(registry.Get(run.Id, Actor) is PackageBatchRunResponse).IsTrue();
	}

	[Test]
	public async Task TerminalReplayReleasesConfigureSecrets()
	{
		var registry = new PackageBatchRunRegistry();
		var token = Review(registry, new() { ["password"] = "private" }).ReviewToken!;
		var run = registry.Admit(token, Actor).Expect<PackageBatchAdmission>().Run;
		registry.Update(run.Id, PackageBatchRunState.Completed, []);
		await Assert.That(JsonSerializer.Serialize(registry.Admit(token, Actor).Expect<PackageBatchAdmission>())).DoesNotContain("private");
	}

	[Test]
	public async Task OversizedRunningProgressKeepsActiveRunAndCannotPublishSuccess()
	{
		var clock = new Clock();
		var registry = new PackageBatchRunRegistry(clock, capacity: 1, runLifetime: TimeSpan.FromMinutes(1));
		var run = registry.Admit(Review(registry).ReviewToken!, Actor).Expect<PackageBatchAdmission>().Run;
		registry.Update(run.Id, PackageBatchRunState.Running,
			[new("step", Intent, PackageBatchStepState.Running, new('x', 4 * 1024 * 1024))]);
		clock.Advance(TimeSpan.FromDays(1));
		var status = registry.Get(run.Id, Actor).Expect<PackageBatchRunResponse>();
		await Assert.That(status.State).IsEqualTo(PackageBatchRunState.Running);
		await Assert.That(JsonSerializer.Serialize(status).Length < 4096).IsTrue();
		await Assert.That(registry.Admit(Review(registry).ReviewToken!, Actor) is Error<string>).IsTrue();
	}

	[Test]
	public async Task UnknownStepProgressCannotReplaceReviewedSteps()
	{
		var registry = new PackageBatchRunRegistry();
		var run = registry.Admit(Review(registry).ReviewToken!, Actor).Expect<PackageBatchAdmission>().Run;
		registry.Update(run.Id, PackageBatchRunState.Completed,
			[new("not-reviewed", Intent, PackageBatchStepState.Completed)]);
		var status = registry.Get(run.Id, Actor).Expect<PackageBatchRunResponse>();
		await Assert.That(status.State).IsEqualTo(PackageBatchRunState.Unknown);
		await Assert.That(status.Steps.Any(x => x.StepId == "not-reviewed")).IsFalse();
	}

	[Test]
	public async Task ProgressInputAndReturnedListsCannotMutateRetainedStatus()
	{
		var registry = new PackageBatchRunRegistry();
		var run = registry.Admit(Review(registry).ReviewToken!, Actor).Expect<PackageBatchAdmission>().Run;
		var steps = new List<PackageBatchStepProgress> { new("step", Intent, PackageBatchStepState.Running) };
		registry.Update(run.Id, PackageBatchRunState.Running, steps);
		steps.Clear();
		var returned = registry.Get(run.Id, Actor).Expect<PackageBatchRunResponse>();
		if (returned.Steps is IList<PackageBatchStepProgress> mutable)
			mutable[0] = new("tampered", Intent, PackageBatchStepState.Completed);
		var retained = registry.Get(run.Id, Actor).Expect<PackageBatchRunResponse>();
		await Assert.That(retained.Steps.Count).IsEqualTo(1);
		await Assert.That(retained.Steps[0].StepId).IsEqualTo("step");
		await Assert.That(retained.Steps[0].State).IsEqualTo(PackageBatchStepState.Running);
	}

	private static PackageBatchPlanResponse Review(PackageBatchRunRegistry registry, Dictionary<string, string>? answers = null)
		=> registry.CreateReview(Actor, new([Intent], [new("step", answers)]),
			new([new("step", Intent, "Explicitly requested")], [], [], null, null), "fingerprint")
			.Expect<PackageBatchPlanResponse>();

	private sealed class Clock : TimeProvider
	{
		private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan elapsed) => _now += elapsed;
	}
}
