namespace SharpMUSH.Configuration.Options;

public record CommandOptions(
	[property: SharpConfig(Name = "noisy_whisper", Category = "Command", Description = "Show whisper attempts to others in the room")] bool NoisyWhisper,
	[property: SharpConfig(Name = "possessive_get", Category = "Command", Description = "Allow 'get my object' syntax")] bool PossessiveGet,
	[property: SharpConfig(Name = "possessive_get_d", Category = "Command", Description = "Allow 'get my object' syntax for drop command")] bool PossessiveGetD,
	[property: SharpConfig(Name = "link_to_object", Category = "Command", Description = "Allow linking exits to objects")] bool LinkToObject,
	[property: SharpConfig(Name = "owner_queues", Category = "Command", Description = "Queue commands run with owner privileges")] bool OwnerQueues,
	[property: SharpConfig(Name = "full_invis", Category = "Command", Description = "Complete invisibility hides from all detection")] bool FullInvisibility,
	[property: SharpConfig(Name = "wiz_noaenter", Category = "Command", Description = "Wizards bypass @aenter/@aleave attributes")] bool WizardNoAEnter,
	[property: SharpConfig(Name = "really_safe", Category = "Command", Description = "Enable extra safety checks for destructive commands")] bool ReallySafe,
	[property: SharpConfig(Name = "destroy_possessions", Category = "Command", Description = "Destroy contents when container is destroyed")] bool DestroyPossessions,
	[property: SharpConfig(Name = "probate_judge", Category = "Command", Description = "Player who handles ownership of orphaned objects", ValidationPattern = @"^\d+$")] uint ProbateJudge
);