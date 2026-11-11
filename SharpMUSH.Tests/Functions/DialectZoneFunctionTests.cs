using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>TinyMUX's and RhostMUSH's zone functions, over objects in one zone or several.</summary>
public class DialectZoneFunctionTests : ServerTestBase
{
	private IZoneListStore Zones => WebAppFactoryArg.Services.GetRequiredService<IZoneListStore>();

	private async Task<DBRef> CreateAsync(string prefix)
		=> DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName(prefix)})"));

	[Test]
	public async Task MembersByType()
	{
		var zone = await CreateAsync("ZfZone");
		var thing = await CreateAsync("ZfThing");
		var room = DBRef.Parse(await Eval($"dig({TestIsolationHelpers.GenerateUniqueName("ZfRoom")})"));
		var other = await CreateAsync("ZfOther");
		await Zones.SetZonesAsync(thing, [other, zone]);
		await Zones.SetZonesAsync(room, [zone]);

		await Assert.That(await Eval($"zchildren({zone})")).IsEqualTo($"#{thing.Number} #{room.Number}");
		await Assert.That(await Eval($"zthings({zone})")).IsEqualTo($"#{thing.Number}");
		await Assert.That(await Eval($"zrooms({zone})")).IsEqualTo($"#{room.Number}");
		await Assert.That(await Eval($"inzone({zone})")).IsEqualTo($"#{room.Number}");
		await Assert.That(await Eval($"zexits({zone})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"inzone({zone},{thing})")).IsEqualTo("1");
		await Assert.That(await Eval($"inzone({other},{room})")).IsEqualTo("0");
	}

	[Test]
	public async Task LzoneListsMembersOrZones()
	{
		var zone = await CreateAsync("LzZone");
		var second = await CreateAsync("LzSecond");
		var a = await CreateAsync("LzA");
		var b = await CreateAsync("LzB");
		await Zones.SetZonesAsync(a, [zone, second]);
		await Zones.SetZonesAsync(b, [zone]);

		await Assert.That(await Eval($"lzone({zone})")).IsEqualTo($"#{a.Number} #{b.Number}");
		await Assert.That(await Eval($"lzone({a})")).IsEqualTo($"#{zone.Number} #{second.Number}");
		await Assert.That(await Eval($"lzone({zone},l)")).IsEqualTo("1 2");
		await Assert.That(await Eval($"lzone({zone},~2-1)")).IsEqualTo($"#{b.Number}");
		await Assert.That(await Eval($"lzone({a},,1)")).StartsWith($"#{zone.Number}:");
	}

	[Test]
	public async Task ZlconJoinsTheContentsOfEveryMember()
	{
		var zone = await CreateAsync("ZlZone");
		var box = await CreateAsync("ZlBox");
		var inside = await CreateAsync("ZlInside");
		await Eval($"tel({inside},{box})");
		await Zones.SetZonesAsync(box, [zone]);

		await Assert.That(await Eval($"zlcon({zone})")).IsEqualTo($"#{inside.Number}");
	}

	[Test]
	public async Task ZsearchKeepsTheZonesObjects()
	{
		var zone = await CreateAsync("ZsZone");
		var name = TestIsolationHelpers.GenerateUniqueName("ZsMember");
		var member = DBRef.Parse(await Eval($"create({name})"));
		await CreateAsync(name[..^2]);
		await Zones.SetZonesAsync(member, [zone]);

		await Assert.That(await Eval($"zsearch({zone} name={name})")).IsEqualTo($"#{member.Number}");
		await Assert.That(await Eval($"zsearch({zone})")).IsEqualTo($"#{member.Number}");
		await Assert.That(await Eval($"zsearchobjid({zone} name={name})")).StartsWith($"#{member.Number}:");
		await Assert.That(await Eval($"searchobjid(all,name,{name})")).StartsWith($"#{member.Number}:");
		await Assert.That(await Eval($"searchng(all,name,{name})")).IsEqualTo($"#{member.Number}");
	}

	[Test]
	public async Task ZonecmdChangesTheList()
	{
		var obj = await CreateAsync("ZcObj");
		var a = await CreateAsync("ZcA");
		var b = await CreateAsync("ZcB");
		var c = await CreateAsync("ZcC");

		await Assert.That(await Eval($"zonecmd(add,{obj},{a})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"zonecmd(add,{obj},{b})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"zonecmd(list,{obj})")).IsEqualTo($"#{a.Number} #{b.Number}");
		await Assert.That(await Eval($"zonecmd(replace,{obj},{a}/{c})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"lzone({obj})")).IsEqualTo($"#{b.Number} #{c.Number}");
		await Assert.That(await Eval($"zonecmd(delete,{obj},{b})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"lzone({obj})")).IsEqualTo($"#{c.Number}");
		await Assert.That(await Eval($"zonecmd(delete,{obj},{b})")).StartsWith("#-1");
		await Assert.That(await Eval($"zonecmd(purge,{obj})")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"lzone({obj})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ZsearchTakesADbrefRangeWithoutAClass()
	{
		var zone = await CreateAsync("ZrZone");
		var low = await CreateAsync("ZrLow");
		var high = await CreateAsync("ZrHigh");
		await Zones.SetZonesAsync(low, [zone]);
		await Zones.SetZonesAsync(high, [zone]);

		await Assert.That(await Eval($"zsearch({zone},{high.Number},{high.Number})")).IsEqualTo($"#{high.Number}");
	}

	[Test]
	public async Task ZonecmdListGivesTheZonesOfAZoneWithMembers()
	{
		var zone = await CreateAsync("ZlsZone");
		var member = await CreateAsync("ZlsMember");
		var outer = await CreateAsync("ZlsOuter");
		await Zones.SetZonesAsync(member, [zone]);
		await Zones.SetZonesAsync(zone, [outer]);

		await Assert.That(await Eval($"zonecmd(list,{zone})")).IsEqualTo($"#{outer.Number}");
		await Assert.That(await Eval($"lzone({zone})")).IsEqualTo($"#{member.Number}");
	}

	[Test]
	public async Task AMortalMayNotListAZoneTheyDoNotControl()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "ZpMortal");
		var zone = await CreateAsync("ZpZone");
		var tool = await CreateAsync("ZpTool");
		await Zones.SetZonesAsync(await CreateAsync("ZpMember"), [zone]);
		await Cmd($"&LIST {tool}=[zchildren({zone})]|[zonecmd(add,{zone},{tool})]");
		await Cmd($"@chown {tool}={mortal}");
		await Cmd($"@set {tool}=!HALT");

		await Assert.That(await Eval($"u({tool}/LIST)")).IsEqualTo("#-1 PERMISSION DENIED|#-1 PERMISSION DENIED");
	}

	[Test]
	public async Task ZfunFamilyRunsAZonesAttribute()
	{
		var caller = await CreateAsync("ZuCaller");
		var a = await CreateAsync("ZuA");
		var b = await CreateAsync("ZuB");
		var stranger = await CreateAsync("ZuStranger");
		await Zones.SetZonesAsync(caller, [a, b]);
		await Cmd($"&FN {a}=a:%0:[num(me)]");
		await Cmd($"&FN {b}=b:%0:[num(me)]");
		await Cmd($"&FN {stranger}=s");
		await Cmd($"&CALL {caller}=[zfun({a}/fn,1)] [zfun({b}/fn,2)] [zfun2(fn,3)] [zfunlocal({b}/fn,4)] [zfundefault(none,dflt)] [zfun2default({b}/fn,x,5)] [zfun({stranger}/fn)]");

		await Assert.That(await Eval($"u({caller}/CALL)")).IsEqualTo(
			$"a:1:#{a.Number} b:2:#{b.Number} a:3:#{caller.Number} b:4:#{b.Number} dflt b:5:#{caller.Number} #-1 INVALID ZONE");
	}
}
