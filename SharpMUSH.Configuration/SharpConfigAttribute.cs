using System.Text.Json.Serialization;

namespace SharpMUSH.Configuration;

[AttributeUsage(AttributeTargets.Property)]
public class SharpConfigAttribute : Attribute
{
	[JsonIgnore] public override object TypeId => base.TypeId;

	public required string Name { get; set; }
	public required string Description { get; set; }
	public required string Category { get; set; }
	public string? ValidationPattern { get; set; }
}