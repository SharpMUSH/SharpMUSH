namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// Text to send to an object: markup, or a plain string that needs none.
/// </summary>
public partial union SharpMessage(MString, string);
