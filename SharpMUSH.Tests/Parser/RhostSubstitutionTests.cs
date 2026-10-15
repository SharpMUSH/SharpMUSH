namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Substitutions from RhostMUSH that were text before: <c>%i_&lt;n&gt;</c> and <c>%i_L</c> (an
/// iteration's position), <c>%il</c> (<c>%iL</c> in either case) and <c>%-&lt;n&gt;</c> (argument n).
/// Expected values were checked against a RhostMUSH server.
/// </summary>
public class RhostSubstitutionTests : ServerTestBase
{
	[Test]
	[Arguments("iter(a b c,%i_0:%i0)", "1:a 2:b 3:c")]
	[Arguments("iter(a b,iter(x y,%i_1/%i_0/%il/%i_l/%iL/%i_L))", "1/1/a/1/a/1 1/2/a/1/a/1 2/1/b/2/b/2 2/2/b/2/b/2")]
	[Arguments("iter(0 1,%i_!0:%i!0:%i_!!0:%i!l)", "0:1:1:1 0:0:1:0")]
	[Arguments("iter(a b,%i_5)", "#-1 REGISTER OUT OF RANGE #-1 REGISTER OUT OF RANGE")]
	[Arguments("%i_0", "#-1 REGISTER OUT OF RANGE")]
	[Arguments("%il", "#-1 REGISTER OUT OF RANGE")]
	[Arguments("iter(a b,[inum(0)]%i_0)", "11 22")]
	public async Task IterationPositionAndOutermost(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("ulambda(lit(#lambda/%-0|%-1|%-01|%-12|%-!!$1|%-!!$5|%-!3),a,b,c,d,e,f,g,h,i,j,k,l,m)", "a|b|b|m|1|1|1")]
	[Arguments("ulambda(lit(#lambda/%-0|%-1|%-01|%-12|%-!!$1|%-!!$5|%-!3),,x)", "|x|x||1|0|1")]
	[Arguments("ulambda(lit(#lambda/%-1x %-0123),a,b)", "bx 3")]
	public async Task DashArgument(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>
	/// What stays as it was: a <c>%-</c> without a number, and <c>%+</c> followed by text, which PennMUSH
	/// reads as the argument count (RhostMUSH's <c>%+!</c> is not added for that reason).
	/// </summary>
	[Test]
	[Arguments("ulambda(lit(#lambda/%-|%-x|%+!),a,b)", "-|-x|2!")]
	[Arguments("%i_x", "i_x")]
	public async Task OtherFormsStayAsTheyWere(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);
}
