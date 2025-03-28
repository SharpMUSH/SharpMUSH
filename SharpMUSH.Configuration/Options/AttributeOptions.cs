﻿namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[property: PennConfig(Name = "adestroy")] bool ADestroy,
	[property: PennConfig(Name = "amail")] bool AMail,
	[property: PennConfig(Name = "player_listen")] bool PlayerListen,
	[property: PennConfig(Name = "player_ahear")] bool PlayerAHear,
	[property: PennConfig(Name = "startups")] bool Startups,
	[property: PennConfig(Name = "read_remote_desc")] bool ReadRemoteDesc,
	[property: PennConfig(Name = "room_connects")] bool RoomConnects,
	[property: PennConfig(Name = "reverse_shs")] bool ReverseShs,
	[property: PennConfig(Name = "empty_attrs")] bool EmptyAttributes,
	[property: PennConfig(Name = "gender_attr")] string? GenderAttribute,
	[property: PennConfig(Name = "poss_pronoun_attr")] string? PossessivePronounAttribute,
	[property: PennConfig(Name = "abs_pronoun_attr")] string? AbsolutePossessivePronounAttribute,
	[property: PennConfig(Name = "obj_pronoun_attr")] string? ObjectivePronounAttribute,
	[property: PennConfig(Name = "subj_pronoun_attr")] string? SubjectivePronounAttribute
);