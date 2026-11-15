using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// <see cref="MailWatch"/> reads the <c>comm-feed</c> package's <c>mail.new</c> push off the command terminal: each one
/// reads the inbox's unread count again and announces the message.
/// </summary>
public class MailWatchTests : TrackingBunitContext
{
	[Test]
	public async Task ParsesTheMailNewPayload()
	{
		var mail = MailWatch.Parse("""{"v":2,"number":3,"from":"Wren","fromObjid":"#12:1","subject":"Docks","urgent":true}""");

		await Assert.That(mail).IsEqualTo(new MailWatch.NewMail(3, "Wren", "Docks", true));
	}

	[Test]
	[Arguments("not json")]
	[Arguments("[1,2]")]
	[Arguments("""{"v":2,"from":"Wren"}""")]
	[Arguments("""{"number":"three"}""")]
	public async Task AnythingElseIsNoMessage(string json)
	{
		await Assert.That(MailWatch.Parse(json)).IsNull();
	}

	/// <summary>A push reads the count again before it announces the message, so the badge and the notice agree.</summary>
	[Test]
	public async Task APush_ReadsTheCountAgain_ThenAnnouncesTheMessage()
	{
		var fake = SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		var watch = Services.GetRequiredService<MailWatch>();
		await watch.RefreshAsync();
		await Assert.That(watch.InboxUnread).IsEqualTo(2);

		var arrived = new TaskCompletionSource<(MailWatch.NewMail Mail, int? Unread)>();
		watch.Arrived += mail => arrived.TrySetResult((mail, watch.InboxUnread));
		fake.Deliver(new(4, "Mara Quill", "Second bell", DateTimeOffset.UnixEpoch, Read: false, Urgent: false, "INBOX"));

		var (mail, unread) = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(mail).IsEqualTo(new MailWatch.NewMail(4, "Mara Quill", "Second bell", false) { InInbox = true });
		await Assert.That(unread).IsEqualTo(3);
	}

	/// <summary>A message filed out of the inbox (a MAILFILTER) is announced, but not as an inbox number to open.</summary>
	[Test]
	public async Task AMessageNotInTheInbox_IsNotLinkedByItsNumber()
	{
		var fake = SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		var watch = Services.GetRequiredService<MailWatch>();
		await watch.RefreshAsync();

		var arrived = new TaskCompletionSource<MailWatch.NewMail>();
		watch.Arrived += mail => arrived.TrySetResult(mail);
		fake.Oob.Set(MailWatch.NewMailPackage, """{"v":2,"number":9,"from":"Wren","subject":"Filed","urgent":false}""");

		var mail = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(mail.InInbox).IsFalse();
	}

	/// <summary>A message for the previous character, still being counted when the tab switches, is not announced.</summary>
	[Test]
	public async Task AMessageForThePreviousCharacter_IsNotAnnounced()
	{
		var fake = SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		var watch = Services.GetRequiredService<MailWatch>();
		await watch.RefreshAsync();

		var announced = 0;
		watch.Arrived += _ => announced++;
		var gate = fake.HoldLists = new TaskCompletionSource();
		fake.Deliver(new(4, "Mara Quill", "Second bell", DateTimeOffset.UnixEpoch, Read: false, Urgent: false, "INBOX"));
		fake.SwitchCharacter();
		gate.SetResult();
		await watch.RefreshAsync();
		// Nothing to wait for when nothing comes: give the held arrival time to finish, then look.
		await Task.Delay(TimeSpan.FromMilliseconds(200));

		await Assert.That(announced).IsEqualTo(0);
	}
}
