using System.Text.RegularExpressions;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The argument forms TinyMUX gives functions both servers have: counts SharpMUSH used to refuse, and, under
/// <c>mux_arguments</c>, the meanings TinyMUX gives arguments SharpMUSH reads differently.
/// </summary>
public partial class MuxArgumentFormTests : ServerTestBase
{
	private static IDisposable MuxArguments(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxArguments = on }
		});

	[Test]
	[Arguments("before(%b%babc def)", "abc")]
	[Arguments("after(%b%babc def ghi)", "def ghi")]
	[Arguments("[before(abc def,)]|[after(abc def,)]", "abc|def")]
	[Arguments("before(abc)", "abc")]
	[Arguments("eval(lit([add(1,2)]))", "3")]
	[Arguments("splice(a b c,x y z,b,%b,-)", "a-y-c")]
	[Arguments("splice(a|b|c,x|y|z,b,|,@@)", "ayc")]
	[Arguments("table(a b c,3,8,%b,|,.)", "a..|b..\nc..")]
	[Arguments("convsecs(1000000000.5,utc,3)", "Sun Sep 09 01:46:40.5 2001")]
	[Arguments("convsecs(1000000000.25,utc,1)", "Sun Sep 09 01:46:40.2 2001")]
	[Arguments("convsecs(1000000000.04,utc,1)", "Sun Sep 09 01:46:40 2001")]
	[Arguments("convsecs(1000000000,utc,9)", "Sun Sep 09 01:46:40 2001")]
	[Arguments("[isjson(5,number)][isjson(5,string)][isjson(true,boolean)][isjson(null,null)][isjson(x,string)]", "10110")]
	[Arguments("isjson(5,frob)", ErrorMessages.Returns.UnknownJsonType)]
	[Arguments("[setq(a,1,b,2,c,3)][unsetq(a,b)][r(a)][r(b)][r(c)]", "3")]
	[Arguments("[allof(1)]|[allof(0)]|[strallof(x)]|[strallof()]|[strfirstof(y)]", "1||x||y")]
	[Arguments(@"mix(#lambda/ucstr\(\%0\),a b)", "A B")]
	[Arguments("hmac(The quick brown fox jumps over the lazy dog,key)", "F7BC83F430538424B13298E6AA6FB143EF4D59A14946175997479DBC2D1A3CD8")]
	[Arguments("hmac(sha1,key,The quick brown fox jumps over the lazy dog)", "de7c9b85b8b78aa6bc8a7a36f70a90701c9db4d9")]
	[Arguments("setunion(a|b,b|c,|,-)", "a|b|c")]
	[Arguments("hmac(sha1,key,text,MUX)", "369e2959eb49450338b212748f77d8ded74847bb")]
	[Arguments("textfile(help,x,y)", "#-1 FUNCTION (TEXTFILE) EXPECTS 2 ARGUMENTS BUT GOT 3")]
	public async Task TinyMuxArgumentCounts(string expression, string expected)
	{
		using var option = MuxArguments(false);
		await Assert.That(await Eval(expression)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("setunion(a|b,b|c,|,-)", "a-b-c")]
	[Arguments("setdiff(c|a|b,b,|,-)", "a-c")]
	[Arguments("setinter(a|b|c,c|b,|,@@)", "bc")]
	[Arguments("setunion(10|9,1,|,%b,n)", "1 9 10")]
	[Arguments("hmac(The quick brown fox jumps over the lazy dog,key,sha1)", "DE7C9B85B8B78AA6BC8A7A36F70A90701C9DB4D9")]
	[Arguments("hmac(text,key,frob)", ErrorMessages.Returns.UnsupportedDigestType)]
	[Arguments("hmac(sha1,key,text,base64)", "Np4pWetJRQM4shJ0j3fY3tdIR7s=")]
	public async Task TinyMuxArgumentMeanings(string expression, string expected)
	{
		using var option = MuxArguments(true);
		await Assert.That(await Eval(expression)).IsEqualTo(expected);
	}

	[Test]
	public async Task TimeTakesDecimalPlaces()
	{
		var result = await Eval("time(utc,3)");
		await Assert.That(TimeString().IsMatch(result)).IsTrue().Because(result);
		await Assert.That(TimeString().IsMatch(await Eval("time(local,0)"))).IsTrue();
		await Assert.That(TimeString().IsMatch(await Eval("time(local)"))).IsTrue();
	}

	[Test]
	public async Task TerminfoReadsItsArgumentsAsOneName()
		=> await Assert.That(await Eval("terminfo(nobody,here)")).IsEqualTo(ErrorMessages.Returns.NotConnected);

	[Test]
	public async Task MailsendTakesASubject()
	{
		var subject = TestIsolationHelpers.GenerateUniqueName("MuxMailSubject");
		await Assert.That(await Eval($"mailsend(me,{subject},Body)")).IsEqualTo("1");
		await Assert.That(await Eval($"mailsend({TestIsolationHelpers.GenerateUniqueName("Nobody")},{subject},Body)"))
			.IsEqualTo(ErrorMessages.Returns.NoSuchPlayer);
	}

	[Test]
	public async Task RegnattrTakesTheRegexpApart()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("RegnattrMux")})");
		await Eval($"[set({thing},MUXRE_ONE:1)][set({thing},MUXRE_TWO:2)][set({thing},OTHER:3)]");

		await Assert.That(await Eval($"regnattr({thing},^MUXRE_)")).IsEqualTo("2");
		await Assert.That(await Eval($"regnattr({thing}/^MUXRE_)")).IsEqualTo("2");
	}

	[Test]
	public async Task ConnlogTakesAPlayer()
	{
		var result = await Eval("connlog(me)");
		await Assert.That(result).DoesNotStartWith("#-1 FUNCTION");
		await Assert.That(await Eval($"connlog({TestIsolationHelpers.GenerateUniqueName("Nobody")},5)"))
			.IsEqualTo(ErrorMessages.Returns.PlayerNotFound);
	}

	[Test]
	public async Task LwhoOneListsDescriptorsUnderMuxArguments()
	{
		using var option = MuxArguments(true);
		var ports = await Eval("lwho(1)");
		await Assert.That(ports.Split(' ', StringSplitOptions.RemoveEmptyEntries)).IsNotEmpty();
		foreach (var entry in ports.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			await Assert.That(PortEntry().IsMatch(entry)).IsTrue().Because(entry);
		}

		foreach (var entry in (await Eval("lwho(0)")).Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			await Assert.That(PlayerEntry().IsMatch(entry)).IsTrue().Because(entry);
		}
	}

	[Test]
	public async Task ChannelsTakesTinyMuxArgumentsOnlyUnderMuxArguments()
	{
		await Assert.That(await Eval("channels(all,|,0,1)"))
			.IsEqualTo(ErrorMessages.Returns.ArgumentCount("CHANNELS", 0, 2, 4));

		using var option = MuxArguments(true);
		var all = await Eval("channels(all,|)");
		await Assert.That(all).IsEqualTo(await Eval("channels(,|)"));
		await Assert.That(await Eval("channels(all,|,0,1)")).IsEqualTo(all.Split('|')[0]);
		await Assert.That(await Eval("channels(all,|,1)")).IsEqualTo(string.Join('|', all.Split('|').Skip(1)));
		await Assert.That(await Eval($"channels({TestIsolationHelpers.GenerateUniqueName("Nobody")})"))
			.IsEqualTo(ErrorMessages.Returns.PlayerNotFound);
	}

	[Test]
	public async Task DynhelpReadsAnObjectsHelpUnderMuxArguments()
	{
		var thing = await Eval($"create({TestIsolationHelpers.GenerateUniqueName("DynhelpMux")})");
		await Eval($"[set({thing},HELP_FOOBAR:Foo text)][set({thing},HELP_FOOD:Food text)][set({thing},TOPIC_X:Ex text)]");

		await Assert.That(await Eval($"dynhelp({thing},foobar)")).IsEqualTo(ErrorMessages.Returns.NoSuchFile);

		using var option = MuxArguments(true);
		await Assert.That(await Eval($"dynhelp({thing},foobar)")).IsEqualTo("Foo text");
		await Assert.That(await Eval($"dynhelp({thing},fooba)")).IsEqualTo("Foo text");
		await Assert.That(await Eval($"dynhelp({thing},foo)")).IsEqualTo(ErrorMessages.Returns.AmbiguousTopic);
		await Assert.That(await Eval($"dynhelp({thing},zzz)")).IsEqualTo(ErrorMessages.Returns.NoSuchTopic);
		await Assert.That(await Eval($"dynhelp({thing},foo?ar)")).IsEqualTo(ErrorMessages.Returns.NoSuchTopic);
		await Assert.That(await Eval($"textfile({thing},x,TOPIC_)")).IsEqualTo("Ex text");
	}

	[GeneratedRegex(@"\A\w{3} \w{3} \d\d \d\d:\d\d:\d\d(\.\d{1,3})? \d{4}\z")]
	private static partial Regex TimeString();

	[GeneratedRegex(@"\A#\d+:\d+\z")]
	private static partial Regex PortEntry();

	[GeneratedRegex(@"\A#\d+\z")]
	private static partial Regex PlayerEntry();
}
