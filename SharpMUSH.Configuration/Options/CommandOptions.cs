namespace SharpMUSH.Configuration.Options;

public record CommandOptions(
	[property: PennConfig(Name = "noisy_whisper")] bool NoisyWhisper,
	[property: PennConfig(Name = "possessive_get")] bool PossessiveGet,
	[property: PennConfig(Name = "possessive_get_d")] bool PossessiveGetD,
	[property: PennConfig(Name = "link_to_object")] bool LinkToObject,
	[property: PennConfig(Name = "owner_queues")] bool OwnerQueues,
	[property: PennConfig(Name = "full_invis")] bool FullInvisibility,
	[property: PennConfig(Name = "wiz_noaenter")] bool WizardNoAEnter,
	[property: PennConfig(Name = "really_safe")] bool ReallySafe,
	[property: PennConfig(Name = "destroy_possessions")] bool DestroyPossessions,
	[property: PennConfig(Name = "probate_judge")] uint ProbateJudge
);