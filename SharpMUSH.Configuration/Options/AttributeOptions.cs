namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[property: PennConfig(Name = "adestroy", Description = "Enable @destroy command for objects")] bool ADestroy,
	[property: PennConfig(Name = "amail", Description = "Allow @mail command for sending messages")] bool AMail,
	[property: PennConfig(Name = "player_listen", Description = "Allow players to set @listen attributes")] bool PlayerListen,
	[property: PennConfig(Name = "player_ahear", Description = "Allow players to set @ahear attributes")] bool PlayerAHear,
	[property: PennConfig(Name = "startups", Description = "Run @startup attributes when objects are created")] bool Startups,
	[property: PennConfig(Name = "read_remote_desc", Description = "Allow reading descriptions from remote objects")] bool ReadRemoteDesc,
	[property: PennConfig(Name = "room_connects", Description = "Allow @connect/@disconnect in rooms")] bool RoomConnects,
	[property: PennConfig(Name = "reverse_shs", Description = "Reverse scanning for setunion/setinter/setdiff")] bool ReverseShs,
	[property: PennConfig(Name = "empty_attrs", Description = "Allow setting empty attributes")] bool EmptyAttributes,
	[property: PennConfig(Name = "gender_attr", Description = "Attribute name used to store player gender")] string? GenderAttribute,
	[property: PennConfig(Name = "poss_pronoun_attr", Description = "Attribute name for possessive pronouns (his/her)")] string? PossessivePronounAttribute,
	[property: PennConfig(Name = "abs_pronoun_attr", Description = "Attribute name for absolute possessive pronouns (his/hers)")] string? AbsolutePossessivePronounAttribute,
	[property: PennConfig(Name = "obj_pronoun_attr", Description = "Attribute name for objective pronouns (him/her)")] string? ObjectivePronounAttribute,
	[property: PennConfig(Name = "subj_pronoun_attr", Description = "Attribute name for subjective pronouns (he/she)")] string? SubjectivePronounAttribute
);