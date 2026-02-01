using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using System.Reflection;

namespace SharpMUSH.Library.API;

/// <summary>
/// Builds enhanced configuration schema from SharpMUSHOptions
/// </summary>
public static class SchemaBuilder
{
	public static ConfigurationSchema BuildSchema(SharpMUSHOptions options)
	{
		var schema = new ConfigurationSchema();
		
		// Build category metadata
		schema.Categories = BuildCategories();
		
		// Build property metadata
		schema.Properties = BuildProperties(options);
		
		return schema;
	}
	
	private static List<CategoryMetadata> BuildCategories()
	{
		return new List<CategoryMetadata>
		{
			new()
			{
				Name = "NetOptions",
				DisplayName = "Network Configuration",
				Description = "Server connection and network settings",
				Icon = "mdi-network",
				Order = 1,
				Groups = new List<GroupMetadata>
				{
					new() { Name = "connection", DisplayName = "Connection Settings", Order = 1 },
					new() { Name = "limits", DisplayName = "Connection Limits", Order = 2 },
					new() { Name = "protocol", DisplayName = "Network Protocol", Order = 3 }
				}
			},
			new()
			{
				Name = "LimitOptions",
				DisplayName = "Limits",
				Description = "Resource and capacity limits",
				Icon = "mdi-speedometer",
				Order = 2,
				Groups = new List<GroupMetadata>
				{
					new() { Name = "general", DisplayName = "General Limits", Order = 1 }
				}
			},
			new()
			{
				Name = "ChatOptions",
				DisplayName = "Chat",
				Description = "Chat and communication settings",
				Icon = "mdi-chat",
				Order = 3,
				Groups = new List<GroupMetadata>
				{
					new() { Name = "general", DisplayName = "Chat Settings", Order = 1 }
				}
			}
			// Add more categories as needed
		};
	}
	
	private static Dictionary<string, PropertyMetadata> BuildProperties(SharpMUSHOptions options)
	{
		var properties = new Dictionary<string, PropertyMetadata>();
		
		// Build Network properties
		AddNetworkProperties(properties, options.Net);
		
		// Add more property groups as needed
		
		return properties;
	}
	
	private static void AddNetworkProperties(Dictionary<string, PropertyMetadata> properties, NetOptions netOptions)
	{
		// Connection Settings Group
		properties.Add("NetOptions.Port", new PropertyMetadata
		{
			Name = "Port",
			DisplayName = "Port",
			Description = "The port number the server listens on for incoming connections",
			Category = "NetOptions",
			Group = "connection",
			Order = 1,
			Type = "integer",
			Component = "numeric",
			DefaultValue = 4201u,
			Min = 1,
			Max = 65535,
			Required = true,
			Path = "NetOptions.Port"
		});
		
		properties.Add("NetOptions.SslPort", new PropertyMetadata
		{
			Name = "SslPort",
			DisplayName = "SSL Port",
			Description = "Port for SSL/TLS encrypted connections",
			Category = "NetOptions",
			Group = "connection",
			Order = 2,
			Type = "integer",
			Component = "numeric",
			DefaultValue = 4202u,
			Min = 1,
			Max = 65535,
			Required = true,
			Path = "NetOptions.SslPort"
		});
		
		properties.Add("NetOptions.UseSSL", new PropertyMetadata
		{
			Name = "UseSSL",
			DisplayName = "Enable SSL/TLS",
			Description = "Require encrypted connections for enhanced security",
			Category = "NetOptions",
			Group = "connection",
			Order = 3,
			Type = "boolean",
			Component = "switch",
			DefaultValue = true,
			Path = "NetOptions.UseSSL"
		});
		
		// Connection Limits Group
		properties.Add("NetOptions.MaxConnections", new PropertyMetadata
		{
			Name = "MaxConnections",
			DisplayName = "Maximum Connections",
			Description = "Maximum number of simultaneous player connections allowed",
			Category = "NetOptions",
			Group = "limits",
			Order = 1,
			Type = "integer",
			Component = "numeric",
			DefaultValue = 100u,
			Min = 1,
			Required = true,
			Path = "NetOptions.MaxConnections"
		});
		
		properties.Add("NetOptions.ConnectionsPerIP", new PropertyMetadata
		{
			Name = "ConnectionsPerIP",
			DisplayName = "Connections Per IP",
			Description = "Maximum connections allowed from a single IP address",
			Category = "NetOptions",
			Group = "limits",
			Order = 2,
			Type = "integer",
			Component = "numeric",
			DefaultValue = 5u,
			Min = 1,
			Required = true,
			Path = "NetOptions.ConnectionsPerIP"
		});
		
		properties.Add("NetOptions.IdleTimeout", new PropertyMetadata
		{
			Name = "IdleTimeout",
			DisplayName = "Idle Timeout (seconds)",
			Description = "Disconnect players who are idle for this many seconds",
			Category = "NetOptions",
			Group = "limits",
			Order = 3,
			Type = "integer",
			Component = "numeric",
			DefaultValue = 3600u,
			Min = 60,
			Tooltip = "Minimum 60 seconds recommended",
			Path = "NetOptions.IdleTimeout"
		});
		
		// Network Protocol Group
		properties.Add("NetOptions.EnablePueblo", new PropertyMetadata
		{
			Name = "EnablePueblo",
			DisplayName = "Enable Pueblo/HTML Support",
			Description = "Allow clients to render HTML formatting",
			Category = "NetOptions",
			Group = "protocol",
			Order = 1,
			Type = "boolean",
			Component = "switch",
			DefaultValue = false,
			Path = "NetOptions.EnablePueblo"
		});
		
		properties.Add("NetOptions.EnableIPv6", new PropertyMetadata
		{
			Name = "EnableIPv6",
			DisplayName = "Enable IPv6",
			Description = "Accept connections over IPv6 in addition to IPv4",
			Category = "NetOptions",
			Group = "protocol",
			Order = 2,
			Type = "boolean",
			Component = "switch",
			DefaultValue = true,
			Path = "NetOptions.EnableIPv6"
		});
		
		properties.Add("NetOptions.EnableTelnet", new PropertyMetadata
		{
			Name = "EnableTelnet",
			DisplayName = "Enable Telnet Negotiation",
			Description = "Support telnet protocol features",
			Category = "NetOptions",
			Group = "protocol",
			Order = 3,
			Type = "boolean",
			Component = "switch",
			DefaultValue = true,
			Path = "NetOptions.EnableTelnet"
		});
	}
}
