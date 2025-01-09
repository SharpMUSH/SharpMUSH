namespace SharpMUSH.Configuration.Options;

public record CommandOptions(
	[PennConfig(Name = "noisy_whisper")] bool NoisyWhisper,
	[PennConfig(Name = "possessive_get")] bool PossessiveGet,
	[PennConfig(Name = "possessive_get_d")] bool PossessiveGetD,
	[PennConfig(Name = "link_to_object")] bool LinkToObject,
	[PennConfig(Name = "owner_queues")] bool OwnerQueues,
	[PennConfig(Name = "full_invisibility")] bool FullInvisibility,
	[PennConfig(Name = "wizard_no_aenter")] bool WizardNoAEnter,
	[PennConfig(Name = "really_safe")] bool ReallySafe,
	[PennConfig(Name = "destroy_possessions")] bool DestroyPossessions,
	[PennConfig(Name = "probate_judge")] uint ProbateJudge
);