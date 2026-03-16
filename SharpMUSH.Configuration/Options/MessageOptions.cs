namespace SharpMUSH.Configuration.Options;

public record MessageOptions(
	[property: SharpConfig(
		Name = "connect_file",
		Category = "Message",
		Description = "Text file displayed when players connect",
		Group = "Text Files",
		Order = 1)]
	string ConnectFile,

	[property: SharpConfig(
		Name = "motd_file",
		Category = "Message",
		Description = "Message of the day file shown to all players",
		Group = "Text Files",
		Order = 2)]
	string MessageOfTheDayFile,

	[property: SharpConfig(
		Name = "wizmotd_file",
		Category = "Message",
		Description = "Message of the day file shown only to wizards",
		Group = "Text Files",
		Order = 3)]
	string WizMessageOfTheDayFile,

	[property: SharpConfig(
		Name = "newuser_file",
		Category = "Message",
		Description = "Text file displayed to newly registered players",
		Group = "Text Files",
		Order = 4)]
	string NewUserFile,

	[property: SharpConfig(
		Name = "register_create_file",
		Category = "Message",
		Description = "Text file displayed during player registration",
		Group = "Text Files",
		Order = 5)]
	string RegisterCreateFile,

	[property: SharpConfig(
		Name = "quit_file",
		Category = "Message",
		Description = "Text file displayed when players disconnect",
		Group = "Text Files",
		Order = 6)]
	string QuitFile,

	[property: SharpConfig(
		Name = "down_file",
		Category = "Message",
		Description = "Text file displayed when server is shutting down",
		Group = "Text Files",
		Order = 7)]
	string DownFile,

	[property: SharpConfig(
		Name = "full_file",
		Category = "Message",
		Description = "Text file displayed when server is at capacity",
		Group = "Text Files",
		Order = 8)]
	string FullFile,

	[property: SharpConfig(
		Name = "guest_file",
		Category = "Message",
		Description = "Text file displayed to guest players",
		Group = "Text Files",
		Order = 9)]
	string GuestFile,

	[property: SharpConfig(
		Name = "who_file",
		Category = "Message",
		Description = "Text file displayed with WHO command output",
		Group = "Text Files",
		Order = 10)]
	string WhoFile,

	[property: SharpConfig(
		Name = "connect_html_file",
		Category = "Message",
		Description = "HTML file displayed when players connect via web",
		Group = "HTML Files",
		Order = 1)]
	string ConnectHtmlFile,

	[property: SharpConfig(
		Name = "motd_html_file",
		Category = "Message",
		Description = "HTML message of the day for web connections",
		Group = "HTML Files",
		Order = 2)]
	string MessageOfTheDayHtmlFile,

	[property: SharpConfig(
		Name = "wizmotd_html_file",
		Category = "Message",
		Description = "HTML wizard message of the day for web connections",
		Group = "HTML Files",
		Order = 3)]
	string WizMessageOfTheDayHtmlFile,

	[property: SharpConfig(
		Name = "newuser_html_file",
		Category = "Message",
		Description = "HTML file for new user registration via web",
		Group = "HTML Files",
		Order = 4)]
	string NewUserHtmlFile,

	[property: SharpConfig(
		Name = "register_create_html_file",
		Category = "Message",
		Description = "HTML file for player creation via web",
		Group = "HTML Files",
		Order = 5)]
	string RegisterCreateHtmlFile,

	[property: SharpConfig(
		Name = "quit_html_file",
		Category = "Message",
		Description = "HTML file displayed when players disconnect via web",
		Group = "HTML Files",
		Order = 6)]
	string QuitHtmlFile,

	[property: SharpConfig(
		Name = "down_html_file",
		Category = "Message",
		Description = "HTML file displayed when server shuts down via web",
		Group = "HTML Files",
		Order = 7)]
	string DownHtmlFile,

	[property: SharpConfig(
		Name = "full_html_file",
		Category = "Message",
		Description = "HTML file displayed when server is full via web",
		Group = "HTML Files",
		Order = 8)]
	string FullHtmlFile,

	[property: SharpConfig(
		Name = "guest_html_file",
		Category = "Message",
		Description = "HTML file displayed to guest players via web",
		Group = "HTML Files",
		Order = 9)]
	string GuestHtmlFile,

	[property: SharpConfig(
		Name = "who_html_file",
		Category = "Message",
		Description = "HTML file for WHO command output via web",
		Group = "HTML Files",
		Order = 10)]
	string WhoHtmlFile,

	[property: SharpConfig(
		Name = "index_html_file",
		Category = "Message",
		Description = "Main HTML index file for web interface",
		Group = "HTML Files",
		Order = 11)]
	string IndexHtmlFile
);
