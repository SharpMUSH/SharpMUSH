using System.Text.Json;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.BUnit.Components;

public class ApplicationDraftServiceTests
{
	private static JsonElement J(string raw)
	{
		using var document = JsonDocument.Parse(raw);
		return document.RootElement.Clone();
	}
	private static readonly JsonElement Declaration = J("""[{"key":"title","type":"text","validation":{"required":true,"max_length":8}},{"key":"text","type":"textarea"},{"key":"softcode","type":"boolean","default":false}]""");
	private static readonly ApplicationDraftKey Key = new("#1:1", "#2:2", "terminal:1:incarnation");

	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Test]
	public async Task CallbackFailureAfterEffectCannotAutomaticallyRepeatTheEffect()
	{
		var service = new ApplicationDraftService();
		var writes = 0;
		Task<JsonElement> Ok(IReadOnlyDictionary<string, JsonElement> _) => Task.FromResult(J("{\"ok\":true}"));
		Task<JsonElement> File(IReadOnlyDictionary<string, JsonElement> _) { writes++; throw new InvalidOperationException("lost callback response"); }
		await service.RunAsync(Key, "new", Declaration, J("{\"title\":\"hello\"}"), Ok, File);
		await service.RunAsync(Key, "review", Declaration, J("{}"), Ok, File);
		InvalidOperationException? failure = null;
		try { await service.RunAsync(Key, "file", Declaration, J("{}"), Ok, File); }
		catch (InvalidOperationException exception) { failure = exception; }
		await Assert.That(failure).IsNotNull();
		await Assert.That(failure!.Message).IsEqualTo("lost callback response");
		var retry = await service.RunAsync(Key, "file", Declaration, J("{}"), Ok, File);
		await Assert.That(retry.GetProperty("ok").GetBoolean()).IsFalse();
		await Assert.That(writes).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentFileRetriesSerializeAndConflictingPayloadCannotRepeatEffect()
	{
		var service = new ApplicationDraftService();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = 0;
		Task<JsonElement> Validate(IReadOnlyDictionary<string, JsonElement> _) => Task.FromResult(J("{\"ok\":true}"));
		async Task<JsonElement> File(IReadOnlyDictionary<string, JsonElement> _) { Interlocked.Increment(ref writes); entered.SetResult(); await release.Task; return J("{\"ok\":true}"); }
		await service.RunAsync(Key, "new", Declaration, J("{\"title\":\"hello\"}"), Validate, File);
		await service.RunAsync(Key, "review", Declaration, J("{}"), Validate, File);
		var first = service.RunAsync(Key, "file", Declaration, J("{}"), Validate, File);
		await entered.Task;
		var retry = service.RunAsync(Key, "file", Declaration, J("{}"), Validate, File);
		release.SetResult();
		await Task.WhenAll(first, retry);
		await Assert.That(writes).IsEqualTo(1);
		var conflict = await service.RunAsync(Key, "file", Declaration, J("{\"title\":\"changed\"}"), Validate, File);
		await Assert.That(conflict.GetProperty("ok").GetBoolean()).IsFalse();
		await Assert.That(writes).IsEqualTo(1);
	}

	[Test]
	public async Task ExpiryCancelForgedActorAndUnsupportedActionsFailClosed()
	{
		var clock = new Clock();
		var service = new ApplicationDraftService(clock);
		Task<JsonElement> Ok(IReadOnlyDictionary<string, JsonElement> _) => Task.FromResult(J("{\"ok\":true}"));
		await service.RunAsync(Key, "new", Declaration, J("{\"title\":\"hello\"}"), Ok, Ok);
		var forged = await service.RunAsync(Key, "set", Declaration, J("{\"actor\":\"#1:1\"}"), Ok, Ok);
		await Assert.That(forged.GetProperty("ok").GetBoolean()).IsFalse();
		var wrongApplication = await service.RunAsync(Key with { Application = "#1:1/OTHER" }, "file", Declaration, J("{}"), Ok, Ok);
		await Assert.That(wrongApplication.GetProperty("ok").GetBoolean()).IsFalse();
		var wrongActor = await service.RunAsync(Key with { Actor = "#3:3" }, "file", Declaration, J("{}"), Ok, Ok);
		await Assert.That(wrongActor.GetProperty("ok").GetBoolean()).IsFalse();
		clock.Now += ApplicationDraftService.Lifetime;
		await Assert.That((await service.RunAsync(Key, "file", Declaration, J("{}"), Ok, Ok)).GetProperty("ok").GetBoolean()).IsFalse();
		await service.RunAsync(Key, "new", Declaration, J("{}"), Ok, Ok);
		await service.RunAsync(Key, "cancel", Declaration, J("{}"), Ok, Ok);
		await Assert.That((await service.RunAsync(Key, "file", Declaration, J("{}"), Ok, Ok)).GetProperty("ok").GetBoolean()).IsFalse();
		await Assert.That((await service.RunAsync(Key, "execute", Declaration, J("{}"), Ok, Ok)).GetProperty("ok").GetBoolean()).IsFalse();
	}

	[Test]
	public async Task RequiredAndLengthRulesComeFromDeclarationAndTypesRemainDistinct()
	{
		var fields = J("{\"title\":\"123456789\",\"softcode\":false}");
		await Assert.That(ApplicationDraftService.ValidateFields(Declaration, fields).GetProperty("ok").GetBoolean()).IsFalse();
		var longer = J(Declaration.GetRawText().Replace("max_length\":8", "max_length\":10"));
		await Assert.That(ApplicationDraftService.ValidateFields(longer, fields).GetProperty("ok").GetBoolean()).IsTrue();
		await Assert.That(ApplicationDraftService.ValidateFields(Declaration, J("{\"title\":null}")).GetProperty("ok").GetBoolean()).IsFalse();
		await Assert.That(ApplicationDraftService.ValidateFields(Declaration, J("{\"title\":\"\"}")).GetProperty("ok").GetBoolean()).IsFalse();
		await Assert.That(ApplicationDraftService.ValidateFields(Declaration, J("{\"title\":\"hi\",\"softcode\":\"false\"}")).GetProperty("ok").GetBoolean()).IsFalse();
	}

	[Test]
	public async Task ReviewBackFile_ValidateLiteralFieldsAndReplayReceipt()
	{
		var service = new ApplicationDraftService();
		var writes = 0;
		Task<JsonElement> Validate(IReadOnlyDictionary<string, JsonElement> _) => Task.FromResult(J("{\"ok\":true}"));
		Task<JsonElement> File(IReadOnlyDictionary<string, JsonElement> _) { writes++; return Task.FromResult(J("{\"ok\":true,\"message\":\"filed\"}")); }
		await service.RunAsync(Key, "new", Declaration, J("{}"), Validate, File);
		await service.RunAsync(Key, "set", Declaration, J("{\"title\":\"hello\",\"text\":\"[set(me,W)]%;say secret\"}"), Validate, File);
		var review = await service.RunAsync(Key, "review", Declaration, J("{}"), Validate, File);
		await Assert.That(review.GetProperty("ok").GetBoolean()).IsTrue();
		await Assert.That(writes).IsEqualTo(0);
		await service.RunAsync(Key, "back", Declaration, J("{}"), Validate, File);
		var reviewAgain = await service.RunAsync(Key, "review", Declaration, J("{}"), Validate, File);
		await Assert.That(reviewAgain.GetProperty("fields").GetProperty("text").GetString()).IsEqualTo("[set(me,W)]%;say secret");
		await service.RunAsync(Key, "file", Declaration, J("{}"), Validate, File);
		await service.RunAsync(Key, "file", Declaration, J("{}"), Validate, File);
		await Assert.That(writes).IsEqualTo(1);
	}

	[Test]
	public async Task FileRechecksPermissionAndIsolatedInstancesCannotConsumeDraft()
	{
		var service = new ApplicationDraftService();
		var allowed = true;
		var writes = 0;
		Task<JsonElement> Validate(IReadOnlyDictionary<string, JsonElement> _) => Task.FromResult(J(allowed ? "{\"ok\":true}" : "{\"ok\":false,\"errors\":{\"bucket\":\"closed\"}}"));
		Task<JsonElement> File(IReadOnlyDictionary<string, JsonElement> _) { writes++; return Task.FromResult(J("{\"ok\":true}")); }
		await service.RunAsync(Key, "new", Declaration, J("{\"title\":\"hello\"}"), Validate, File);
		var review = await service.RunAsync(Key, "review", Declaration, J("{}"), Validate, File);
		await Assert.That(review.GetProperty("ok").GetBoolean()).IsTrue();
		allowed = false;
		var rejected = await service.RunAsync(Key, "file", Declaration, J("{}"), Validate, File);
		await Assert.That(rejected.GetProperty("ok").GetBoolean()).IsFalse();
		await Assert.That(writes).IsEqualTo(0);
		var other = await service.RunAsync(Key with { Instance = "terminal:2:other" }, "file", Declaration, J("{}"), Validate, File);
		await Assert.That(other.GetProperty("ok").GetBoolean()).IsFalse();
		service.RetireInstance(Key.Instance);
		await Assert.That((await service.RunAsync(Key, "file", Declaration, J("{}"), Validate, File)).GetProperty("ok").GetBoolean()).IsFalse();
	}
}
