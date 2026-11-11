using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

/// <summary>TinyMUX's and RhostMUSH's <c>@femit</c>, <c>@fpose</c> and <c>@fsay</c> written as SharpMUSH's <c>@force</c>.</summary>
public class ForcedSpeechRewriterTests
{
	[Test]
	[Arguments("@femit Lackey=sweeps [name(me)].", "@force Lackey=@emit/noeval sweeps [name(me)].")]
	[Arguments("@fe/here #12=hi", "@force #12=@emit/noeval hi")]
	[Arguments("@femit/room *Bob=hi", "@force *Bob=@lemit/noeval hi")]
	[Arguments("@fpose Lackey=bows.", "@force Lackey=pose/noeval bows.")]
	[Arguments("@fpose/nospace Lackey='s tired.", "@force Lackey=semipose/noeval 's tired.")]
	[Arguments("@fsay Lackey = Hello, all.", "@force Lackey=say/noeval Hello, all.")]
	[Arguments("$bow *:@fpose %0=bows.;@fsay %0=Done", "$bow *:@force %0=pose/noeval bows.;@force %0=say/noeval Done")]
	[Arguments("@switch %0=1,@fsay me=one,@fsay me=other", "@switch %0=1,@force me=say/noeval one,@force me=say/noeval other")]
	[Arguments("{@femit [v(puppet)]=hi}", "{@force [v(puppet)]=@emit/noeval hi}")]
	public async Task WritesItAsForce(string softcode, string expected)
	{
		var result = ForcedSpeechRewriter.Rewrite(softcode);

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(result.Changed).IsTrue();
	}

	[Test]
	[Arguments("@feed/subscribe news")]
	[Arguments("think @femit me=hi")]
	[Arguments("@force me=@emit hi")]
	[Arguments("@switch %0=@fsay *,@pemit %#=matched")]
	[Arguments("think [switch(%0,@fe x,a,b)]")]
	[Arguments("@fsay <object>=<message> makes the object say it.")]
	public async Task LeavesOtherTextAlone(string softcode)
	{
		var result = ForcedSpeechRewriter.Rewrite(softcode);

		await Assert.That(result.Text).IsEqualTo(softcode);
		await Assert.That(result.Notes).IsEmpty();
	}

	[Test]
	public async Task NotesASwitchItCannotCarry()
	{
		var result = ForcedSpeechRewriter.Rewrite("@fsay/bogus me=hi");

		await Assert.That(result.Text).IsEqualTo("@fsay/bogus me=hi");
		await Assert.That(result.Notes).Contains(note => note.StartsWith("check by hand"));
	}
}
