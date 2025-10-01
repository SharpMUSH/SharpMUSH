namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[property: PennConfig(Name = "adestroy", Description = "Enable @destroy command for objects", DefaultValue = "no")] bool ADestroy,
	[property: PennConfig(Name = "amail", Description = "Allow @mail command for sending messages", DefaultValue = "no")] bool AMail,
	[property: PennConfig(Name = "player_listen", Description = "Allow players to set @listen attributes", DefaultValue = "yes")] bool PlayerListen,
	[property: PennConfig(Name = "player_ahear", Description = "Allow players to set @ahear attributes", DefaultValue = "yes")] bool PlayerAHear,
	[property: PennConfig(Name = "startups", Description = "Run @startup attributes when objects are created", DefaultValue = "yes")] bool Startups,
	[property: PennConfig(Name = "read_remote_desc", Description = "Allow reading descriptions from remote objects", DefaultValue = "no")] bool ReadRemoteDesc,
	[property: PennConfig(Name = "room_connects", Description = "Allow @connect/@disconnect in rooms", DefaultValue = "yes")] bool RoomConnects,
	[property: PennConfig(Name = "reverse_shs", Description = "Reverse scanning for setunion/setinter/setdiff", DefaultValue = "yes")] bool ReverseShs,
	[property: PennConfig(Name = "empty_attrs", Description = "Allow setting empty attributes", DefaultValue = "yes")] bool EmptyAttributes,
	[property: PennConfig(Name = "gender_attr", Description = "Attribute name used to store player gender", DefaultValue = "SEX")] string? GenderAttribute,
	[property: PennConfig(Name = "poss_pronoun_attr", Description = "Attribute name for possessive pronouns (his/her)", DefaultValue = "")] string? PossessivePronounAttribute,
	[property: PennConfig(Name = "abs_pronoun_attr", Description = "Attribute name for absolute possessive pronouns (his/hers)", DefaultValue = "")] string? AbsolutePossessivePronounAttribute,
	[property: PennConfig(Name = "obj_pronoun_attr", Description = "Attribute name for objective pronouns (him/her)", DefaultValue = "")] string? ObjectivePronounAttribute,
	[property: PennConfig(Name = "subj_pronoun_attr", Description = "Attribute name for subjective pronouns (he/she)", DefaultValue = "")] string? SubjectivePronounAttribute
);