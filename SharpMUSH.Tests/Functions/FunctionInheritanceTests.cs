using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Functions that PennMUSH answers through <c>atr_get</c> (<c>attrib.c:1184</c>) see an attribute the
/// object only inherits from its @parent chain or type ancestor: <c>do_get_attrib</c> (<c>v()</c>),
/// <c>parse_attrib</c> (<c>default()</c>, <c>edefault()</c>, <c>hasflag()</c>, <c>owner()</c>),
/// <c>fetch_ufun_attrib</c> (<c>udefault()</c>) and <c>parse_timezone_arg</c> (<c>time()</c>).
/// </summary>
public class FunctionInheritanceTests : ServerTestBase
{
	private async Task<(DBRef Parent, DBRef Child)> ParentAndChild(string prefix)
	{
		var parent = DBRef.Parse(TrailingDbref(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName(prefix + "P")}")));
		var child = DBRef.Parse(TrailingDbref(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName(prefix + "C")}")));
		await Cmd($"@parent #{child.Number}=#{parent.Number}");
		return (parent, child);
	}

	[Test]
	public async Task AttributeReadingFunctionsSeeAParentsAttribute()
	{
		var (parent, child) = await ParentAndChild("Inh");
		var c = $"#{child.Number}";
		await Cmd($"&FOO #{parent.Number}=from parent");
		await Cmd($"@set #{parent.Number}/FOO=visual");
		await Cmd($"&TWICE #{parent.Number}=[mul(%0,2)]");
		await Cmd($"&TWO #{parent.Number}=[add(1,1)]");

		await Assert.That(await EvalAs(child, "v(FOO)")).IsEqualTo("from parent");
		await Assert.That(await Eval($"default({c}/FOO,none)")).IsEqualTo("from parent");
		await Assert.That(await Eval($"edefault({c}/TWO,none)")).IsEqualTo("2");
		await Assert.That(await Eval($"udefault({c}/TWICE,none,4)")).IsEqualTo("8");
		await Assert.That(await Eval($"hasflag({c}/FOO,visual)")).IsEqualTo("1");
		await Assert.That(await Eval($"owner({c}/FOO)")).IsEqualTo("#1");
		await Assert.That(await Eval($"visible(#1,{c}/FOO)")).IsEqualTo("1");
	}

	/// <summary><c>fun_hasflag</c> and <c>fun_owner</c> (<c>fundb.c:1031</c>, <c>fundb.c:1719</c>) answer a
	/// missing attribute with a bare <c>#-1</c>.</summary>
	[Test]
	public async Task MissingAttributeIsMinusOne()
	{
		var (_, child) = await ParentAndChild("Miss");
		await Assert.That(await Eval($"hasflag(#{child.Number}/NOPE,visual)")).IsEqualTo("#-1");
		await Assert.That(await Eval($"owner(#{child.Number}/NOPE)")).IsEqualTo("#-1");
	}

	/// <summary><c>can_edit_attr</c> (<c>attrib.c:414-421</c>) judges an attribute the object does not hold
	/// as one about to be created.</summary>
	[Test]
	public async Task ControlsOnAMissingAttributeAsksWhetherItCouldBeCreated()
	{
		var (_, child) = await ParentAndChild("Ctl");
		await Assert.That(await Eval($"controls(me,#{child.Number}/NOT_THERE_YET)")).IsEqualTo("1");
	}

	/// <summary>
	/// <c>fun_pfun</c> (<c>funufun.c:244-292</c>): the code runs as the caller, not the parent, and a
	/// no_inherit attribute is refused even though it sits on the parent itself.
	/// </summary>
	[Test]
	public async Task PFunRunsAsTheCallerAndRefusesNoInherit()
	{
		var (parent, child) = await ParentAndChild("Pfun");
		await Cmd($"&WHO #{parent.Number}=%!");
		await Cmd($"&SECRET #{parent.Number}=hidden");
		await Cmd($"@set #{parent.Number}/SECRET=no_inherit");

		await Assert.That(await EvalAs(child, "pfun(WHO)")).IsEqualTo($"#{child.Number}");
		await Assert.That(await EvalAs(child, "pfun(SECRET)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(parent, "pfun(WHO)")).IsEqualTo(string.Empty);
	}

	/// <summary><c>parse_timezone_arg</c> (<c>tz.c:463</c>) reads TZ through <c>atr_get</c>.</summary>
	[Test]
	public async Task TimeUsesAnInheritedTimeZone()
	{
		var (parent, child) = await ParentAndChild("Tz");
		var plain = DBRef.Parse(TrailingDbref(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("TzPlain")}")));
		// UTC+14, so its hour differs from the server's in every zone a test host runs in.
		await Cmd($"&TZ #{parent.Number}=Pacific/Kiritimati");

		var hours = await Eval($"[extract(time(#{parent.Number}),4,1)] [extract(time(#{child.Number}),4,1)] [extract(time(#{plain.Number}),4,1)]");
		var (parentHour, childHour, plainHour) = (hours.Split(' ')[0][..2], hours.Split(' ')[1][..2], hours.Split(' ')[2][..2]);

		await Assert.That(childHour).IsEqualTo(parentHour);
		await Assert.That(childHour).IsNotEqualTo(plainHour);
	}

	private static string TrailingDbref(string created) => created.Trim().Split(' ')[^1].Trim().TrimEnd('.');
}
