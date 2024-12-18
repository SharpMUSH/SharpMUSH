using Newtonsoft.Json;

namespace SharpMUSH.Library.Models;

public class SharpPower
{

	[JsonIgnore]
	public string? Id { get; set; }

	public required string Name { get; set; }

	public required bool System { get; set; }

	public required string Alias { get; set; }

	public required string[] SetPermissions { get; set; }

	public required string[] UnsetPermissions { get; set; }

	public required string[] TypeRestrictions { get; set; }
}