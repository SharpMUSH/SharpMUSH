namespace SharpMUSH.Configuration.Options;

public record MessageOptions(
	[property: SharpConfig(Name = "connect_file", Description = "Text file displayed when players connect")]
	string ConnectFile,
	[property: SharpConfig(Name = "motd_file", Description = "Message of the day file shown to all players")]
	string MessageOfTheDayFile,
	[property: SharpConfig(Name = "wizmotd_file", Description = "Message of the day file shown only to wizards")]
	string WizMessageOfTheDayFile,
	[property: SharpConfig(Name = "newuser_file", Description = "Text file displayed to newly registered players")]
	string NewUserFile,
	[property: SharpConfig(Name = "register_create_file", Description = "Text file displayed during player registration")]
	string RegisterCreateFile,
	[property: SharpConfig(Name = "quit_file", Description = "Text file displayed when players disconnect")]
	string QuitFile,
	[property: SharpConfig(Name = "down_file", Description = "Text file displayed when server is shutting down")]
	string DownFile,
	[property: SharpConfig(Name = "full_file", Description = "Text file displayed when server is at capacity")]
	string FullFile,
	[property: SharpConfig(Name = "guest_file", Description = "Text file displayed to guest players")]
	string GuestFile,
	[property: SharpConfig(Name = "who_file", Description = "Text file displayed with WHO command output")]
	string WhoFile,
	[property: SharpConfig(Name = "connect_html_file", Description = "HTML file displayed when players connect via web")]
	string ConnectHtmlFile,
	[property: SharpConfig(Name = "motd_html_file", Description = "HTML message of the day for web connections")]
	string MessageOfTheDayHtmlFile,
	[property: SharpConfig(Name = "wizmotd_html_file", Description = "HTML wizard message of the day for web connections")]
	string WizMessageOfTheDayHtmlFile,
	[property: SharpConfig(Name = "newuser_html_file", Description = "HTML file for new user registration via web")]
	string NewUserHtmlFile,
	[property: SharpConfig(Name = "register_create_html_file", Description = "HTML file for player creation via web")]
	string RegisterCreateHtmlFile,
	[property: SharpConfig(Name = "quit_html_file", Description = "HTML file displayed when players disconnect via web")]
	string QuitHtmlFile,
	[property: SharpConfig(Name = "down_html_file", Description = "HTML file displayed when server shuts down via web")]
	string DownHtmlFile,
	[property: SharpConfig(Name = "full_html_file", Description = "HTML file displayed when server is full via web")]
	string FullHtmlFile,
	[property: SharpConfig(Name = "guest_html_file", Description = "HTML file displayed to guest players via web")]
	string GuestHtmlFile,
	[property: SharpConfig(Name = "who_html_file", Description = "HTML file for WHO command output via web")]
	string WhoHtmlFile,
	[property: SharpConfig(Name = "index_html_file", Description = "Main HTML index file for web interface")]
	string IndexHtmlFile
);