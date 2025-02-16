using DotNext.Threading;
using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public record SharpAttribute(
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string? LongName,
	[property: JsonIgnore] AsyncLazy<IEnumerable<SharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry)
{
	public MString Value { get; set; } = MModule.empty();
}