namespace SharpMUSH.Configuration.Options;

public record CommandOptions(
	[property: SharpConfig(
		Name = "noisy_whisper",
		Category = "Command",
		Description = "Show whisper attempts to others in the room",
		Group = "Communication",
		Order = 1)]
	bool NoisyWhisper,

	[property: SharpConfig(
		Name = "possessive_get",
		Category = "Command",
		Description = "Allow 'get my object' syntax",
		Group = "Syntax",
		Order = 1)]
	bool PossessiveGet,

	[property: SharpConfig(
		Name = "possessive_get_d",
		Category = "Command",
		Description = "Allow 'get my object' syntax for drop command",
		Group = "Syntax",
		Order = 2)]
	bool PossessiveGetD,

	[property: SharpConfig(
		Name = "link_to_object",
		Category = "Command",
		Description = "Allow linking exits to objects",
		Group = "Building",
		Order = 1)]
	bool LinkToObject,

	[property: SharpConfig(
		Name = "owner_queues",
		Category = "Command",
		Description = "Queue commands run with owner privileges",
		Group = "Security",
		Order = 1)]
	bool OwnerQueues,

	[property: SharpConfig(
		Name = "full_invis",
		Category = "Command",
		Description = "Complete invisibility hides from all detection",
		Group = "Security",
		Order = 2)]
	bool FullInvisibility,

	[property: SharpConfig(
		Name = "wiz_noaenter",
		Category = "Command",
		Description = "Wizards bypass @aenter/@aleave attributes",
		Group = "Security",
		Order = 3)]
	bool WizardNoAEnter,

	[property: SharpConfig(
		Name = "really_safe",
		Category = "Command",
		Description = "Enable extra safety checks for destructive commands",
		Group = "Security",
		Order = 4)]
	bool ReallySafe,

	[property: SharpConfig(
		Name = "destroy_possessions",
		Category = "Command",
		Description = "Destroy contents when container is destroyed",
		Group = "Building",
		Order = 2)]
	bool DestroyPossessions,

	[property: SharpConfig(
		Name = "probate_judge",
		Category = "Command",
		Description = "Player who handles ownership of orphaned objects",
		ValidationPattern = @"^\d+$",
		Group = "Building",
		Order = 3,
		Min = 0)]
	uint ProbateJudge
);
