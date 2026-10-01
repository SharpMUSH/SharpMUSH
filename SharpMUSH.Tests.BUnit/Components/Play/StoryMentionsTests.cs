using SharpMUSH.Client.Components.Scenes;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.4 / §4.8: in a pose, the other participants' names are mentions. The pose body is
/// rendered markup (HTML), so names are marked in its text only, never inside a tag or an existing link.
/// </summary>
public class StoryMentionsTests
{
	private static readonly StoryMentions.Target Tomas = new("Tomas Reyes", "#ffb454", false);
	private static readonly StoryMentions.Target Ilsa = new("Ilsa Varn", null, true);

	[Test]
	public async Task AFullName_AndAFirstName_BecomeMentionLinks()
	{
		var html = StoryMentions.Mark("Tomas Reyes nods. Later, Tomas leaves.", [Tomas]);
		await Assert.That(html).IsEqualTo(
			"""<a class="mention" href="/character/Tomas%20Reyes" data-name="Tomas Reyes" style="--name:#ffb454">Tomas Reyes</a> nods. Later, <a class="mention" href="/character/Tomas%20Reyes" data-name="Tomas Reyes" style="--name:#ffb454">Tomas</a> leaves.""");
	}

	[Test]
	public async Task TheViewersOwnName_UnderlinesInTheAccent()
	{
		var html = StoryMentions.Mark("beside Ilsa", [Ilsa]);
		await Assert.That(html).Contains("""style="--name:var(--accent)">Ilsa</a>""");
	}

	[Test]
	public async Task TagsAttributes_AndExistingLinks_AreLeftAlone()
	{
		const string source = """<span title="Tomas" style="color:#fff">x</span> <a href="/w">Tomas</a>""";
		await Assert.That(StoryMentions.Mark(source, [Tomas])).IsEqualTo(source);
	}

	[Test]
	public async Task OnlyWholeWords_AndCaseMatters()
	{
		const string source = "Tomasina and tomas and Tomas's";
		var html = StoryMentions.Mark(source, [Tomas]);
		await Assert.That(html).StartsWith("Tomasina and tomas and <a ");
		await Assert.That(html).EndsWith(">Tomas</a>'s");
	}

	[Test]
	public async Task AFirstNameSharedByTwoParticipants_IsNotGuessed()
	{
		var other = new StoryMentions.Target("Tomas Oake", null, false);
		var html = StoryMentions.Mark("Tomas waves at Tomas Oake.", [Tomas, other]);
		await Assert.That(html).StartsWith("Tomas waves at <a ");
		await Assert.That(html).Contains("""data-name="Tomas Oake">Tomas Oake</a>""");
	}

	[Test]
	public async Task EncodedText_AndUnsafeColours_AreHandled()
	{
		var odd = new StoryMentions.Target("Ann <&> Bee", "red;background:url(x)", false);
		var html = StoryMentions.Mark("hi Ann &lt;&amp;&gt; Bee", [odd]);
		await Assert.That(html).Contains("""href="/character/Ann%20%3C%26%3E%20Bee" data-name="Ann &lt;&amp;&gt; Bee">Ann &lt;&amp;&gt; Bee</a>""")
			.Because("only #rrggbb colours pass; anything else is dropped rather than written into a style");
	}

	[Test]
	public async Task ANameInQuotes_IsStillFound()
	{
		// The renderer encodes quotes, so dialogue arrives as &quot;Tomas,&quot; she says.
		var html = StoryMentions.Mark("&quot;Tomas,&quot; she says.", [Tomas]);
		await Assert.That(html).StartsWith("&quot;<a class=\"mention\"");
	}

	[Test]
	public async Task NoTargets_ChangeNothing()
		=> await Assert.That(StoryMentions.Mark("<b>Tomas</b>", [])).IsEqualTo("<b>Tomas</b>");
}
