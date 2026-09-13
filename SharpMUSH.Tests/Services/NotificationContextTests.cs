using MarkupString;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class NotificationContextTests
{
	private static string Plain(SharpMessage message) => message switch { MarkupText text => text.ToPlainText(), string text => text, _ => "" };
	[Test]
	public async Task LegacyArrayAndWithInitializerTakeIndependentStampedSnapshots()
	{
		var first = new DBRef(10, 100);
		var second = new DBRef(10, 200);
		var original = new[] { first };
		var context = new NotificationContext(new DBRef(1), new DBRef(2), original);
		original[0] = second;
		var replacement = new[] { second };
		var changed = context with { ExcludedObjects = replacement };
		replacement[0] = first;
		await Assert.That(context.Exclusions.SetEquals([first])).IsTrue();
		await Assert.That(changed.Exclusions.SetEquals([second])).IsTrue();
		await Assert.That(changed.Exclusions.Contains(first)).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LegacyNotifierReceivesCompletePrefixAndBody(bool prompt)
	{
		var notify = Substitute.For<INotifyService>();
		var context = new NotificationContext(new DBRef(10), new DBRef(2), []) { Prefix = MarkupText.Plain("prefix ") };
		await notify.NotifyWithContextAsync(context, MarkupText.Plain("body"), null,
			INotifyService.NotificationType.PrivateEmit, prompt);
		if (prompt)
			await notify.Received(1).Prompt(context.Target, Arg.Is<SharpMessage>(message => Plain(message) == "prefix body"), null,
				INotifyService.NotificationType.PrivateEmit);
		else
			await notify.Received(1).Notify(context.Target, Arg.Is<SharpMessage>(message => Plain(message) == "prefix body"), null,
				INotifyService.NotificationType.PrivateEmit);
	}
}
