using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Tests.Functions;

public class BooleanCompatibilityTests
{
	[Test]
	[Arguments("0x0", false)]
	[Arguments("-0x0.0p100", false)]
	[Arguments("0x1", true)]
	[Arguments("0x", true)]
	[Arguments("0e-9999", false)]
	[Arguments("1e-9999", true)]
	[Arguments("0 ", true)]
	[Arguments(" 0", false)]
	[Arguments("\t", true)]
	[Arguments(" ", false)]
	public async Task PennBooleanBoundary(string input, bool expected)
		=> await Assert.That(Predicates.Truthy(MarkupText.Plain(input))).IsEqualTo(expected);

	[Test]
	[Arguments("text", false)]
	[Arguments("0.1", false)]
	[Arguments("1.2foo", true)]
	[Arguments("-1text", true)]
	[Arguments("4294967296", false)]
	[Arguments("0x1", false)]
	[Arguments("9223372036854775807", true)]
	[Arguments("-9223372036854775808", false)]
	[Arguments("18446744073709551616", true)]
	public async Task TinyBooleanUsesLeadingInteger(string input, bool expected)
		=> await Assert.That(Predicates.Truthy(MarkupText.Plain(input), tinyBooleans: true)).IsEqualTo(expected);
}
