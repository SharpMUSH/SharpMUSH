using OneOf;
using OneOf.Monads;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library;

public static partial class HelperFunctions
{
	private static readonly Regex DatabaseReferenceRegex = DatabaseReference();
	private static readonly Regex DatabaseReferenceWithAttributeRegex = DatabaseReferenceWithAttribute();
	private static readonly Regex OptionalDatabaseReferenceWithAttributeRegex = OptionalDatabaseReferenceWithAttribute();

	public static bool IsWizard(this AnySharpObject obj)
		=> obj.Object()!.Flags().Any(x => x.Name == "Wizard");

	public static bool IsRoyalty(this AnySharpObject obj)
		=> obj.Object()!.Flags().Any(x => x.Name == "Royalty");

	public static bool IsMistrust(this AnySharpObject obj)
		=> obj.Object()!.Flags().Any(x => x.Name == "Mistrust");

	public static bool IsGod(this AnySharpObject obj)
		=> obj.Object()!.Key! == 1;

	public static bool IsPriv(this AnySharpObject obj)
		=> IsGod(obj) || IsWizard(obj) || IsRoyalty(obj);

	public static bool IsSee_All(this AnySharpObject obj)
		=> IsPriv(obj) || obj.HasPower("See_All");
	
	public static bool IsGuest(this AnySharpObject obj)
		=> obj.HasPower("Guest");

	public static bool IsVisual(this AnySharpObject obj)
		=> obj.HasPower("Visual");

	public static bool IsDark(this AnySharpObject obj)
		=> obj.HasPower("Dark");

	public static bool IsLight(this AnySharpObject obj)
		=> obj.HasPower("Light");
	
	public static bool IsDarkLegal(this AnySharpObject obj)
		=> obj.IsDark() && (obj.CanDark() || !obj.IsAlive());

	public static bool IsAudible(this AnySharpObject obj)
		=> obj.HasPower("Audible");
	public static bool IsOrphan(this AnySharpObject obj)
		=> obj.HasPower("Orphan");

	public static bool IsAlive(this AnySharpObject obj)
		=> obj.IsPlayer || IsPuppet(obj) || (IsAudible(obj) && obj.Object().Attributes().Any(x => x.Name == "FORWARDLIST"));

	public static bool IsPuppet(this AnySharpObject obj)
		=> obj.HasPower("Puppet");

	public static bool HasPower(this AnySharpObject obj, string power)
		=> obj.Object().Powers().Any(x => x.Name == power || x.Alias == power);

	public static bool HasLongFingers(this AnySharpObject obj)
		=> obj.IsPriv() || obj.HasPower("Long_Fingers");

	public static bool HasFlag(this AnySharpObject obj, string flag)
		=> obj.Object().Flags().Any(x => x.Name == flag);

	// This may belong in the Permission Service.
	public static bool CanDark(this AnySharpObject obj)
		=> obj.HasPower("Can_Dark") || obj.IsWizard();

	public static DBRef? Ancestor(this AnySharpObject obj)
		=> obj.IsOrphan() ? null : obj.Match(
				player => Configurable.AncestorPlayer,
				room => Configurable.AncestorRoom,
				exit => Configurable.AncestorExit,
				thing => Configurable.AncestorThing
			);

	public static bool Inheritable(this AnySharpObject obj)
		=> obj.IsPlayer
				|| obj.HasFlag("Trust")
				|| obj.Object().Owner().Object.Flags().Any(x => x.Name == "Trust")
				|| IsWizard(obj);

	public static bool Owns(this AnySharpObject who,
															 AnySharpObject what)
		=> who.Object()!.Owner().Object.Id == what.Object()!.Owner().Object.Id;

	/// <summary>
	/// Takes the pattern of '#DBREF/attribute' and splits it out if possible.
	/// </summary>
	/// <param name="dbrefAttr">#DBREF/Attribute</param>
	/// <returns>False if it could not be split. DBRef & Attribute if it could.</returns>
	public static OneOf<(string db, string Attribute), bool> SplitDBRefAndAttr(string DBRefAttr)
	{
		var match = DatabaseReferenceWithAttributeRegex.Match(DBRefAttr);
		var obj = match.Groups["Object"]?.Value;
		var attr = match.Groups["Attribute"]?.Value;

		return string.IsNullOrEmpty(attr) || string.IsNullOrEmpty(obj)
			? false 
			: (obj!, attr!);
	}
	
	public static OneOf<(string? db, string Attribute), bool> SplitOptionalDBRefAndAttr(string DBRefAttr)
	{
		var match = DatabaseReferenceWithAttributeRegex.Match(DBRefAttr);
		var obj = match.Groups["Object"]?.Value;
		var attr = match.Groups["Attribute"]?.Value;

		return string.IsNullOrEmpty(attr)
			? false 
			: (obj!, attr!);
	}

	public static Option<DBRef> ParseDBRef(string dbrefStr)
	{
		var match = DatabaseReferenceRegex.Match(dbrefStr);
		var dbref = match.Groups["DatabaseNumber"]?.Value;
		var cTime = match.Groups["CreationTimestamp"]?.Value;

		return string.IsNullOrEmpty(dbref)
			? new None()
			: new DBRef(int.Parse(dbref!), string.IsNullOrWhiteSpace(cTime) ? null : long.Parse(cTime));
	}

	/// <summary>
	/// A regular expression that takes the form of '#123:43143124' or '#543'.
	/// </summary>
	/// <returns>A regex that has a named group for the DBRef Number and Creation Milliseconds.</returns>
	[GeneratedRegex(@"#(?<DatabaseNumber>\d+)(?::(?<CreationTimestamp>\d+))?")]
	private static partial Regex DatabaseReference();

	/// <summary>
	/// A regular expression that takes the form of 'Object/attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"(?<Object>.+?)/(?<Attribute>[a-zA-Z1-9@_\-\.`]+)")]
	private static partial Regex DatabaseReferenceWithAttribute();
	
	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"(?:(?<Object>.+?)/)?(?<Attribute>[a-zA-Z1-9@_\-\.`]+)")]
	private static partial Regex OptionalDatabaseReferenceWithAttribute();
}
