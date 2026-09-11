using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Internal;

public class NotificationRecorderTests
{
	[Test]
	public async Task WaitIgnoresEarlierMessagesAndOtherRecipients()
	{
		var recorder = new TestHelpers.NotificationRecorder();
		var notify = TestHelpers.CreateNotifyServiceSubstitute(recorder);
		var recipient = new DBRef(42, 1000);
		await notify.Notify(recipient, MarkupText.Plain("ready"));
		var start = recorder.CountFor(recipient);
		await notify.Notify(new DBRef(43, 1000), MarkupText.Plain("ready"));

		var waiting = recorder.WaitForAsync(recipient, "ready", startIndex: start);
		await Assert.That(waiting.IsCompleted).IsFalse();
		await notify.Notify(recipient, MarkupText.Plain("ready"));
		await waiting;
	}

	[Test]
	public async Task WaitHonorsCancellation()
	{
		var recorder = new TestHelpers.NotificationRecorder();
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		await Assert.That(() => recorder.WaitForAsync(new DBRef(42), "absent",
			cancellationToken: cancellation.Token)).Throws<OperationCanceledException>();
	}

	[Test]
	public async ValueTask RecipientBucketsDistinguishCreationStamps()
	{
		var recorder = new TestHelpers.NotificationRecorder();
		var notify = TestHelpers.CreateNotifyServiceSubstitute(recorder);
		var first = new DBRef(42, 1000);
		var recycled = new DBRef(42, 2000);

		await notify.Notify(first, MarkupText.Plain("first"));
		await notify.Notify(recycled, MarkupText.Plain("recycled"));

		await Assert.That(recorder.For(first)).IsEquivalentTo(["first"]);
		await Assert.That(recorder.For(recycled)).IsEquivalentTo(["recycled"]);

		var firstRaw = recorder.RawFor(first);
		var recycledRaw = recorder.RawFor(recycled);
		await Assert.That(firstRaw.Count).IsEqualTo(1);
		await Assert.That(TestHelpers.MessagePlainTextEquals(firstRaw[0], "first")).IsTrue();
		await Assert.That(recycledRaw.Count).IsEqualTo(1);
		await Assert.That(TestHelpers.MessagePlainTextEquals(recycledRaw[0], "recycled")).IsTrue();
	}
}
