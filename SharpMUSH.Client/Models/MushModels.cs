namespace SharpMUSH.Client.Models;

public class MushObject
{
	public int Dbref { get; set; }
	public string Name { get; set; } = string.Empty;
	public MushObjectType Type { get; set; }
	public string Flags { get; set; } = string.Empty;
	public string Owner { get; set; } = string.Empty;
	public List<MushAttribute> Attributes { get; set; } = [];
}

public class MushAttribute
{
	public string Name { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
	public List<string> AttributeFlags { get; set; } = [];
}

public enum MushObjectType
{
	Thing,
	Room,
	Exit,
	Player,
	Unknown
}

public class MushSearchResult
{
	public int Dbref { get; set; }
	public string Name { get; set; } = string.Empty;
	public MushObjectType Type { get; set; }
}
