using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Components.Mail;
using SharpMUSH.Client.Components.Settings;
using SharpMUSH.Tests.BUnit.Components.Characters;
using SharpMUSH.Tests.BUnit.Components.Mail;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>A portal served under <c>/portal/</c>: the base href is not the site root.</summary>
public sealed class SubpathNavigationManager : NavigationManager
{
	public SubpathNavigationManager(string relative) => Initialize("http://localhost/portal/", "http://localhost/portal/" + relative);

	protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
}

/// <summary>
/// Section sidebars mark the current row from the path relative to the base href. Under a non-root
/// base the absolute path carries the base's prefix and matched no route, so no row was current.
/// </summary>
public class SectionPathTests : TrackingBunitContext
{
	[Test]
	[Arguments("settings/theme", "/settings/theme", "")]
	[Arguments("mail?folder=SENT#top", "/mail", "?folder=SENT")]
	[Arguments("", "/", "")]
	[Arguments("admin/roles/", "/admin/roles", "")]
	public async Task IsRelativeToTheBase(string relative, string path, string query)
	{
		var at = SectionPath.Of(new SubpathNavigationManager(relative));
		await Assert.That(at.Path).IsEqualTo(path);
		await Assert.That(at.Query).IsEqualTo(query);
	}

	[Test]
	public async Task TheSettingsSidebar_MarksTheCurrentRow_UnderASubpathBase()
	{
		CharactersApiFake.Install(this);
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this));
		Services.AddSingleton<NavigationManager>(new SubpathNavigationManager("settings/theme"));
		var cut = Render<SettingsSidebar>();
		await Assert.That(cut.FindAll("a.kit-row[aria-current='page']").Select(a => a.GetAttribute("href")).ToList())
			.IsEquivalentTo(["/settings/theme"]);
	}

	[Test]
	public async Task TheMailSidebar_MarksTheFolder_UnderASubpathBase()
	{
		MailApiFake.Install(this);
		Services.AddSingleton<NavigationManager>(new SubpathNavigationManager("mail?folder=SENT"));
		var cut = Render<MailSidebar>();
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/mail?folder=SENT']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("a.kit-row[aria-current='page']").Select(a => a.GetAttribute("href")).ToList())
			.IsEquivalentTo(["/mail?folder=SENT"]);
	}
}
