using OneOf;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

public static class HelperFunctions
{
	public static bool IsPlayer(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Match(player => true, room => false, exit => false, thing => false);

	public static bool IsRoom(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Match(player => false, room => true, exit => false, thing => false);

	public static bool IsExit(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Match(player => false, room => false, exit => true, thing => false);

	public static bool IsThing(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Match(player => false, room => false, exit => false, thing => true);

	public static bool IsWizard(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Object()!.Flags!.Any(x => x.Name == "Wizard");

	public static bool IsRoyalty(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Object()!.Flags!.Any(x => x.Name == "Royalty");

	public static bool IsMistrust(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Object()!.Flags!.Any(x => x.Name == "Mistrust");

	public static bool IsGod(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> obj.Object()!.Key! == 1;

	public static bool IsPriv(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> IsGod(obj) || IsWizard(obj) || IsRoyalty(obj);

	public static bool IsSee_All(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> IsPriv(obj) || obj.HasPower("See_All");

	public static bool HasPower(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj, string power)
		=> obj.Object().Powers!.Any(x => x.Name == power || x.Alias == power);

	public static bool HasFlag(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj, string flag)
		=> obj.Object().Flags!.Any(x => x.Name == flag);

	public static bool Inheritable(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj)
		=> IsPlayer(obj)
				|| obj.HasFlag("Trust")
				|| obj.Object().Owner!.Single().Object.Flags!.Any(x => x.Name == "Trust")
				|| IsWizard(obj);

	public static bool Owns(this OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> who,
															 OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> what)
		=> who.Object()!.Owner!.Single().Object.Id == what.Object()!.Owner!.Single().Object.Id;
}
