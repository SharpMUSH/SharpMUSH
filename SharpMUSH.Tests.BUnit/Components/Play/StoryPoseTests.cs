using Bunit;
using MarkupString;
using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.4 story row (boards 01, 05): a 44px portrait, the name in its colour and the time, then
/// the pose; an OOC pose is the band (§4.10); names of the others are mentions.
/// </summary>
public class StoryPoseTests : BunitContext
{
	public StoryPoseTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static ScenePoseView Pose(string author = "Tomas Reyes", string showAs = "", IReadOnlyList<string>? tags = null,
		string markup = "leans on a stack of crates", bool deleted = false, int edits = 1) =>
		new("P1", "42", "#312", author, showAs, "#1201", "Lower Docks", "pose", tags ?? [], new Dictionary<string, string>(),
			new DateTimeOffset(2026, 9, 30, 17, 2, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), deleted, markup, markup, edits, null, null, null);

	[Test]
	public async Task APose_HasItsPortrait_TheNameInItsColour_TheTime_AndTheText()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()).Add(x => x.ImageUrl, "/api/wiki-assets/t/tomas.jpg").Add(x => x.Color, "#ffb454"));
		await Assert.That(cut.Find("img.story-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
		await Assert.That(cut.Find(".story-name").TextContent).IsEqualTo("Tomas Reyes");
		await Assert.That(cut.Find(".story-name").GetAttribute("style")).Contains("color:#ffb454");
		await Assert.That(cut.Find(".story-time").TextContent)
			.IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(Pose().CreatedAt).ToLocalTime().ToString("HH:mm"));
		await Assert.That(cut.Find(".story-body").TextContent).IsEqualTo("leans on a stack of crates");
	}

	[Test]
	public async Task WithoutAPicture_ThePortraitIsInitials_OnTheNamesTint()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()).Add(x => x.ImageUrl, "javascript:alert(1)"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".story-portrait--initials").TextContent).IsEqualTo("TR");
		await Assert.That(cut.Find(".story-name").GetAttribute("style")).IsNull()
			.Because("with no colour sent the name takes the default");
	}

	[Test]
	public async Task AnOocPose_IsTheBand_WithInitialsInPlaceOfThePortrait()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(author: "Wren Halloway", tags: ["ooc"], markup: "brb, making tea"))
			.Add(x => x.ImageUrl, "/w.jpg").Add(x => x.Color, "#6aa7ff"));
		await Assert.That(cut.Find(".kit-ooc .kit-ooc-initials").TextContent).IsEqualTo("WH");
		await Assert.That(cut.Find(".kit-ooc-name").TextContent).IsEqualTo("Wren Halloway");
		await Assert.That(cut.Find(".kit-ooc-text").TextContent).IsEqualTo("brb, making tea");
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
	}

	/// <summary>
	/// The <c>ooc</c> command records <c>Name: words</c>; the band already names the speaker, so it shows the
	/// words. A posed OOC line keeps its name, which is part of the sentence.
	/// </summary>
	[Test]
	[Arguments("Wren Halloway: brb, making tea", "brb, making tea")]
	[Arguments("Wren Halloway waves.", "Wren Halloway waves.")]
	[Arguments("Tomas: hi", "Tomas: hi")]
	public async Task AnOocBand_DropsTheSpeakersNameFromWhatWasSaid(string recorded, string shown)
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(author: "Wren Halloway", tags: ["ooc"], markup: recorded)));
		await Assert.That(cut.Find(".kit-ooc-text").TextContent).IsEqualTo(shown);
	}

	[Test]
	public async Task APersona_ShowsItsOwnName()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(author: "Alice", showAs: "Mysterious Stranger")));
		await Assert.That(cut.Find(".story-name").TextContent).IsEqualTo("Mysterious Stranger");
	}

	[Test]
	public async Task EditedAndDeleted_AreMarked()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(deleted: true, edits: 3)));
		await Assert.That(cut.Find(".story-row").ClassList).Contains("story-row--deleted");
		await Assert.That(cut.FindAll(".story-edited").Count).IsEqualTo(1);
	}

	[Test]
	public async Task YourOwnPose_HasEdit_WhichSavesTheWholeText()
	{
		(string PoseId, string Text)? saved = null;
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()).Add(x => x.CanEdit, true)
			.Add(x => x.OnEdit, e => saved = e));
		var edit = cut.Find("button.story-edit-btn");
		await Assert.That(edit.GetAttribute("aria-label")).IsEqualTo("Edit");
		await edit.ClickAsync();
		await Assert.That(cut.Find("textarea.story-editor-input").GetAttribute("value")).IsEqualTo("leans on a stack of crates");
		await Assert.That(cut.FindAll(".story-body").Count).IsEqualTo(0).Because("the text is being edited in its place");

		await cut.Find("textarea.story-editor-input").InputAsync("leans on the crates, waiting.");
		await cut.Find("button.story-editor-save").ClickAsync();
		await Assert.That(saved).IsEqualTo(("P1", @"leans on the crates\, waiting."))
			.Because("the box is the pose as shown, and goes back as decompose() writes it");
		await Assert.That(cut.FindAll(".story-editor").Count).IsEqualTo(0);
	}

	[Test]
	public async Task YourOwnOocPose_HasEditToo()
	{
		(string PoseId, string Text)? saved = null;
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(tags: ["ooc"], markup: "brb, making tea")).Add(x => x.CanEdit, true)
			.Add(x => x.OnEdit, e => saved = e));
		await Assert.That(cut.FindAll(".story-row--ooc").Count).IsEqualTo(1);
		await cut.Find("button.story-edit-btn").ClickAsync();
		await Assert.That(cut.Find("textarea.story-editor-input").GetAttribute("value")).IsEqualTo("brb, making tea")
			.Because("the box holds the pose as it shows");
		await cut.Find("textarea.story-editor-input").InputAsync("back, tea made");
		await cut.Find("button.story-editor-save").ClickAsync();
		await Assert.That(saved).IsEqualTo(("P1", @"back\, tea made"));
	}

	[Test]
	public async Task AnEditedOocPose_IsMarked_LikeAnyOther()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(tags: ["ooc"], markup: "brb, making tea", edits: 2)));
		await Assert.That(cut.FindAll(".story-row--ooc .story-edited").Count).IsEqualTo(1);
	}

	[Test]
	public async Task Cancel_OrNoChange_SendsNothing()
	{
		var calls = 0;
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()).Add(x => x.CanEdit, true).Add(x => x.OnEdit, _ => calls++));
		await cut.Find("button.story-edit-btn").ClickAsync();
		await cut.Find("textarea.story-editor-input").InputAsync("something else");
		await cut.Find("button.story-editor-cancel").ClickAsync();
		await cut.Find("button.story-edit-btn").ClickAsync();
		await cut.Find("button.story-editor-save").ClickAsync();
		await Assert.That(calls).IsEqualTo(0);
	}

	[Test]
	public async Task SomeoneElsesPose_HasNoEdit()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()));
		await Assert.That(cut.FindAll("button.story-edit-btn").Count).IsEqualTo(0);
	}

	[Test]
	public async Task APoseEditedOnScreen_IsMarked_ForAMoment()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()));
		await Assert.That(cut.Find(".story-row").ClassList).DoesNotContain("story-row--just-edited");
		cut.Render(p => p.Add(x => x.Pose, Pose(markup: "leans on the crates, waiting.", edits: 2)));
		await Assert.That(cut.Find(".story-row").ClassList).Contains("story-row--just-edited");
		await Assert.That(cut.Find(".story-edited").TextContent.Trim()).IsEqualTo("edited");
	}

	[Test]
	public async Task WithAHandler_ThePortraitAndName_OpenTheCharacter()
	{
		string? opened = null;
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()).Add(x => x.OnCharacter, name => opened = name));
		await cut.Find("button.story-name").ClickAsync();
		await Assert.That(opened).IsEqualTo("Tomas Reyes");
		opened = null;
		await cut.Find("button.story-portrait-btn").ClickAsync();
		await Assert.That(opened).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task WithoutAHandler_TheNameLinksTheProfile()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()));
		await Assert.That(cut.Find("a.story-name").GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
	}

	[Test]
	public async Task OtherParticipants_AreMentionsInTheText()
	{
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose(author: "Ilsa Varn", markup: "sets her lantern beside Tomas."))
			.Add(x => x.Mentions, [new StoryMentions.Target("Tomas Reyes", "#ffb454", false), new StoryMentions.Target("Ilsa Varn", null, true)]));
		var mention = cut.Find(".story-body a.mention");
		await Assert.That(mention.TextContent).IsEqualTo("Tomas");
		await Assert.That(cut.FindAll(".story-body a.mention").Count).IsEqualTo(1).Because("the author is not a mention in their own pose");
	}

	/// <summary>
	/// Edit shows the pose styled, colours and all, and saves it as decompose() writes it, so a save that changed
	/// only the words keeps them.
	/// </summary>
	[Test]
	public async Task Edit_ShowsThePoseStyled_AndSavesItDecomposed()
	{
		(string PoseId, string Text)? saved = null;
		var styled = MarkupText.Concat(
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), "Tomas"),
			MarkupText.Plain(" leans on a stack of crates"));
		var pose = Pose() with { Content = styled.ToPlainText(), Markup = MarkupTextSerializer.Serialize(styled) };
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, pose).Add(x => x.CanEdit, true).Add(x => x.OnEdit, e => saved = e));

		await cut.Find("button.story-edit-btn").ClickAsync();
		await Assert.That(cut.Find("textarea.story-editor-input").GetAttribute("value")).IsEqualTo("Tomas leans on a stack of crates");
		await Assert.That(cut.Find(".story-editor .fi-overlay").InnerHtml).Contains("Tomas</span>")
			.Because("the layer over the field shows the name in its colour");

		await cut.Find("textarea.story-editor-input").InputAsync("Tomas leans on the crates.");
		await cut.Find("button.story-editor-save").ClickAsync();
		await Assert.That(saved).IsEqualTo(("P1", "[ansi(r,Tomas)]%bleans on the crates."));
	}

	/// <summary>
	/// Softcode typed into the box would be posted as text; the box says so and offers to send it as softcode,
	/// which then goes as typed.
	/// </summary>
	[Test]
	public async Task SoftcodeTypedInTheBox_IsNoticed_AndCanBeSentAsSoftcode()
	{
		(string PoseId, string Text)? saved = null;
		var cut = Render<StoryPose>(p => p.Add(x => x.Pose, Pose()).Add(x => x.CanEdit, true).Add(x => x.OnEdit, e => saved = e));
		await cut.Find("button.story-edit-btn").ClickAsync();
		await Assert.That(cut.FindAll(".fi-notice").Count).IsEqualTo(0);

		await cut.Find("textarea.story-editor-input").InputAsync("[ansi(r,Tomas)] waves.");
		await Assert.That(cut.Find(".fi-notice").TextContent).Contains("[ansi(");
		await cut.Find(".fi-notice-action").ClickAsync();

		await Assert.That(cut.FindAll(".fi-overlay").Count).IsEqualTo(0).Because("a softcode box has no styled layer");
		await cut.Find("button.story-editor-save").ClickAsync();
		await Assert.That(saved).IsEqualTo(("P1", "[ansi(r,Tomas)] waves."));
	}
}
