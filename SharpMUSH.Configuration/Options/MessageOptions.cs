namespace SharpMUSH.Configuration.Options;

public record MessageOptions(
	[property: PennConfig(Name = "connect_file", Description = "Text file displayed when players connect")] string ConnectFile,
	[property: PennConfig(Name = "motd_file", Description = "Message of the day file shown to all players")] string MessageOfTheDayFile,
	[property: PennConfig(Name = "wizmotd_file", Description = "Message of the day file shown only to wizards")] string WizMessageOfTheDayFile,
	[property: PennConfig(Name = "newuser_file", Description = "Text file displayed to newly registered players")] string NewUserFile,
	[property: PennConfig(Name = "register_create_file", Description = "Text file displayed during player registration")] string RegisterCreateFile,
	[property: PennConfig(Name = "quit_file", Description = "Text file displayed when players disconnect")] string QuitFile,
	[property: PennConfig(Name = "down_file", Description = "Text file displayed when server is shutting down")] string DownFile,
	[property: PennConfig(Name = "full_file", Description = "Text file displayed when server is at capacity")] string FullFile,
	[property: PennConfig(Name = "guest_file", Description = "Text file displayed to guest players")] string GuestFile,
	[property: PennConfig(Name = "who_file", Description = "Text file displayed with WHO command output")] string WhoFile,
	[property: PennConfig(Name = "connect_html_file", Description = "HTML file displayed when players connect via web")] string ConnectHtmlFile,
	[property: PennConfig(Name = "motd_html_file", Description = "HTML message of the day for web connections")] string MessageOfTheDayHtmlFile,
	[property: PennConfig(Name = "wizmotd_html_file", Description = "HTML wizard message of the day for web connections")] string WizMessageOfTheDayHtmlFile,
	[property: PennConfig(Name = "newuser_html_file", Description = "HTML file for new user registration via web")] string NewUserHtmlFile,
	[property: PennConfig(Name = "register_create_html_file", Description = "HTML file for player creation via web")] string RegisterCreateHtmlFile,
	[property: PennConfig(Name = "quite_html_file", Description = "HTML file displayed when players disconnect via web")] string QuitHtmlFile,
	[property: PennConfig(Name = "down_html_file", Description = "HTML file displayed when server shuts down via web")] string DownHtmlFile,
	[property: PennConfig(Name = "full_html_file", Description = "HTML file displayed when server is full via web")] string FullHtmlFile,
	[property: PennConfig(Name = "guest_html_file", Description = "HTML file displayed to guest players via web")] string GuestHtmlFile,
	[property: PennConfig(Name = "who_html_file", Description = "HTML file for WHO command output via web")] string WhoHtmlFile,
	[property: PennConfig(Name = "index_html_file", Description = "Main HTML index file for web interface")] string IndexHtmlFile
);