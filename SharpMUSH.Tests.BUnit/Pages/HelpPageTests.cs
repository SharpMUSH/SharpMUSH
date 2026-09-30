using Bunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The portal help pages, rendered against the real help API.
/// </summary>
/// <remarks>
/// These pages used to render <c>WikiView</c> against slugs no install ever seeds, so <c>/help</c>
/// showed "This page does not exist yet. You can create it…" to every visitor on a fresh game. The
/// assertions below are about what a reader actually sees: real index content, a real entry, a
/// disambiguation list, a plain miss — and never an invitation to author a wiki page.
/// </remarks>
public class HelpPageTests : TrackingBunitContext
{
	private const string IndexBody = """
		# help
		This is the index to the MUSH online help files.
		For an explanation of the help system, type: help [newbie]
		""";

	private const string MailBody = """
		# MAIL
		# @MAIL
		@mail invokes the built-in MUSH mailer.
		""";

	internal static readonly Dictionary<string, Dictionary<string, string>> Corpora = new()
	{
		["help"] = new(StringComparer.OrdinalIgnoreCase)
		{
			["help"] = IndexBody,
			["newbie"] = "# newbie\nIf you are new to MUSHing…",
			["MAIL"] = MailBody,
			["@MAIL"] = MailBody,
			["mail-sending"] = "# mail-sending\nHow to send mail.",
			["mail-reading"] = "# mail-reading\nHow to read mail.",
			["getting started"] = "# Getting Started\nA walkthrough.",
		},
		["ahelp"] = new(StringComparer.OrdinalIgnoreCase)
		{
			["ahelp"] = "# ahelp\nAdministrative help.",
			["Security"] = "# Security\nWizard-only security notes.",
		},
	};

	/// <summary>
	/// Waits for rendered markup to contain <paramref name="expected"/>. Written as a throwing
	/// assertion rather than a bool-returning lambda: bUnit's WaitForAssertion takes an Action, so a
	/// lambda that merely *returns* false satisfies it immediately and waits for nothing.
	/// </summary>
	private static void WaitForMarkup(Bunit.IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string expected) =>
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(expected, StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"markup does not (yet) contain '{expected}'");
			}
		}, TimeSpan.FromSeconds(5));

	private void AddHelpServices(bool isStaff)
	{
		HelpApi.Install(this, isStaff, Corpora);
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
	}

	[TUnit.Core.Test]
	public async Task Index_RendersTheShippedIndexAndEveryTopic()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "This is the index to the MUSH online help files.");

		await Assert.That(cut.Markup).Contains("This is the index to the MUSH online help files.");
		await Assert.That(cut.Markup).Contains("href=\"/help/mail-sending\"");
		await Assert.That(cut.Markup).Contains("href=\"/help/getting%20started\"");
		await Assert.That(cut.Markup).DoesNotContain("does not exist yet")
			.Because("help is not wiki content — there is nothing here for a reader to create");
	}

	[TUnit.Core.Test]
	public async Task Index_OmitsTheAdminCorpusForAMortal()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization().SetAuthorized("mortal");

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "This is the index to the MUSH online help files.");

		await Assert.That(cut.Markup).DoesNotContain("href=\"/help/admin/Security\"");
	}

	[TUnit.Core.Test]
	public async Task Index_ListsTheAdminCorpusForStaff()
	{
		AddHelpServices(isStaff: true);
		this.AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "href=\"/help/admin/Security\"");

		await Assert.That(cut.Markup).Contains("href=\"/help/admin/Security\"");
	}

	[TUnit.Core.Test]
	[Arguments("@mail", "@mail invokes the built-in MUSH mailer.")]
	[Arguments("getting started", "A walkthrough.")]
	[Arguments("newbie", "If you are new to MUSHing")]
	public async Task Topic_RendersTheResolvedEntry(string topic, string expected)
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, topic));
		WaitForMarkup(cut, expected);

		await Assert.That(cut.Markup).Contains(expected);
	}

	[TUnit.Core.Test]
	public async Task Topic_OffersTheCandidatesWhenSeveralMatch()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "mail-*"));
		WaitForMarkup(cut, "href=\"/help/mail-reading\"");

		await Assert.That(cut.Markup).Contains("href=\"/help/mail-reading\"");
		await Assert.That(cut.Markup).Contains("href=\"/help/mail-sending\"");
	}

	[TUnit.Core.Test]
	public async Task Topic_SaysNoSuchTopicRatherThanOfferingToCreateOne()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "nosuchtopicxyz"));
		WaitForMarkup(cut, "HelpNoSuchTopic");

		await Assert.That(cut.Markup).Contains("HelpNoSuchTopic");
		await Assert.That(cut.Markup).DoesNotContain("does not exist yet");
	}

	/// <summary>
	/// The admin page renders below <c>AuthorizeRouteView</c> in bUnit, so its <c>[Authorize]</c>
	/// has no runtime effect here and a redirect test would prove nothing. What is worth asserting
	/// is that the page reads the admin corpus rather than the general one — a mortal reaching it
	/// gets the server's 403, which this handler reproduces, and sees an error rather than content.
	/// </summary>
	[TUnit.Core.Test]
	public async Task AdminTopic_ReadsTheAdminCorpus()
	{
		AddHelpServices(isStaff: true);
		this.AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.HelpAdminTopic>(p => p.Add(c => c.Topic, "Security"));
		WaitForMarkup(cut, "Wizard-only security notes.");

		await Assert.That(cut.Markup).Contains("Wizard-only security notes.");
	}

	[TUnit.Core.Test]
	public async Task AdminTopic_ShowsNoContentWhenTheServerRefuses()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization().SetAuthorized("mortal");

		var cut = Render<SharpMUSH.Client.Pages.HelpAdminTopic>(p => p.Add(c => c.Topic, "Security"));
		WaitForMarkup(cut, "HelpLoadFailed");

		await Assert.That(cut.Markup).DoesNotContain("Wizard-only security notes.");
	}

	[TUnit.Core.Test]
	public async Task Index_IsAPlainHeader_WithTheEntryAndTheTopicsInCards()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "This is the index to the MUSH online help files.");

		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("AdmHelpKicker");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Help");
		await Assert.That(cut.Find(".kit-card .help-entry").TextContent).Contains("This is the index");
		await Assert.That(cut.Find(".kit-card .help-topic-list")).IsNotNull();
		await Assert.That(cut.Find(".kit-card-controls input.help-filter")).IsNotNull()
			.Because("the filter sits in the topic card's header");
	}

	/// <summary>The sidebar's search lands on <c>/help?q=</c> when no topic has exactly that name.</summary>
	[TUnit.Core.Test]
	public async Task Index_TheAddressFiltersTheTopics()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();
		Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>().NavigateTo("/help?q=mail-s");

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		WaitForMarkup(cut, "This is the index to the MUSH online help files.");

		var topics = cut.FindAll(".help-topic-list a").Select(a => a.TextContent).ToList();
		await Assert.That(topics).IsEquivalentTo(new[] { "mail-sending" });
		await Assert.That(cut.Find("input.help-filter").GetAttribute("value")).IsEqualTo("mail-s");
	}

	[TUnit.Core.Test]
	public async Task Topic_IsAPlainHeader_UnderHelp_WithTheEntryInACard()
	{
		AddHelpServices(isStaff: false);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "newbie"));
		WaitForMarkup(cut, "If you are new to MUSHing");

		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("Help");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("newbie");
		await Assert.That(cut.Find(".kit-page-actions a").GetAttribute("href")).IsEqualTo("/help");
		await Assert.That(cut.Find(".kit-card .help-entry").TextContent).Contains("If you are new to MUSHing");
	}

	[TUnit.Core.Test]
	public async Task AdminTopic_IsAPlainHeader_UnderHelpAndAdmin()
	{
		AddHelpServices(isStaff: true);
		this.AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.HelpAdminTopic>(p => p.Add(c => c.Topic, "Security"));
		WaitForMarkup(cut, "Wizard-only security notes.");

		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("Help / HelpAdminTitle");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Security");
		await Assert.That(cut.Find(".kit-card .help-entry").TextContent).Contains("Wizard-only security notes.");
	}
}
