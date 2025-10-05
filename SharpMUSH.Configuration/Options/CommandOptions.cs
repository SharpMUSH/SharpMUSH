namespace SharpMUSH.Configuration.Options;

public record CommandOptions(
	[property: SharpConfig(Name = "noisy_whisper", Description = "Show whisper attempts to others in the room")] bool NoisyWhisper,
	[property: SharpConfig(Name = "possessive_get", Description = "Allow 'get my object' syntax")] bool PossessiveGet,
	[property: SharpConfig(Name = "possessive_get_d", Description = "Allow 'get my object' syntax for drop command")] bool PossessiveGetD,
	[property: SharpConfig(Name = "link_to_object", Description = "Allow linking exits to objects")] bool LinkToObject,
	[property: SharpConfig(Name = "owner_queues", Description = "Queue commands run with owner privileges")] bool OwnerQueues,
	[property: SharpConfig(Name = "full_invis", Description = "Complete invisibility hides from all detection")] bool FullInvisibility,
	[property: SharpConfig(Name = "wiz_noaenter", Description = "Wizards bypass @aenter/@aleave attributes")] bool WizardNoAEnter,
	[property: SharpConfig(Name = "really_safe", Description = "Enable extra safety checks for destructive commands")] bool ReallySafe,
	[property: SharpConfig(Name = "destroy_possessions", Description = "Destroy contents when container is destroyed")] bool DestroyPossessions,
	[property: SharpConfig(Name = "probate_judge", Description = "Player who handles ownership of orphaned objects")] uint ProbateJudge
);