namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A <c>%</c> that ends the string has no substitution to start, so it is a literal <c>%</c>, as in
/// PennMUSH (<c>process_expression</c>: "Line ended in %, so treat it as literal"). The lexer read it as
/// the start of a substitution with nothing after it, and the parser answered
/// <c>#-1 PARSER FAILURE</c>, so a typed <c>say 50%</c> failed before the command ran. A <c>\</c> at the
/// end had the same failure; PennMUSH drops it when evaluating and keeps it when not.
/// </summary>
public class TrailingPercentTests : ServerTestBase
{
	[Test]
	[Arguments("50%", "50%")]
	[Arguments("%", "%")]
	[Arguments("[add(1,2)]%", "3%")]
	[Arguments("add(1,2)%", "3%")]
	// Anywhere else it still starts a substitution.
	[Arguments("50%%", "50%")]
	[Arguments("50%% x", "50% x")]
	public async Task APercentAtTheEndIsText(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	public async Task AnAttributeEndingInAPercentKeepsIt()
	{
		var attribute = TestIsolationHelpers.GenerateUniqueName("PERCENT").ToUpperInvariant();
		await Cmd($"&{attribute} me=50%");
		await Assert.That(await Eval($"get(me/{attribute})")).IsEqualTo("50%");
		await Assert.That(await Eval($"u(me/{attribute})")).IsEqualTo("50%");
	}

	[Test]
	[Arguments("a\\", "a")]
	[Arguments("\\", "")]
	[Arguments("a\\\\", "a\\")]
	public async Task ABackslashAtTheEndIsDropped(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	public async Task AnAttributeEndingInABackslashKeepsIt()
	{
		var attribute = TestIsolationHelpers.GenerateUniqueName("BACKSLASH").ToUpperInvariant();
		await Cmd($"&{attribute} me=a\\");
		await Assert.That(await Eval($"get(me/{attribute})")).IsEqualTo("a\\");
	}
}
