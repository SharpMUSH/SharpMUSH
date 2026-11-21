namespace SharpMUSH.Library.Models;

/// <summary>Which of an attribute's two keys: the one that lets someone read it, or the one that lets them write it.</summary>
public enum AttributeLockKind
{
	Read,
	Write
}
