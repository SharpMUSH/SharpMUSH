namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// Text to send to an object: markup, or a plain string that needs none.
/// </summary>
public union SharpMessage(MString, string);
