namespace SharpMUSH.Configuration.Options;

public record MessageOptions(
	[property:
		PennConfig(Name = "connect_file", Description = "Text file displayed when players connect",
			DefaultValue = "connect.txt")]
	string ConnectFile,
	[property:
		PennConfig(Name = "motd_file", Description = "Message of the day file shown to all players",
			DefaultValue = "motd.txt")]
	string MessageOfTheDayFile,
	[property:
		PennConfig(Name = "wizmotd_file", Description = "Message of the day file shown only to wizards",
			DefaultValue = "wizmotd.txt")]
	string WizMessageOfTheDayFile,
	[property:
		PennConfig(Name = "newuser_file", Description = "Text file displayed to newly registered players",
			DefaultValue = "newuser.txt")]
	string NewUserFile,
	[property:
		PennConfig(Name = "register_create_file", Description = "Text file displayed during player registration",
			DefaultValue = "register.txt")]
	string RegisterCreateFile,
	[property:
		PennConfig(Name = "quit_file", Description = "Text file displayed when players disconnect",
			DefaultValue = "quit.txt")]
	string QuitFile,
	[property:
		PennConfig(Name = "down_file", Description = "Text file displayed when server is shutting down",
			DefaultValue = "down.txt")]
	string DownFile,
	[property:
		PennConfig(Name = "full_file", Description = "Text file displayed when server is at capacity",
			DefaultValue = "full.txt")]
	string FullFile,
	[property:
		PennConfig(Name = "guest_file", Description = "Text file displayed to guest players", DefaultValue = "guest.txt")]
	string GuestFile,
	[property:
		PennConfig(Name = "who_file", Description = "Text file displayed with WHO command output",
			DefaultValue = "who.txt")]
	string WhoFile,
	[property:
		PennConfig(Name = "connect_html_file", Description = "HTML file displayed when players connect via web",
			DefaultValue = "connect.html")]
	string ConnectHtmlFile,
	[property:
		PennConfig(Name = "motd_html_file", Description = "HTML message of the day for web connections",
			DefaultValue = "motd.html")]
	string MessageOfTheDayHtmlFile,
	[property:
		PennConfig(Name = "wizmotd_html_file", Description = "HTML wizard message of the day for web connections",
			DefaultValue = "wizmotd.html")]
	string WizMessageOfTheDayHtmlFile,
	[property:
		PennConfig(Name = "newuser_html_file", Description = "HTML file for new user registration via web",
			DefaultValue = "newuser.html")]
	string NewUserHtmlFile,
	[property:
		PennConfig(Name = "register_create_html_file", Description = "HTML file for player creation via web",
			DefaultValue = "register.html")]
	string RegisterCreateHtmlFile,
	[property:
		PennConfig(Name = "quite_html_file", Description = "HTML file displayed when players disconnect via web",
			DefaultValue = "quit.html")]
	string QuitHtmlFile,
	[property:
		PennConfig(Name = "down_html_file", Description = "HTML file displayed when server shuts down via web",
			DefaultValue = "down.html")]
	string DownHtmlFile,
	[property:
		PennConfig(Name = "full_html_file", Description = "HTML file displayed when server is full via web",
			DefaultValue = "full.html")]
	string FullHtmlFile,
	[property:
		PennConfig(Name = "guest_html_file", Description = "HTML file displayed to guest players via web",
			DefaultValue = "guest.html")]
	string GuestHtmlFile,
	[property:
		PennConfig(Name = "who_html_file", Description = "HTML file for WHO command output via web",
			DefaultValue = "who.html")]
	string WhoHtmlFile,
	[property:
		PennConfig(Name = "index_html_file", Description = "Main HTML index file for web interface",
			DefaultValue = "index.html")]
	string IndexHtmlFile
);