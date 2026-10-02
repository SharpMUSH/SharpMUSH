namespace SharpMUSH.Tests.Functions;

/// <summary>
/// ansi() reads the code strings decompose() writes: PennMUSH's define_ansi_data takes one string a character
/// at a time, so a colour can follow letters with no space and <c>/</c> or <c>!</c> marks the background.
/// </summary>
public class AnsiCodeTokensTests
{
	[Test]
	[Arguments("hr", new[] { "hr" })]
	[Arguments("fhuBr", new[] { "fhuBr" })]
	[Arguments("#ff0000", new[] { "#ff0000" })]
	[Arguments("/#0000ff", new[] { "/#0000ff" })]
	[Arguments("#ff0000!#0000ff", new[] { "#ff0000", "/#0000ff" })]
	[Arguments("#ff0000/#0000ff", new[] { "#ff0000", "/#0000ff" })]
	[Arguments("u#ff0000", new[] { "u", "#ff0000" })]
	[Arguments("+xterm200!+xterm16", new[] { "+xterm200", "/+xterm16" })]
	[Arguments("h r <255 0 0>", new[] { "h", "r", "<255 0 0>" })]
	[Arguments("/<0 0 255>", new[] { "/<0 0 255>" })]
	[Arguments("200", new[] { "200" })]
	public async Task SplitsAsPennReadsThem(string codes, string[] expected)
		=> await Assert.That(Implementation.Functions.Functions.AnsiCodeTokens(codes).ToArray()).IsEquivalentTo(expected);
}
