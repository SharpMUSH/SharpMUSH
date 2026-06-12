namespace SharpMUSH.Server.Services;

/// <summary>
/// The stock character-profile and character-directory softcode seeded onto the default
/// http_handler object (#4), served through the default verb routers (see help sharphttp,
/// HTTP ROUTING). Read-only — profile editing is deliberately out of scope for now.
///
/// Routes (all under the engine's /http/ entry point):
///   GET /http/characters             → GET`CHARACTERS       — JSON array of every player
///   GET /http/profile/schema        → GET`PROFILE`SCHEMA   — the profile field/section schema
///   GET /http/profile?objid=#1:123  → GET`PROFILE          — one character's profile data
///
/// Characters are addressed by **objid** (#dbref:creation-ms) rather than name: stable across
/// renames, no name-matching ambiguity, and safe against dbref recycling. The character list is
/// the objid source — each row carries name, objid, creation time, and a category (FN`CHARCAT:
/// Wizard / Royalty / Guest / Player by default, redefinable per game).
///
/// Profile values are stored as PROFILE`&lt;key&gt; attributes on the character. With no viewer
/// identity on the /http/ path, the profile serves the PUBLIC view only. Real HTTP statuses come
/// from @respond (404 for an unknown objid) — there is no JSON status envelope.
/// </summary>
public static class DefaultProfileHandlerSoftcode
{
	/// <summary>
	/// (attribute name, MUSHcode) pairs, in dependency order: helpers before consumers, and
	/// parent attributes before their backtick children — setting GET`PROFILE`SCHEMA first
	/// would auto-create GET`PROFILE as an empty branch node, which the bootstrap's
	/// never-overwrite check would then skip.
	/// </summary>
	public static readonly IReadOnlyList<(string Attribute, string Code)> Attributes =
	[
		// Renders one profile field as the {value, visible} shape the portal expects.
		// %0 = character objid, %1 = field key. Public view: always visible.
		("FN`FIELD",
			"json(object,value,json(string,get(%0/PROFILE`%1)),visible,json(boolean,true))"),

		// Default directory categorization for one player. %0 = player dbref/objid.
		// Flag-based, first match wins: Wizard > Royalty > Guest (the power) > Player.
		// Games can redefine this attribute to categorize however they like.
		("FN`CHARCAT",
			"if(hasflag(%0,WIZARD),Wizard,if(hasflag(%0,ROYALTY),Royalty,if(haspower(%0,Guest),Guest,Player)))"),

		// One character-directory row. %0 = player dbref/objid.
		// created is the raw creation time in unix milliseconds (ctime's utc form).
		("FN`CHARROW",
			"json(object,name,json(string,name(%0)),objid,json(string,objid(%0)),created,json(number,ctime(%0,1)),category,json(string,u(FN`CHARCAT,%0)))"),

		// fold() step: append one row to the accumulating JSON array. %0 = array so far, %1 = player.
		("FN`JARRINS",
			"json_mod(%0,insert,$\\[[json_query(%0,size)]\\],u(FN`CHARROW,%1))"),

		// GET /http/characters — every player as [{name, objid, created}, ...], unsorted
		// (the portal sorts client-side).
		("GET`CHARACTERS",
			"@respond/type application/json; " +
			"think fold(me/FN`JARRINS,lsearch(all,type,player),\\[\\])"),

		// GET /http/profile?objid=#1:123 — one character's public profile data.
		("GET`PROFILE",
			"@assert cand(isdbref(%q<form.objid>),hastype(%q<form.objid>,player))=@respond 404 NO SUCH CHARACTER; " +
			"@respond/type application/json; " +
			"think json(object," +
				"character,json(string,name(%q<form.objid>))," +
				"objid,json(string,objid(%q<form.objid>))," +
				"dbref,json(string,num(%q<form.objid>))," +
				"fields,json(object," +
					"fullname,u(FN`FIELD,%q<form.objid>,fullname)," +
					"alias,u(FN`FIELD,%q<form.objid>,alias)," +
					"age,u(FN`FIELD,%q<form.objid>,age)," +
					"concept,u(FN`FIELD,%q<form.objid>,concept)," +
					"faction,u(FN`FIELD,%q<form.objid>,faction)," +
					"description,u(FN`FIELD,%q<form.objid>,description)))"),

		// GET /http/profile/schema — the field/section schema the portal renders from.
		("GET`PROFILE`SCHEMA",
			"@respond/type application/json; " +
			"think json(object,sections,json(array," +
				"json(object,name,json(string,Demographics),order,json(number,1),fields,json(array," +
					"json(object,key,json(string,fullname),label,json(string,Full Name),type,json(string,text),visible_to,json(string,public),max_length,json(number,120))," +
					"json(object,key,json(string,alias),label,json(string,Alias),type,json(string,text),visible_to,json(string,public))," +
					"json(object,key,json(string,age),label,json(string,Age),type,json(string,text),visible_to,json(string,public))," +
					"json(object,key,json(string,concept),label,json(string,Concept),type,json(string,text),visible_to,json(string,public))))," +
				"json(object,name,json(string,Status),order,json(number,2),fields,json(array," +
					"json(object,key,json(string,faction),label,json(string,Faction),type,json(string,text),visible_to,json(string,public))))," +
				"json(object,name,json(string,Description),order,json(number,3),fields,json(array," +
					"json(object,key,json(string,description),label,json(string,Description),type,json(string,mstring),visible_to,json(string,public))))))"),
		];
}
