using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library;

public static partial class HelperFunctions
{
	private static readonly Regex DatabaseReferenceRegex = DatabaseReference();
	private static readonly Regex DatabaseReferenceWithAttributeRegex = DatabaseReferenceWithAttribute();
	private static readonly Regex ObjectWithAttributeRegex = ObjectWithAttribute();
	private static readonly Regex OptionalDatabaseReferenceWithAttributeRegex = OptionalDatabaseReferenceWithAttribute();
	private static readonly Regex DatabaseReferenceWithOptionalAttributeRegex = DatabaseReferenceWithOptionalAttribute();

	public static async ValueTask<bool> IsWizard(this AnySharpObject obj)
		=> (await obj.Object().Flags.WithCancellation(CancellationToken.None)).Any(x => x.Name == "Wizard");

	public static async ValueTask<bool> IsRoyalty(this AnySharpObject obj)
		=> (await obj.Object().Flags.WithCancellation(CancellationToken.None)).Any(x => x.Name == "Royalty");

	public static async ValueTask<bool> IsMistrust(this AnySharpObject obj)
		=> (await obj.Object().Flags.WithCancellation(CancellationToken.None)).Any(x => x.Name == "Mistrust");

	public static bool IsGod(this AnySharpObject obj)
		=> obj.Object().Key == 1;

	public static async ValueTask<bool> IsPriv(this AnySharpObject obj)
		=> IsGod(obj) || await IsWizard(obj) || await IsRoyalty(obj);

	public static async ValueTask<bool> IsSee_All(this AnySharpObject obj)
		=> await IsPriv(obj) || await obj.HasPower("See_All");

	public static async ValueTask<bool> IsGuest(this AnySharpObject obj)
		=> await obj.HasPower("Guest");

	public static async ValueTask<bool> IsVisual(this AnySharpObject obj)
		=> await obj.HasPower("Visual");

	public static async ValueTask<bool> IsDark(this AnySharpObject obj)
		=> await obj.HasPower("Dark");

	public static async ValueTask<bool> IsLight(this AnySharpObject obj)
		=> await obj.HasPower("Light");

	public static async ValueTask<bool> IsDarkLegal(this AnySharpObject obj)
		=> await obj.IsDark() && (await obj.CanDark() || !await obj.IsAlive());

	public static async ValueTask<bool> IsAudible(this AnySharpObject obj)
		=> await obj.HasPower("Audible");

	public static async ValueTask<bool> IsOrphan(this AnySharpObject obj)
		=> await obj.HasPower("Orphan");

	public static async ValueTask<bool> IsAlive(this AnySharpObject obj)
		=> obj.IsPlayer
		   || await IsPuppet(obj)
		   || (await IsAudible(obj) && (await obj.Object().Attributes.WithCancellation(CancellationToken.None))
			   .Any(x => x.Name == "FORWARDLIST"));

	public static async ValueTask<bool> IsPuppet(this AnySharpObject obj)
		=> await obj.HasPower("Puppet");

	public static async ValueTask<bool> HasPower(this AnySharpObject obj, string power)
		=> (await obj.Object().Powers.WithCancellation(CancellationToken.None))
			.Any(x => x.Name == power || x.Alias == power);

	public static bool HasType(this AnySharpObject obj, string validType) =>
		validType switch
		{
			"PLAYER" => obj.IsPlayer,
			"THING" => obj.IsThing,
			"ROOM" => obj.IsRoom,
			"EXIT" => obj.IsExit,
			_ => true, 
		};
	
	public static string TypeString(this AnySharpObject obj) =>
		obj switch
		{
			{IsPlayer: true} => "PLAYER",
			{IsThing: true} => "THING",
			{IsRoom: true} => "ROOM",
			{IsExit: true} => "EXIT",
			_ => "OBJECT"
		};
	
	public static async ValueTask<bool> HasLongFingers(this AnySharpObject obj)
		=> await obj.IsPriv() || await obj.HasPower("Long_Fingers");

	public static async ValueTask<bool> HasFlag(this AnySharpObject obj, string flag)
		=> (await obj.Object().Flags.WithCancellation(CancellationToken.None)).Any(x => x.Name == flag);

	// This may belong in the Permission Service.
	public static async ValueTask<bool> CanDark(this AnySharpObject obj)
		=> await obj.HasPower("Can_Dark") || await obj.IsWizard();

	public static async ValueTask<bool> CanHide(this AnySharpObject obj)
		=> await obj.HasPower("Hide") || await obj.IsPriv();

	public static async ValueTask<DBRef?> Ancestor(this AnySharpObject obj, IOptionsMonitor<PennMUSHOptions> configuration)
		=> await obj.IsOrphan()
			? null
			: obj.Match(
				_ => configuration.CurrentValue.Database.AncestorPlayer == null
					? null
					: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorPlayer)),
				_ => configuration.CurrentValue.Database.AncestorRoom == null
					? null
					: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorRoom)),
				_ => configuration.CurrentValue.Database.AncestorExit == null
					? null
					: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorExit)),
				_ => configuration.CurrentValue.Database.AncestorThing == null
					? (DBRef?)null
					: new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.AncestorThing))
			);

	public static async ValueTask<bool> Inheritable(this AnySharpObject obj)
		=> obj.IsPlayer
		   || await obj.HasFlag("Trust")
		   || (await (await obj.Object().Owner.WithCancellation(CancellationToken.None))
			   .Object.Flags.WithCancellation(CancellationToken.None)).Any(x => x.Name == "Trust")
		   || await IsWizard(obj);

	public static async ValueTask<bool> Owns(this AnySharpObject who,
		AnySharpObject what)
		=> (await who.Object().Owner.WithCancellation(CancellationToken.None)).Object.Id ==
		   (await what.Object().Owner.WithCancellation(CancellationToken.None)).Object.Id;

	/// <summary>
	/// Takes the pattern of '#DBREF/attribute' and splits it out if possible.
	/// </summary>
	/// <param name="dbReferenceAttr">#DBREF/Attribute</param>
	/// <returns><see cref="DbRefAttribute"/> if it is a valid DbRef/Attribute format. Otherwise, <see cref="None"/>.</returns>
	public static Option<DbRefAttribute> SplitDBRefAndAttr(string dbReferenceAttr)
	{
		var match = DatabaseReferenceWithAttributeRegex.Match(dbReferenceAttr);
		var obj = match.Groups["Object"].Value;
		
		// TODO: Validate Attribute Pattern!
		var attr = match.Groups["Attribute"].Value;

		return !string.IsNullOrEmpty(attr) && DBRef.TryParse(obj, out var dbRef)
				? new DbRefAttribute(dbRef!.Value, attr.ToUpper().Split("`").ToArray())
				: new None()
			;
	}
	
	/// <summary>
	/// Takes the pattern of 'Object/attribute' and splits it out if possible.
	/// </summary>
	/// <param name="objectAttr">Object/Attribute</param>
	/// <returns><see cref="DbRefAttribute"/> if it is a valid Object/Attribute format. Otherwise, <see cref="None"/>.</returns>
	public static OneOf<(string db, string Attribute), None> SplitObjectAndAttr(string objectAttr)
	{
		var match = ObjectWithAttributeRegex.Match(objectAttr);
		var obj = match.Groups["Object"].Value;
		
		// TODO: Validate Attribute Pattern!
		var attr = match.Groups["Attribute"].Value;

		return string.IsNullOrEmpty(attr) || string.IsNullOrEmpty(obj)
			? new None()
			: (obj, attr);
	}

	public static OneOf<(string? db, string Attribute), bool> SplitOptionalObjectAndAttr(string ObjectAttr)
	{
		var match = OptionalDatabaseReferenceWithAttributeRegex.Match(ObjectAttr);
		var obj = match.Groups["Object"].Value;
		
		// TODO: Validate Attribute Pattern!
		var attr = match.Groups["Attribute"].Value;

		return string.IsNullOrEmpty(attr)
			? false
			: (obj, attr);
	}

	/// <summary>
	/// This function detects any chance of a loop in the parent chain.
	/// </summary>
	/// <param name="start"></param>
	/// <param name="newParent"></param>
	/// <returns>Whether there's a loop or not</returns>
	public static async ValueTask<bool> SafeToAddParent(AnySharpObject start, AnySharpObject newParent)
	{
		var newParentDbRef = newParent.Object().DBRef;
		
		if ((await start.Object().Parent.WithCancellation(CancellationToken.None)).Object()!.DBRef == newParentDbRef)
		{
			return true;
		}
		
		var children = await start.Object().Children.WithCancellation(CancellationToken.None);
		
		return children.All(x => x.DBRef != newParentDbRef);
	}

	public static OneOf<(string db, string? Attribute), bool> SplitDBRefAndOptionalAttr(string DBRefAttr)
	{
		var match = DatabaseReferenceWithOptionalAttributeRegex.Match(DBRefAttr);
		var obj = match.Groups["Object"].Value;
		
		// TODO: Validate Attribute Pattern!
		var attr = match.Groups["Attribute"].Value;

		return string.IsNullOrEmpty(obj)
			? false
			: (obj, attr);
	}

	public static Option<DBRef> ParseDBRef(string dbrefStr)
	{
		var match = DatabaseReferenceRegex.Match(dbrefStr);
		var dbref = match.Groups["DatabaseNumber"].Value;
		var cTime = match.Groups["CreationTimestamp"].Value;

		return string.IsNullOrEmpty(dbref)
			? new None()
			: new DBRef(int.Parse(dbref), string.IsNullOrWhiteSpace(cTime) ? null : long.Parse(cTime));
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
	[GeneratedRegex(@"#(?<Object>\d+(:\d+)?)/(?<Attribute>[a-zA-Z1-9@_\-\.`]+)")]
	private static partial Regex DatabaseReferenceWithAttribute();
	
	/// <summary>
	/// A regular expression that takes the form of 'Object/attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"(?<Object>[^/]+)/(?<Attribute>[a-zA-Z1-9@_\-\.`]+)")]
	private static partial Regex ObjectWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"(?:(?<Object>[^/]+)/)?(?<Attribute>[a-zA-Z1-9@_\-\.`]+)")]
	private static partial Regex OptionalDatabaseReferenceWithAttribute();

	/// <summary>
	/// A regular expression that takes the form of '[Object/]attributeName'.
	/// </summary>
	/// <returns>A regex that has a named group for the Object and Attribute.</returns>
	[GeneratedRegex(@"(?<Object>[^/]+)(?:/(?<Attribute>[a-zA-Z1-9@_\-\.`]+))?")]
	private static partial Regex DatabaseReferenceWithOptionalAttribute();
}