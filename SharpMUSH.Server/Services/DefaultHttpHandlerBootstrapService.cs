using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Seeds the default <c>HTTP`PROFILE`*</c> softcode onto the configured <c>http_handler</c>
/// object (#4 by default). Backend-agnostic: it sets attributes through <see cref="IAttributeService"/>,
/// so it works identically on every database provider. Idempotent — each attribute is written only
/// when absent, so admin customizations are never overwritten.
///
/// The stock handler stores profile values as <c>PROFILE`&lt;key&gt;</c> attributes on the character and
/// enforces visibility/editability itself (the engine stays opinionless). Request data arrives as
/// stack args: <c>%0</c>=method, <c>%1</c>=path, <c>%2</c>=query, <c>%3</c>=body, <c>%4</c>=viewer dbref.
/// </summary>
public class DefaultHttpHandlerBootstrapService(
	IMediator mediator,
	IAttributeService attributeService,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<DefaultHttpHandlerBootstrapService> logger) : IHostedService
{
	// Each value below is a single MUSHcode expression. Every JSON leaf is wrapped in
	// json(string,…)/json(number,…)/json(boolean,…) — bare literals are NOT valid JSON values.
	private static readonly (string Attribute, string Code)[] StockAttributes =
	[
		("FN`RANK", "switch(%0,public,0,player,1,owner,2,staff,3,0)"),

		("FN`FIELD",
			"if(gte(u(FN`RANK,%3),u(FN`RANK,%2))," +
				"json(object,value,json(string,get(%0/PROFILE`%1)),visible,json(boolean,true))," +
				"json(object,visible,json(boolean,false)))"),

		("FN`SETONE",
			"if(cand(json_query(%4,exists,%1),gte(u(FN`RANK,%3),u(FN`RANK,%2)))," +
				"[set(%0,PROFILE`%1:[json_query(%4,get,%1)])]%1,)"),

		("HTTP`PROFILE`SCHEMA",
			"json(object,sections,json(array," +
				"json(object,name,json(string,Demographics),order,json(number,1),fields,json(array," +
					"json(object,key,json(string,fullname),label,json(string,Full Name),type,json(string,text),editable_by,json(string,self),visible_to,json(string,public),max_length,json(number,120))," +
					"json(object,key,json(string,alias),label,json(string,Alias),type,json(string,text),editable_by,json(string,self),visible_to,json(string,public))," +
					"json(object,key,json(string,age),label,json(string,Age),type,json(string,text),editable_by,json(string,self),visible_to,json(string,public))," +
					"json(object,key,json(string,concept),label,json(string,Concept),type,json(string,text),editable_by,json(string,self),visible_to,json(string,public))))," +
				"json(object,name,json(string,Status),order,json(number,2),fields,json(array," +
					"json(object,key,json(string,faction),label,json(string,Faction),type,json(string,text),editable_by,json(string,staff),visible_to,json(string,public))," +
					"json(object,key,json(string,approval),label,json(string,Approval),type,json(string,text),editable_by,json(string,staff),visible_to,json(string,player))))," +
				"json(object,name,json(string,Description),order,json(number,3),fields,json(array," +
					"json(object,key,json(string,description),label,json(string,Description),type,json(string,mstring),editable_by,json(string,self),visible_to,json(string,public))))," +
				"json(object,name,json(string,Admin Notes),order,json(number,4),visible_to,json(string,staff),fields,json(array," +
					"json(object,key,json(string,adminnotes),label,json(string,Notes),type,json(string,mstring),editable_by,json(string,staff),visible_to,json(string,staff))))))"),

		("HTTP`PROFILE`GET",
			"[setq(t,pmatch(after(%1,profile/)))]" +
			"[setq(v,switch(1,not(isdbref(%4)),public,orflags(%4,Wr),staff,controls(%4,%qt),owner,player))]" +
			"[if(not(isdbref(%qt))," +
				"json(object,status,json(number,404),error,json(string,No such character))," +
				"json(object,status,json(number,200),character,json(string,name(%qt)),dbref,json(string,%qt),fields,json(object," +
					"fullname,u(FN`FIELD,%qt,fullname,public,%qv)," +
					"alias,u(FN`FIELD,%qt,alias,public,%qv)," +
					"age,u(FN`FIELD,%qt,age,public,%qv)," +
					"concept,u(FN`FIELD,%qt,concept,public,%qv)," +
					"faction,u(FN`FIELD,%qt,faction,public,%qv)," +
					"approval,u(FN`FIELD,%qt,approval,player,%qv)," +
					"description,u(FN`FIELD,%qt,description,public,%qv)," +
					"adminnotes,u(FN`FIELD,%qt,adminnotes,staff,%qv))))]"),

		("HTTP`PROFILE`SET",
			"[setq(t,pmatch(after(%1,profile/)))]" +
			"[setq(v,switch(1,orflags(%4,Wr),staff,controls(%4,%qt),owner,player))]" +
			"[if(not(isdbref(%qt))," +
				"json(object,status,json(number,404),error,json(string,No such character))," +
				"json(object,status,json(number,200),updated,json(string,trim(squish(iter(" +
					"fullname:self age:self alias:self concept:self faction:staff description:self adminnotes:staff," +
					"[setq(k,before(##,:))][setq(lvl,after(##,:))]u(FN`SETONE,%qt,%qk,%qlvl,%qv,%3)," +
					"%b,%b))))))]")
	];

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			logger.LogDebug("No http_handler configured; skipping default profile handler seeding.");
			return;
		}

		var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), cancellationToken);
		if (handlerResult.IsNone)
		{
			logger.LogWarning("Configured http_handler #{HandlerDbRef} not found; cannot seed default profile softcode.", handlerDbRef.Value);
			return;
		}

		var godResult = await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)), cancellationToken);
		if (godResult.IsNone)
		{
			logger.LogWarning("God (#1) not found; cannot seed default profile softcode.");
			return;
		}

		var handler = handlerResult.Known;
		var god = godResult.Known;
		var seeded = 0;

		foreach (var (attribute, code) in StockAttributes)
		{
			var existing = await attributeService.GetAttributeAsync(
				god, handler, attribute, IAttributeService.AttributeMode.Execute, parent: false);
			if (existing.IsAttribute)
			{
				continue; // Respect admin customizations — never overwrite.
			}

			var setResult = await attributeService.SetAttributeAsync(god, handler, attribute, MModule.single(code));
			if (setResult.IsT1)
			{
				logger.LogWarning("Failed to seed {Attribute} on #{HandlerDbRef}: {Error}",
					attribute, handlerDbRef.Value, setResult.AsT1.Value);
			}
			else
			{
				seeded++;
			}
		}

		if (seeded > 0)
		{
			logger.LogInformation("Seeded {Count} default HTTP`PROFILE`* attributes on #{HandlerDbRef}.", seeded, handlerDbRef.Value);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
