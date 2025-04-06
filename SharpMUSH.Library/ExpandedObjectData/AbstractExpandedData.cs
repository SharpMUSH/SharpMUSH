namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Abstract base class for expanded data.
/// Abstract Data is used to store additional information about an object, that cannot be accessed with attributes.
/// An example is <see cref="ExpandedMailData"/>, used to store a Player's mail folders and their current active folder.
/// </summary>
public abstract record AbstractExpandedData;