using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

/// <summary>TinyMUX's <c>@hook/&lt;type&gt; &lt;command&gt;</c> written as SharpMUSH's, with the hook object #12.</summary>
public class HookCommandRewriterTests
{
	[Test]
	[Arguments("@hook/before look", "@hook/before look=#12,B_LOOK")]
	[Arguments("@hook/before/after look", "@hook/before look=#12,B_LOOK;@hook/after look=#12,A_LOOK")]
	[Arguments("@hook/permit @pemit", "@hook/permit @pemit=#12,P_@PEMIT")]
	[Arguments("@hook/fail look;@hook/args look", "@hook/fail look=#12,AF_LOOK;@hook/args look=#12,R_LOOK")]
	[Arguments("@hook/ignore \"", "@hook/ignore say=#12,I_SAY")]
	[Arguments("@hook/igswitch look", "@hook/extend look=*")]
	[Arguments("@hook/bef look", "@hook/before look=#12,B_LOOK")]
	[Arguments("@hook/f look", "@hook/fail look=#12,AF_LOOK")]
	[Arguments("@hook/permit/clear look", "@hook/permit look")]
	[Arguments("@hook", "@hook/list")]
	[Arguments("@hook look", "@hook/list look")]
	[Arguments("$+hookon:@hook/before look", "$+hookon:@hook/before look=#12,B_LOOK")]
	[Arguments("@switch %0=1,@hook/before/after look,@pemit me=no", "@switch %0=1,{@hook/before look=#12,B_LOOK;@hook/after look=#12,A_LOOK},@pemit me=no")]
	[Arguments("{@hook/after look}", "{@hook/after look=#12,A_LOOK}")]
	public async Task RewritesTheTinyMuxForm(string softcode, string expected)
	{
		var result = HookCommandRewriter.Rewrite(softcode, "#12");

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(result.Changed).IsTrue();
	}

	[Test]
	[Arguments("@hook/before look=#5,B_LOOK")]
	[Arguments("Usage: @hook/before <cmd>")]
	[Arguments("@hook/before <cmd>")]
	[Arguments("think @hook/before look")]
	public async Task LeavesOtherTextAlone(string softcode)
	{
		var result = HookCommandRewriter.Rewrite(softcode, "#12");

		await Assert.That(result.Text).IsEqualTo(softcode);
		await Assert.That(result.Notes).IsEmpty();
	}

	[Test]
	[Arguments("@hook/clear look")]
	[Arguments("@hook/bogus look")]
	public async Task NotesWhatItCannotWrite(string softcode)
	{
		var result = HookCommandRewriter.Rewrite(softcode, "#12");

		await Assert.That(result.Text).IsEqualTo(softcode);
		await Assert.That(result.Notes).Contains(note => note.StartsWith("check by hand"));
	}

	[Test]
	public async Task WithoutAHookObjectTheLineIsNoted()
	{
		var result = HookCommandRewriter.Rewrite("@hook/before look", null);

		await Assert.That(result.Text).IsEqualTo("@hook/before look");
		await Assert.That(result.Notes).Contains(note => note.Contains("hook_obj"));
	}
}
