namespace SharpMUSH.Configuration.Options;

public record FileOptions(
	[property: SharpConfig(
		Name = "access_file",
		Category = "File",
		Description = "File containing access control rules",
		Group = "Security Files",
		Order = 1)]
	string AccessFile,

	[property: SharpConfig(
		Name = "names_file",
		Category = "File",
		Description = "File containing restricted player names",
		Group = "Security Files",
		Order = 2)]
	string NamesFile,

	[property: SharpConfig(
		Name = "ssl_private_key_file",
		Category = "File",
		Description = "SSL private key file for secure connections",
		Group = "SSL Configuration",
		Order = 1)]
	string? SSLPrivateKeyFile,

	[property: SharpConfig(
		Name = "ssl_certificate_file",
		Category = "File",
		Description = "SSL certificate file for secure connections",
		Group = "SSL Configuration",
		Order = 2)]
	string? SSLCertificateFile,

	[property: SharpConfig(
		Name = "ssl_ca_file",
		Category = "File",
		Description = "SSL certificate authority file",
		Group = "SSL Configuration",
		Order = 3)]
	string? SSLCAFile,

	[property: SharpConfig(
		Name = "ssl_ca_dir",
		Category = "File",
		Description = "Directory containing SSL certificate authorities",
		Group = "SSL Configuration",
		Order = 4)]
	string? SSLCADirectory,

	[property: SharpConfig(
		Name = "dict_file",
		Category = "File",
		Description = "Dictionary file for spell checking and word lists",
		Group = "Resource Files",
		Order = 1)]
	string? DictionaryFile,

	[property: SharpConfig(
		Name = "colors_file",
		Category = "File",
		Description = "JSON file defining color codes and mappings",
		Group = "Resource Files",
		Order = 2)]
	string? ColorsFile
);
