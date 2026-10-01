using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Mail;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Mail;

/// <summary>
/// The Mail section sidebar (README §6.5): "Mail" and the inbox's unread count, Compose, then one
/// row per folder with the inbox's unread pill, the folder on screen marked. Without an active
/// character there is no mailbox to list, so it says so instead.
/// </summary>
public class MailSidebarTests : TrackingBunitContext
{
	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private MailApiFake _fake = default!;

	private IRenderedComponent<MailSidebar> RenderAt(string path, bool connected = true, bool collapsed = false)
	{
		_fake = MailApiFake.Install(this, connected);
		Nav.NavigateTo(path);
		var cut = Render<MailSidebar>(p => p.Add(x => x.Collapsed, collapsed));
		if (connected)
		{
			cut.WaitForAssertion(() => cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=SENT']"), TimeSpan.FromSeconds(5));
		}

		return cut;
	}

	[Test]
	public async Task ListsCompose_AndEveryFolder_WithTheInboxUnreadPill()
	{
		var cut = RenderAt("/mail");
		await Assert.That(cut.Find(".mail-side-compose a.kit-row").GetAttribute("href")).IsEqualTo("/mail/compose");
		var folders = cut.FindAll(".mail-side-folders a.kit-row");
		await Assert.That(folders.Select(a => a.GetAttribute("href")).ToList()).IsEquivalentTo(["/mail?folder=INBOX", "/mail?folder=SENT"]);
		await Assert.That(folders[0].QuerySelector(".kit-row-unread")!.TextContent).IsEqualTo("2");
		await Assert.That(folders[1].QuerySelector(".kit-row-unread")).IsNull();
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("NavUnreadCount");
	}

	[Test]
	[Arguments("/mail", "/mail?folder=INBOX")]
	[Arguments("/mail?folder=SENT", "/mail?folder=SENT")]
	[Arguments("/mail/1?folder=SENT", "/mail?folder=SENT")]
	[Arguments("/mail/compose", "/mail/compose")]
	public async Task MarksWhereTheReaderIs(string path, string current)
	{
		var cut = RenderAt(path);
		var marked = cut.FindAll("a.kit-row[aria-current='page']").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(marked).IsEquivalentTo([current]);
	}

	[Test]
	public async Task ReadingAMessage_UpdatesTheUnreadPill()
	{
		var cut = RenderAt("/mail");
		await Services.GetRequiredService<MailService>().ReadAsync("INBOX", 1);
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=INBOX'] .kit-row-unread").TextContent != "1")
				throw new InvalidOperationException("pill not refreshed yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=INBOX'] .kit-row-unread").TextContent).IsEqualTo("1");
	}

	/// <summary>
	/// Reading a message the list knew was unread takes one off the pill where it stands: the sidebar
	/// used to re-fetch the folders and the inbox (two requests) on every read, read or not.
	/// </summary>
	[Test]
	public async Task ReadingAKnownUnreadMessage_DecrementsThePill_WithoutARequest()
	{
		var cut = RenderAt("/mail");
		var before = _fake.ListRequests;
		await Services.GetRequiredService<MailService>().ReadAsync("INBOX", 1, wasUnread: true);
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=INBOX'] .kit-row-unread").TextContent != "1")
				throw new InvalidOperationException("pill not decremented yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(_fake.ListRequests).IsEqualTo(before);
	}

	[Test]
	public async Task ReadingAKnownReadMessage_ChangesNothing()
	{
		var cut = RenderAt("/mail");
		var before = _fake.ListRequests;
		var changes = 0;
		Services.GetRequiredService<MailService>().Changed += _ => changes++;
		await Services.GetRequiredService<MailService>().ReadAsync("INBOX", 3, wasUnread: false);
		await Assert.That(changes).IsEqualTo(0);
		await Assert.That(_fake.ListRequests).IsEqualTo(before);
		await Assert.That(cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=INBOX'] .kit-row-unread").TextContent).IsEqualTo("2");
	}

	/// <summary>
	/// Switching character inside the Mail section lists the new character's mailbox. The switch rebinds
	/// the REST session but leaves the terminal alone, and the sidebar listened only to the terminal, so
	/// it went on showing the previous character's folders and unread count.
	/// </summary>
	[Test]
	public async Task SwitchingCharacter_ListsTheNewCharactersMailbox()
	{
		var cut = RenderAt("/mail");

		_fake.SwitchCharacter();

		cut.WaitForAssertion(() => cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=PLOTS']"), TimeSpan.FromSeconds(5));
		var folders = cut.FindAll(".mail-side-folders a.kit-row").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(folders).IsEquivalentTo(["/mail?folder=INBOX", "/mail?folder=PLOTS"]);
		await Assert.That(cut.Find(".mail-side-folders a.kit-row[href='/mail?folder=INBOX'] .kit-row-unread").TextContent).IsEqualTo("1");
	}

	[Test]
	public async Task WithoutACharacter_ListsNoMailbox()
	{
		var cut = RenderAt("/mail", connected: false);
		await Assert.That(cut.FindAll("a.kit-row").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".mail-side-empty").TextContent).Contains("MailNeedsCharacter");
	}

	[Test]
	public async Task Collapsed_KeepsTheIconsOnly()
	{
		var cut = RenderAt("/mail", collapsed: true);
		await Assert.That(cut.FindAll(".kit-side-head").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-section-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a.kit-row.kit-row--collapsed").Count).IsEqualTo(3);
	}

	[Test]
	public async Task Layout_PutsTheSidebarInTheShellSlot_BesideTheBody()
	{
		MailApiFake.Install(this);
		Nav.NavigateTo("/mail");
		var cut = Render<PageSidebarHost>(h => h.AddChildContent<MailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>"))));
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar.mail-shell .mail-side")).IsNotNull();
		await Assert.That(cut.Find(".kit-section-body.mail-shell #body")).IsNotNull();
	}
}
