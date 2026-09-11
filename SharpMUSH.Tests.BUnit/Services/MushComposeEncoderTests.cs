using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// What the live-scene compose box puts on the wire.
///
/// <para>A pose typed in the portal is delivered as the right-hand side of one command line, and the
/// game's parser compresses runs of spaces, eats the ones at an argument's edges, ends the line at a
/// newline and starts a new command at a semicolon. Everything the author typed that those four rules
/// would consume has to travel as a substitution instead — which is what these fix in place, because
/// the loss is silent: the pose arrives, shorter, and nothing reports that anything went missing.</para>
/// </summary>
public class MushComposeEncoderTests
{
	[Test]
	public async Task LeadingIndentSurvivesAsExplicitSpaces()
	{
		await Assert.That(MushComposeEncoder.Encode("    She waits."))
			.IsEqualTo("%b%b%b%bShe waits.")
			.Because("spaces opening an argument are eaten outright; %b is the only spelling that arrives");
	}

	[Test]
	public async Task ASingleSpaceBetweenWordsIsLeftAlone()
	{
		await Assert.That(MushComposeEncoder.Encode("She waits by the door."))
			.IsEqualTo("She waits by the door.")
			.Because("a lone interior space survives on its own, and spelling it out only bloats the line");
	}

	[Test]
	public async Task ARunOfInteriorSpacesIsSpelledOut()
	{
		await Assert.That(MushComposeEncoder.Encode("Name       Role"))
			.IsEqualTo("Name%b%b%b%b%b%b%bRole")
			.Because("the parser compresses a run to one space, which collapses a hand-aligned column");
	}

	[Test]
	public async Task TrailingSpacesSurvive()
	{
		await Assert.That(MushComposeEncoder.Encode("...and then  "))
			.IsEqualTo("...and then%b%b");
	}

	[Test]
	public async Task NewlinesTravelAsSubstitutionsAndKeepTheirIndent()
	{
		await Assert.That(MushComposeEncoder.Encode("He speaks.\n\n    \"Well?\""))
			.IsEqualTo("He speaks.%r%r%b%b%b%b\"Well?\"")
			.Because("a raw newline would end the command, and the indent after one is at a line edge");
	}

	[Test]
	[Arguments("a\r\nb")]
	[Arguments("a\rb")]
	[Arguments("a\nb")]
	public async Task EveryLineEndingSpellingBecomesOneBreak(string input)
	{
		await Assert.That(MushComposeEncoder.Encode(input)).IsEqualTo("a%rb");
	}

	[Test]
	public async Task ASemicolonDoesNotCutThePoseInTwo()
	{
		await Assert.That(MushComposeEncoder.Encode("She paused; then spoke."))
			.IsEqualTo("She paused%; then spoke.")
			.Because("a bare ';' separates commands: the pose stopped there and the rest ran as one");
	}

	[Test]
	public async Task TabsTravelAsTabs()
	{
		await Assert.That(MushComposeEncoder.Encode("a\tb")).IsEqualTo("a%tb");
	}

	[Test]
	public async Task EvaluationCharactersAreLeftForTheGameToDecide()
	{
		// Deliberately untouched: escaping these would also disable the markup a player may want in a
		// pose. Whether web poses may carry MUSH code is the game's policy, not the compose box's.
		await Assert.That(MushComposeEncoder.Encode("[ansi(r,red)]")).IsEqualTo("[ansi(r,red)]");
	}

	[Test]
	public async Task EmptyInputEncodesToNothing()
	{
		await Assert.That(MushComposeEncoder.Encode(string.Empty)).IsEqualTo(string.Empty);
	}
}
