namespace SharpMUSH.Configuration.Options;

public record CommandOptions(
	[property: PennConfig(Name = "noisy_whisper", Description = "Show whisper attempts to others in the room", DefaultValue = "no")] bool NoisyWhisper,
	[property: PennConfig(Name = "possessive_get", Description = "Allow 'get my object' syntax", DefaultValue = "yes")] bool PossessiveGet,
	[property: PennConfig(Name = "possessive_get_d", Description = "Allow 'get my object' syntax for drop command", DefaultValue = "no")] bool PossessiveGetD,
	[property: PennConfig(Name = "link_to_object", Description = "Allow linking exits to objects", DefaultValue = "yes")] bool LinkToObject,
	[property: PennConfig(Name = "owner_queues", Description = "Queue commands run with owner privileges", DefaultValue = "no")] bool OwnerQueues,
	[property: PennConfig(Name = "full_invis", Description = "Complete invisibility hides from all detection", DefaultValue = "no")] bool FullInvisibility,
	[property: PennConfig(Name = "wiz_noaenter", Description = "Wizards bypass @aenter/@aleave attributes", DefaultValue = "no")] bool WizardNoAEnter,
	[property: PennConfig(Name = "really_safe", Description = "Enable extra safety checks for destructive commands", DefaultValue = "yes")] bool ReallySafe,
	[property: PennConfig(Name = "destroy_possessions", Description = "Destroy contents when container is destroyed", DefaultValue = "yes")] bool DestroyPossessions,
	[property: PennConfig(Name = "probate_judge", Description = "Player who handles ownership of orphaned objects", DefaultValue = "1")] uint ProbateJudge
);