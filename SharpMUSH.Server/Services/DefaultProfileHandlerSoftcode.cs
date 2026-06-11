namespace SharpMUSH.Server.Services;

/// <summary>
/// The stock <c>HTTP`PROFILE`*</c> softcode seeded onto the default http_handler object.
/// Shared by <see cref="DefaultHttpHandlerBootstrapService"/> (first-run seeding) and the admin
/// "reset to defaults" endpoint. Each value is a single MUSHcode expression; every JSON leaf is
/// wrapped in json(string,…)/json(number,…)/json(boolean,…) — bare literals are not valid JSON.
///
/// Request data arrives as stack args: %0=method, %1=path, %2=query, %3=body, %4=viewer dbref.
/// The handler stores profile values as PROFILE`&lt;key&gt; attributes on the character and enforces
/// visibility/editability itself; the engine stays opinionless.
/// </summary>
public static class DefaultProfileHandlerSoftcode
{
	/// <summary>(attribute name, MUSHcode) pairs, in dependency order (helpers before consumers).</summary>
	public static readonly IReadOnlyList<(string Attribute, string Code)> Attributes =
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
}
