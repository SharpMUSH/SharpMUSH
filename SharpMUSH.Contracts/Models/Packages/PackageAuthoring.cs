namespace SharpMUSH.Library.Models.Packages;

/// <summary>An object included in an authoring scan (Phase 7).</summary>
/// <param name="Objid">Stable object id.</param>
/// <param name="Name">Current in-game name.</param>
/// <param name="Type">Game type (thing/room/exit/player).</param>
/// <param name="SuggestedRef">Slugified ref name derived from the object's name.</param>
/// <param name="ParentObjid">Objid of the @parent, or null.</param>
/// <param name="Attributes">Top-level attribute values by name.</param>
/// <param name="Flags">Object flag names.</param>
public sealed record AuthoringObject(
	string Objid,
	string Name,
	string Type,
	string SuggestedRef,
	string? ParentObjid,
	IReadOnlyDictionary<string, string> Attributes,
	IReadOnlyList<string> Flags);

/// <summary>A dbref found in attribute values that is NOT in the selection — the admin must classify it.</summary>
/// <param name="Dbref">Bare dbref as written (e.g. <c>#123</c>).</param>
/// <param name="Occurrences">How many times it appears across the selection.</param>
/// <param name="ExampleAttribute">One attribute (objid/name) where it appears, for context.</param>
public sealed record AuthoringExternalDbref(string Dbref, int Occurrences, string ExampleAttribute);

/// <summary>Result of scanning a selection of live objects for packaging.</summary>
/// <param name="Objects">The selected objects with their attrs and suggested refs.</param>
/// <param name="ExternalDbrefs">Dbrefs referenced but not selected — classify as well-known or configure.</param>
public sealed record PackageAuthoringScan(
	IReadOnlyList<AuthoringObject> Objects,
	IReadOnlyList<AuthoringExternalDbref> ExternalDbrefs);

/// <summary>One selected object in an export request.</summary>
/// <param name="Objid">The object to export.</param>
/// <param name="Ref">The manifest ref name the admin chose.</param>
/// <param name="ExcludedAttributes">Attributes to leave out (DESCRIBE, LAST_*, local data).</param>
public sealed record AuthoringObjectSelection(
	string Objid,
	string Ref,
	IReadOnlyList<string> ExcludedAttributes);

/// <summary>Classification of an external dbref as a configure parameter.</summary>
public sealed record AuthoringConfigureClassification(string Key, string Label);

/// <summary>Everything needed to export a selection as a package manifest.</summary>
/// <param name="PackageId">Package id slug.</param>
/// <param name="Version">Semantic version.</param>
/// <param name="Description">Package description.</param>
/// <param name="License">License identifier, or null.</param>
/// <param name="Authors">Author names.</param>
/// <param name="Objects">Selected objects with chosen refs and exclusions.</param>
/// <param name="WellKnownByDbref">External dbref (bare <c>#N</c>) → well-known ref name.</param>
/// <param name="ConfigureByDbref">External dbref (bare <c>#N</c>) → configure classification.</param>
public sealed record PackageAuthoringRequest(
	string PackageId,
	string Version,
	string Description,
	string? License,
	IReadOnlyList<string> Authors,
	IReadOnlyList<AuthoringObjectSelection> Objects,
	IReadOnlyDictionary<string, string> WellKnownByDbref,
	IReadOnlyDictionary<string, AuthoringConfigureClassification> ConfigureByDbref);
