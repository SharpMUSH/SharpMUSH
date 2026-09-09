using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using SharpMUSH.Implementation;

namespace SharpMUSH.Tests.Parser;

public class BufferedTokenSpanStreamTests
{
	private static BufferedTokenSpanStream Lex(string input)
	{
		var stream = new BufferedTokenSpanStream(new SharpMUSHLexer(new AntlrInputStream(input)));
		stream.Fill();
		return stream;
	}

	[Test]
	[Arguments("add(1,2)")]
	[Arguments("think [strcat(a,b)] {c}")]
	public async Task GetText_IntervalStopIsInclusive(string input)
	{
		var stream = Lex(input);
		var lastRealToken = stream.Size - 2;

		await Assert.That(stream.GetText(Interval.Of(0, lastRealToken))).IsEqualTo(input);
		await Assert.That(stream.GetText(stream.Get(0), stream.Get(lastRealToken))).IsEqualTo(input);
		await Assert.That(stream.GetText()).IsEqualTo(input);
	}

	[Test]
	public async Task GetText_SingleTokenInterval_IsThatToken()
	{
		var stream = Lex("add(1,2)");
		var last = stream.Size - 2;

		await Assert.That(stream.GetText(Interval.Of(last, last))).IsEqualTo(stream.Get(last).Text);
		await Assert.That(stream.GetText(Interval.Of(last, 0))).IsEqualTo(string.Empty);
	}
}
