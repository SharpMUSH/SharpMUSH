using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public record SharpAttribute(
	string Key, 
	string Name, 
	IEnumerable<SharpAttributeFlag> Flags, 
	int? CommandListIndex, 
	[property: JsonIgnore] string? LongName, 
	[property: JsonIgnore] Lazy<IEnumerable<SharpAttribute>> Leaves, 
	[property: JsonIgnore] Lazy<SharpPlayer> Owner, 
	[property: JsonIgnore] Lazy<SharpAttributeEntry?> SharpAttributeEntry)
{
	public MString Value { get; set; } = MModule.empty();
}