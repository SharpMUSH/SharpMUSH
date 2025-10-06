namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[property: SharpConfig(Name = "adestroy", Description = "Enable @destroy command for objects")] bool ADestroy,
	[property: SharpConfig(Name = "amail", Description = "Allow @mail command for sending messages")] bool AMail,
	[property: SharpConfig(Name = "player_listen", Description = "Allow players to set @listen attributes")] bool PlayerListen,
	[property: SharpConfig(Name = "player_ahear", Description = "Allow players to set @ahear attributes")] bool PlayerAHear,
	[property: SharpConfig(Name = "startups", Description = "Run @startup attributes when objects are created")] bool Startups,
	[property: SharpConfig(Name = "read_remote_desc", Description = "Allow reading descriptions from remote objects")] bool ReadRemoteDesc,
	[property: SharpConfig(Name = "room_connects", Description = "Allow @connect/@disconnect in rooms")] bool RoomConnects,
	[property: SharpConfig(Name = "reverse_shs", Description = "Reverse scanning for setunion/setinter/setdiff")] bool ReverseShs,
	[property: SharpConfig(Name = "empty_attrs", Description = "Allow setting empty attributes")] bool EmptyAttributes,
	[property: SharpConfig(Name = "gender_attr", Description = "Attribute name used to store player gender")] string? GenderAttribute,
	[property: SharpConfig(Name = "poss_pronoun_attr", Description = "Object + Attribute name for possessive pronouns (his/her)", ValidationPattern = @"^#\d+/\S+$")] string? PossessivePronounAttribute,
	[property: SharpConfig(Name = "abs_pronoun_attr", Description = "Object + Attribute name for absolute possessive pronouns (his/hers)", ValidationPattern = @"^#\d+/\S+$")] string? AbsolutePossessivePronounAttribute,
	[property: SharpConfig(Name = "obj_pronoun_attr", Description = "Object + Attribute name for objective pronouns (him/her)", ValidationPattern = @"^#\d+/\S+$")] string? ObjectivePronounAttribute,
	[property: SharpConfig(Name = "subj_pronoun_attr", Description = "Object + Attribute name for subjective pronouns (he/she)", ValidationPattern = @"^#\d+/\S+$")] string? SubjectivePronounAttribute
);