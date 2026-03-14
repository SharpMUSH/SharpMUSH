namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[property: SharpConfig(
		Name = "adestroy",
		Category = "Attribute",
		Description = "Enable @destroy command for objects",
		Group = "Commands",
		Order = 1)]
	bool ADestroy,

	[property: SharpConfig(
		Name = "amail",
		Category = "Attribute",
		Description = "Allow @mail command for sending messages",
		Group = "Commands",
		Order = 2)]
	bool AMail,

	[property: SharpConfig(
		Name = "player_listen",
		Category = "Attribute",
		Description = "Allow players to set @listen attributes",
		Group = "Player Permissions",
		Order = 1)]
	bool PlayerListen,

	[property: SharpConfig(
		Name = "player_ahear",
		Category = "Attribute",
		Description = "Allow players to set @ahear attributes",
		Group = "Player Permissions",
		Order = 2)]
	bool PlayerAHear,

	[property: SharpConfig(
		Name = "startups",
		Category = "Attribute",
		Description = "Run @startup attributes when objects are created",
		Group = "Behavior",
		Order = 1)]
	bool Startups,

	[property: SharpConfig(
		Name = "read_remote_desc",
		Category = "Attribute",
		Description = "Allow reading descriptions from remote objects",
		Group = "Behavior",
		Order = 2)]
	bool ReadRemoteDesc,

	[property: SharpConfig(
		Name = "room_connects",
		Category = "Attribute",
		Description = "Allow @connect/@disconnect in rooms",
		Group = "Behavior",
		Order = 3)]
	bool RoomConnects,

	[property: SharpConfig(
		Name = "reverse_shs",
		Category = "Attribute",
		Description = "Reverse scanning for setunion/setinter/setdiff",
		Group = "Behavior",
		Order = 4)]
	bool ReverseShs,

	[property: SharpConfig(
		Name = "empty_attrs",
		Category = "Attribute",
		Description = "Allow setting empty attributes",
		Group = "Behavior",
		Order = 5)]
	bool EmptyAttributes,

	[property: SharpConfig(
		Name = "gender_attr",
		Category = "Attribute",
		Description = "Attribute name used to store player gender",
		Group = "Pronouns",
		Order = 1)]
	string? GenderAttribute,

	[property: SharpConfig(
		Name = "poss_pronoun_attr",
		Category = "Attribute",
		Description = "Object + Attribute name for possessive pronouns (his/her)",
		ValidationPattern = @"^#\d+/\S+$",
		Group = "Pronouns",
		Order = 2,
		Tooltip = "Format: #dbref/attributename")]
	string? PossessivePronounAttribute,

	[property: SharpConfig(
		Name = "abs_pronoun_attr",
		Category = "Attribute",
		Description = "Object + Attribute name for absolute possessive pronouns (his/hers)",
		ValidationPattern = @"^#\d+/\S+$",
		Group = "Pronouns",
		Order = 3,
		Tooltip = "Format: #dbref/attributename")]
	string? AbsolutePossessivePronounAttribute,

	[property: SharpConfig(
		Name = "obj_pronoun_attr",
		Category = "Attribute",
		Description = "Object + Attribute name for objective pronouns (him/her)",
		ValidationPattern = @"^#\d+/\S+$",
		Group = "Pronouns",
		Order = 4,
		Tooltip = "Format: #dbref/attributename")]
	string? ObjectivePronounAttribute,

	[property: SharpConfig(
		Name = "subj_pronoun_attr",
		Category = "Attribute",
		Description = "Object + Attribute name for subjective pronouns (he/she)",
		ValidationPattern = @"^#\d+/\S+$",
		Group = "Pronouns",
		Order = 5,
		Tooltip = "Format: #dbref/attributename")]
	string? SubjectivePronounAttribute
);
