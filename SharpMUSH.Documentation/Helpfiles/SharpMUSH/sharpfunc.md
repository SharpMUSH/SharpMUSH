# FUNCTION LIST
# FUNCTION TYPES
  Several major variants of functions are available. The help topics are listed below, together with a quick summary of the function type and some examples of that type of function.

  [Attribute functions]: attribute-related manipulations (GET, UFUN) <br>
  [Bitwise functions]: manipulation of individual bits of numbers (SHL, BOR) <br>
  [Boolean functions]: produce 0 or 1 (false or true) answers (OR, AND) <br>
  [channel functions]: get information about channels (CTITLE, CWHO) <br>
  [Communication functions]: send messages to objects (PEMIT, OEMIT) <br>
  [Connection functions]: get information about a player's connection (CONN) <br>
  [Dbref functions]: return dbref info related to objects (LOC, LEXITS) <br>
  [HTML FUNCTIONS]: output HTML tags for Pueblo and WebSocket clients <br>
  [Information functions]: find out something about objects (FLAGS, MONEY) <br>
  [JSON FUNCTIONS]: create and manipulate JSON objects (JSON, JSON_MAP) <br>
  [List functions]: manipulate lists (REVWORDS, FIRST) <br>
  [Mail Functions]: manipulate @mail (MAIL, FOLDERSTATS) <br>
  [Math functions]: number manipulation, generic or integers only (ADD, DIV) <br>
  [MEDIA FUNCTIONS]: sounds, pictures and panes, written once for every client (SOUND, IMAGE) <br>
  [Regular expression functions]: Regular expressions (REGMATCH, REGEDIT) <br>
  [SQL functions]: access SQL databases (SQL, SQLESCAPE) <br>
  [String functions]: string manipulation (ESCAPE, FLIP) <br>
  [Time functions]: formatting and display of time (TIME, CONVSECS) <br>
  [Utility functions]: general utilities (ISINT, COMP) <br>
  [Wiki functions]: read the shared wiki (WIKI, WIKILIST) <br>

  The command "@list/functions" lists all functions on the game.<br>
  The command "@function" lists only the game's custom global functions defined via the @function command.

# Attribute functions
  These functions can access or alter information stored in attributes on objects.

|                |                |                |                |
|----------------|----------------|----------------|----------------|
| [APOSS()]      | [ATTRIB_SET()] | [DEFAULT()]    | [EDEFAULT()]   |
| [EVAL()]       | [FLAGS()]      | [GET()]        | [GREP()]       |
| [GREPI()]      | [HASATTR()]    | [HASATTRP()]   | [HASATTRVAL()] |
| [HASFLAG()]    | [LATTR()]      | [LFLAGS()]     | [NATTR()]      |
| [OBJ()]        | [OWNER()]      | [PFUN()]       | [POSS()]       |
| [REGLATTR()]   | [REGREP()]     | [REGREPI()]    | [REGXATTR()]   |
| [SET()]        | [SUBJ()]       | [U()]          | [UDEFAULT()]   |
| [UFUN()]       | [ULAMBDA()]    | [ULDEFAULT()]  | [ULOCAL()]     |
| [V()]          | [WILDGREP()]   | [WILDGREPI()]  | [XATTR()]      |
| [XGET()]       | [ZFUN()]       |                |                |

::: seealso
- [attributes]
- [NON-STANDARD ATTRIBUTES]
:::
# Bitwise functions
  These functions treat integers as a sequence of binary bits (either 0 or 1) and manipulate them.

  For example, 2 is represented as '0010' and 4 as '0100'. If these two numbers are bitwise-or'ed together with BOR(), the result is 6, or (in binary) '0110'. These functions are useful for storing small lists of toggle (Yes/No) options efficiently.

|              |              |              |              |
|--------------|--------------|--------------|--------------|
| [BAND()]     | [BASECONV()] | [BNAND()]    | [BNOT()]     |
| [BOR()]      | [BXOR()]     | [SHL()]      | [SHR()]      |

# Boolean functions
  Boolean functions all return 0 or 1 as an answer.

  Your MUSH may be configured to use traditional SharpMUSH booleans, in which case non-zero numbers, non-negative db#'s, and strings are all considered "true" when passed to these functions. Alternatively, your MUSH may be using TinyMUSH 2.2 booleans, in which case only non-zero numbers are "true". Check @config tiny_booleans.

|          |          |          |          |
|----------|----------|----------|----------|
| [AND()]  | [CAND()] | [COR()]  | [EQ()]   |
| [GT()]   | [GTE()]  | [LT()]   | [LTE()]  |
| [NAND()] | [NEQ()]  | [NOR()]  | [NOT()]  |
| [OR()]   | [T()]    | [XOR()]  |          |

::: seealso
- [boolean values]
- [@config]
:::
# Communication functions
  Communication functions are side-effect functions that send a message to an object or objects. [PAGERECALL()] and [PAGECONVERSATIONS()] are the exception: they read your own page log and send nothing (SharpMUSH extensions).

|                       |                       |                       |                       |
|-----------------------|-----------------------|-----------------------|-----------------------|
| [CEMIT()]             | [EMIT()]              | [LEMIT()]             | [MESSAGE()]           |
| [NSEMIT()]            | [NSLEMIT()]           | [NSOEMIT()]           | [NSPEMIT()]           |
| [NSPROMPT()]          | [NSREMIT()]           | [NSZEMIT()]           | [OEMIT()]             |
| [PAGECONVERSATIONS()] | [PAGERECALL()]        | [PEMIT()]             | [PROMPT()]            |
| [REMIT()]             | [ZEMIT()]             |                       |                       |

::: seealso
- [channel functions]
- [Mail Functions]
:::

# Connection functions
  Connection functions return information about the connections open on a game, or about specific connections.

|                |                |                |                |
|----------------|----------------|----------------|----------------|
| [ADDRLOG()]    | [CMDS()]       | [CONN()]       | [CONNLOG()]    |
| [CONNRECORD()] | [DOING()]      | [HEIGHT()]     | [HIDDEN()]     |
| [HOST()]       | [IDLE()]       | [IPADDR()]     | [LPORTS()]     |
| [LWHO()]       | [LWHOID()]     | [MWHO()]       | [MWHOID()]     |
| [NMWHO()]      | [NWHO()]       | [PLAYER()]     | [PORTS()]      |
| [PUEBLO()]     | [RECV()]       | [SENT()]       | [SSL()]        |
| [TERMINFO()]   | [WIDTH()]      | [XMWHO()]      | [XMWHOID()]    |
| [XWHO()]       | [XWHOID()]     | [ZMWHO()]      | [ZWHO()]       |

# Dbref functions
  Dbref functions return a dbref or list of dbrefs related to some value on an object.

|               |               |               |               |
|---------------|---------------|---------------|---------------|
| [CHILDREN()]  | [CON()]       | [ENTRANCES()] | [EXIT()]      |
| [FOLLOWERS()] | [FOLLOWING()] | [HOME()]      | [LCON()]      |
| [LEXITS()]    | [LOC()]       | [LOCATE()]    | [LPARENT()]   |
| [LPLAYERS()]  | [LSEARCH()]   | [LTHINGS()]   | [LVCON()]     |
| [LVEXITS()]   | [LVPLAYERS()] | [LVTHINGS()]  | [NAMELIST()]  |
| [NEXT()]      | [NEXTDBREF()] | [NUM()]       | [OWNER()]     |
| [PARENT()]    | [PMATCH()]    | [RLOC()]      | [RNUM()]      |
| [ROOM()]      | [WHERE()]     | [ZFIND()]     | [ZONE()]      |

::: seealso
- [database]
- [Information functions]
:::
# Information functions
  Information functions return values related to objects or the game.

|                |                |                |                |
|----------------|----------------|----------------|----------------|
| [ACCNAME()]    | [ALIAS()]      | [ANDFLAGS()]   | [ANDLFLAGS()]  |
| [ANDLPOWERS()] | [CONFIG()]     | [CONTROLS()]   | [CSECS()]      |
| [CTIME()]      | [DOWNMOTD()]   | [ELOCK()]      | [FINDABLE()]   |
| [FLAGS()]      | [FULLALIAS()]  | [FULLMOTD()]   | [FULLNAME()]   |
| [GETPIDS()]    | [HASATTR()]    | [HASATTRP()]   | [HASFLAG()]    |
| [HASPOWER()]   | [HASROLE()]    | [HASTYPE()]    | [INAME()]      |
| [ISAPPROVED()] | [LFLAGS()]     | [LOCK()]       | [LOCKFLAGS()]  |
| [LOCKOWNER()]  | [LOCKS()]      | [LPIDS()]      | [LSTATS()]     |
| [MONEY()]      | [MONIKER()]    | [MOTD()]       | [MSECS()]      |
| [MTIME()]      | [MUDNAME()]    | [MUDURL()]     | [NAME()]       |
| [NATTR()]      | [NEARBY()]     | [OBJID()]      | [OBJMEM()]     |
| [ORFLAGS()]    | [ORLFLAGS()]   | [ORLPOWERS()]  | [PERMISSION()] |
| [PIDINFO()]    | [PLAYERMEM()]  | [POLL()]       | [POWERS()]     |
| [QUOTA()]      | [RESTARTS()]   | [ROLES()]      | [TYPE()]       |
| [VERSION()]    | [VISIBLE()]    | [WIZMOTD()]    |                |

::: seealso
- [Dbref functions]
:::

# List functions
  List functions take at least one list of elements and return transformed lists or one or more members of those lists. Most of these functions can take an arbitrary `<delimiter>` argument to specify what delimits list elements; if none is provided, a space is used by default.

|                 |                 |                 |                 |
|-----------------|-----------------|-----------------|-----------------|
| [CHAIN()]       | [ELEMENTS()]    | [EVERY()]       | [EXTRACT()]     |
| [FILTER()]      | [FILTERBOOL()]  | [FILTERQ()]     | [FIRST()]       |
| [FOLD()]        | [GRAB()]        | [GRABALL()]     | [INDEX()]       |
| [ITEMIZE()]     | [ITEMS()]       | [ITER()]        | [JITER()]       |
| [LAST()]        | [LDELETE()]     | [LINSERT()]     | [LISTSET()]     |
| [LOCKFILTER()]  | [LREPLACE()]    | [MAP()]         | [MATCH()]       |
| [MATCHALL()]    | [MEMBER()]      | [MIX()]         | [MUNGE()]       |
| [NAMEGRAB()]    | [NAMEGRABALL()] | [RANDWORD()]    | [REMOVE()]      |
| [REST()]        | [REVWORDS()]    | [SETDIFF()]     | [SETINTER()]    |
| [SETSYMDIFF()]  | [SETUNION()]    | [SHUFFLE()]     | [SOME()]        |
| [SORT()]        | [SORTBY()]      | [SORTKEY()]     | [SPLICE()]      |
| [STEP()]        | [TABLE()]       | [UNIQUE()]      | [WORDPOS()]     |
| [WORDS()]       |                 |                 |                 |

::: seealso
- [LISTS]
:::

# Math functions
  Math functions take one or more floating point numbers and return a numeric value.

|              |              |              |              |
|--------------|--------------|--------------|--------------|
| [ABS()]      | [ACOS()]     | [ADD()]      | [ASIN()]     |
| [ATAN()]     | [ATAN2()]    | [BOUND()]    | [CEIL()]     |
| [COS()]      | [CTU()]      | [DIST2D()]   | [DIST3D()]   |
| [E()]        | [EXP()]      | [FDIV()]     | [FLOOR()]    |
| [FMOD()]     | [FRACTION()] | [LMATH()]    | [LN()]       |
| [LOG()]      | [MAX()]      | [MEAN()]     | [MEDIAN()]   |
| [MIN()]      | [MUL()]      | [PI()]       | [POWER()]    |
| [ROOT()]     | [ROUND()]    | [SIGN()]     | [SIN()]      |
| [SQRT()]     | [STDDEV()]   | [SUB()]      | [TAN()]      |
| [TRUNC()]    | [VAL()]      |              |              |

  These functions operate only on integers (if passed floating point numbers, they will return an error or misbehave):

|               |               |               |               |
|---------------|---------------|---------------|---------------|
| [DEC()]       | [DIV()]       | [FLOORDIV()]  | [INC()]       |
| [MOD()]       | [REMAINDER()] |               |               |


  Math functions are affected by a number of @config options, including the TinyMUSH compatability options null_eq_zero and tiny_math.


::: seealso
- [Vector functions]
:::
# Vector functions
  These functions operate on n-dimensional vectors. A vector is a delimiter-separated list of numbers (space-separated, by default):

|            |            |            |            |
|------------|------------|------------|------------|
| [VADD()]   | [VCROSS()] | [VDIM()]   | [VDOT()]   |
| [VMAG()]   | [VMAX()]   | [VMIN()]   | [VMUL()]   |
| [VSUB()]   | [VUNIT()]  |            |            |



::: seealso
- [Math functions]
:::

# Regular expression functions
  These functions take a regular expression (regexp, or re) and match it against assorted things.

|                  |                  |                  |                  |
|------------------|------------------|------------------|------------------|
| [REGEDIT()]      | [REGEDITALL()]   | [REGEDITALLI()]  | [REGEDITI()]     |
| [REGLATTR()]     | [REGLATTRP()]    | [REGMATCH()]     | [REGMATCHI()]    |
| [REGNATTR()]     | [REGNATTRP()]    | [REGRAB()]       | [REGRABALL()]    |
| [REGRABALLI()]   | [REGRABI()]      | [REGREP()]       | [REGREPI()]      |
| [REGREPLACE()]   | [REGXATTR()]     | [REGXATTRP()]    | [RESWITCH()]     |
| [RESWITCHALL()]  | [RESWITCHALLI()] | [RESWITCHI()]    |                  |

::: seealso
- [String functions]
- [regexp]
:::

# SQL functions
  These functions perform queries or other operations on an SQL database to which the MUSH is connected, if SQL support is available and enabled.

|               |               |               |               |
|---------------|---------------|---------------|---------------|
| [MAPSQL()]    | [SQL()]       | [SQLESCAPE()] |               |


# String functions
  String functions take at least one string and return a transformed string, parts of a string, or a value related to the string(s).

|                          |                          |                          |                          |
|--------------------------|--------------------------|--------------------------|--------------------------|
| [ACCENT()]               | [AFTER()]                | [ALIGN()]                | [ALPHAMAX()]             |
| [ALPHAMIN()]             | [ART()]                  | [BEFORE()]               | [BRACKETS()]             |
| [CAPSTR()]               | [CASE()]                 | [CASEALL()]              | [CAT()]                  |
| [CENTER()]               | [CHR()]                  | [COMP()]                 | [COND()]                 |
| [CONDALL()]              | [DECODE64()]             | [DECOMPOSE()]            | [DECOMPOSEWEB()]         |
| [DECRYPT()]              | [DIGEST()]               | [DISPLAYWIDTH()]         | [EDIT()]                 |
| [ENCODE64()]             | [ENCRYPT()]              | [ESCAPE()]               | [FLIP()]                 |
| [FOREACH()]              | [FORMDECODE()]           | [FORMQ()]                | [GRAPHEMECOUNT()]        |
| [GRAPHEMES()]            | [HMAC()]                 | [IF()]                   | [IFELSE()]               |
| [LCSTR()]                | [LEFT()]                 | [LIT()]                  | [LJUST()]                |
| [LPOS()]                 | [MERGE()]                | [MID()]                  | [ORD()]                  |
| [ORDINAL()]              | [POS()]                  | [PRINTF()]               | [REGEDIT()]              |
| [REGMATCH()]             | [RENDERMARKDOWN()]       | [RENDERMARKDOWNCUSTOM()] | [REPEAT()]               |
| [RIGHT()]                | [RJUST()]                | [SCRAMBLE()]             | [SECURE()]               |
| [SPACE()]                | [SPELLNUM()]             | [SQUISH()]               | [STRALLOF()]             |
| [STRCAT()]               | [STRDELETE()]            | [STRDISTANCE()]          | [STRFIRSTOF()]           |
| [STRINSERT()]            | [STRIPACCENTS()]         | [STRIPANSI()]            | [STRLEN()]               |
| [STRMATCH()]             | [STRREPLACE()]           | [SWITCH()]               | [TR()]                   |
| [TRIM()]                 | [UCSTR()]                | [URLDECODE()]            | [URLENCODE()]            |
| [WRAP()]                 |                          |                          |                          |

Boxes, titled rules, columns, labelled fields, trees, pictures, gauges, lists and tables that the web portal draws as real layout are [LAYOUT FUNCTIONS].

::: seealso
- [STRINGS]
- [LAYOUT FUNCTIONS]
:::
# Time functions
  These functions return times or format times.

|                 |                 |                 |                 |
|-----------------|-----------------|-----------------|-----------------|
| [CONVSECS()]    | [CONVTIME()]    | [CONVUTCSECS()] | [CONVUTCTIME()] |
| [CTIME()]       | [ETIME()]       | [ETIMEFMT()]    | [ISDAYLIGHT()]  |
| [MTIME()]       | [RESTARTTIME()] | [SECS()]        | [STARTTIME()]   |
| [STRINGSECS()]  | [TIME()]        | [TIMECALC()]    | [TIMEFMT()]     |
| [TIMESTRING()]  | [UPTIME()]      | [UTCTIME()]     |                 |

::: seealso
- [timezones]
:::
# Utility functions
  These functions don't quite fit into any other category.

|                    |                    |                    |                    |
|--------------------|--------------------|--------------------|--------------------|
| [@@()]             | [ALLOF()]          | [ANSI()]           | [ATRLOCK()]        |
| [BEEP()]           | [BENCHMARK()]      | [CHECKPASS()]      | [CLONE()]          |
| [CMDLINK()]        | [CREATE()]         | [DIE()]            | [DIG()]            |
| [ENDTAG()]         | [FIRSTOF()]        | [FN()]             | [FUNCTIONS()]      |
| [HTML()]           | [IBREAK()]         | [ILEV()]           | [INUM()]           |
| [ISDBREF()]        | [ISINT()]          | [ISNUM()]          | [ISOBJID()]        |
| [ISREGEXP()]       | [ISWORD()]         | [ITEXT()]          | [LETQ()]           |
| [LINK()]           | [LIST()]           | [LISTQ()]          | [LNUM()]           |
| [LOCALIZE()]       | [LSET()]           | [NULL()]           | [NUMVERSION()]     |
| [OBJEVAL()]        | [OPEN()]           | [PCREATE()]        | [R()]              |
| [RAND()]           | [RESTRICTEDEXPR()] | [S()]              | [SCAN()]           |
| [SET()]            | [SETQ()]           | [SETR()]           | [SLEV()]           |
| [SOUNDEX()]        | [SOUNDSLIKE()]     | [SPEAK()]          | [STEXT()]          |
| [SUGGEST()]        | [TAG()]            | [TAGWRAP()]        | [TEL()]            |
| [TESTLOCK()]       | [TEXTENTRIES()]    | [TEXTFILE()]       | [UNSETQ()]         |
| [UPTIME()]         | [VALID()]          | [WIPE()]           |                    |

# Wiki functions
  Wiki functions read the shared wiki: the same pages the web portal serves.

|                  |                  |                  |                  |
|------------------|------------------|------------------|------------------|
| [WIKI()]         | [WIKICATEGORY()] | [WIKILIST()]     | [WIKIRECENT()]   |
| [WIKISEARCH()]   | [WIKIACCESS()]   |                  |                  |

::: seealso
- [wiki]
:::

# @@()
# NULL()
`@@(<expression>)`<br>
`null(<expression>[, ... , <expression>])`

  The @@() function does nothing and returns nothing. It does not evaluate its argument. It could be used for commenting, perhaps.

  The null() function is similar, but does evaluate its argument(s), so side-effects can occur within a null(). Useful for eating the output of functions when you don't use that output.

::: seealso
- [@@]
:::

# ABS()
`abs(<number>)`

  Returns the absolute value of a number.

  Examples:
```sharp
say abs(-4)
You say, "4"
```

```sharp
> say abs(2)
You say, "2"
```

::: seealso
- [SIGN()]
:::

# ACCNAME()
`accname(<object>)`

  accname() returns the name of `<object>`, applying the object's<br>
  @nameaccent, if any.


::: seealso
- [NAME()]
- [FULLNAME()]
- [INAME()]
- [accents]
:::
# ACOS()
`acos(<cosine>[, <angle type>])`

  Returns the angle that has the given `<cosine>` (arc-cosine), with the angle expressed in the given `<angle type>`, or radians by default.

  See 'HELP ANGLES' for more on the `<angle type>`.


::: seealso
- [ASIN()]
- [ATAN()]
- [COS()]
- [CTU()]
- [SIN()]
- [TAN()]
:::
# ADD()
`add(<number1>, <number2>[, ... , <numberN>])`

  Returns the sum of the given numbers.


::: seealso
- [Math functions]
- [LMATH()]
:::
# AFTER()
`after(<string1>, <string2>)`

  Returns the portion of `<string1>` that occurs after `<string2>`. If `<string2>` isn't in `<string1>`, the function returns nothing. This is case-sensitive.

  Examples:
```sharp
> say after(foo bar baz,bar)
You say, " baz"
```
```sharp
> say after(foo bar baz,ba)
You say, "r baz"
```

::: seealso
- [BEFORE()]
- [REST()]
:::
# ALLOF()
`allof(<expr>[, ... , <exprN>], <osep>)`

  Evaluates every `<expr>` argument (including side-effects) and returns the results of those which are true, in a list separated by `<osep>`. The output separator argument is required, and can be a string of any length (including an empty string; use %b for a space).

  The meaning of true or false depends on configuration options as explained in the 'BOOLEAN VALUES' help topics.
```sharp
    > &s me=Bats are similar to Rats which are afraid of Cats
    > say allof(grab(v(s),rats),grab(v(s),mats),grab(v(s),bats),)
    You say, "Rats Bats"
```

```sharp
    > say allof(#-1,#101,#2970,,#-3,0,#319,null(This Doesn't Count),|)
    You say, "#101|#2970|#319"
```

```sharp
    > say allof(foo, 0, #-1, bar, baz,)
    You say, "foobarbaz"
```

```sharp
    > say allof(foo, 0, #-1, bar, baz,%b)
    You say, "foo bar baz"
```

::: seealso
- [FIRSTOF()]
- [boolean values]
- [STRFIRSTOF()]
- [FILTER()]
:::
# ALPHAMAX()
`alphamax(<word>[, ... , <wordN>])`

  Takes any number of `<word>` arguments, and returns the one which is lexicographically biggest. That is, the `<word>` would be last in alphabetical order.

  This is equivilent to ```last(sort(`<word>` ... `<wordN>`,a))```.

::: seealso
- [ALPHAMIN()]
- [MAX()]
:::
# ALPHAMIN()
`alphamin(<word>[, ... , <wordN>])`

  Takes any number of `<word>` arguments, and returns the one which is lexicographically smallest. That is, the word that would be first in alphabetical order.

  This is equivilent to first(sort(`<word>` ... `<wordN>`,a)).


::: seealso
- [ALPHAMAX()]
- [MIN()]
:::
# AND()
# CAND()
`and(<boolean1>, <boolean2>[, ... , <booleanN>])`<br>
`cand(<boolean1>, <boolean2>[, ... , <booleanN>])`

  These functions take any number of boolean values, and return 1 if all are true, and 0 otherwise. and() will always evaluate all its arguments (including side effects), while cand() stops evaluation after the first false argument.

  Prefer cand(): it skips work the answer no longer needs, and a later argument can rely on the earlier ones being true. Use and() only when every argument has a side effect that must run.


::: seealso
- [boolean values]
- [NAND()]
- [OR()]
- [XOR()]
- [NOT()]
- [LMATH()]
:::
# ANDFLAGS()
# ANDLFLAGS()
`andflags(<object>, <string of flag letters>)`<br>
`andlflags(<object>, <list of flag names>)`

  These functions return 1 if `<object>` has all of the given flags, and 0 if it does not. andflags() takes a string of single flag letters, while andlflags() takes a space-separated list of flag names. In both cases, a ! before the flag means "not flag".

  If there is a syntax error like a ! without a following flag, '#-1 INVALID FLAG' is returned. Unknown flags are treated as being not set.

  Example: Check to see if %# is set Wizard and Dark, but not Ansi.<br>
    > say andflags(%#, WD!A)<br>
    > say andlflags(%#, wizard dark !ansi)


::: seealso
- [ORFLAGS()]
- [FLAGS()]
- [LFLAGS()]
:::
# ANDLPOWERS()
`andlpowers(<object>, <list of powers>)`

  This function returns 1 if `<object>` has all the powers in a specified list, and 0 if it does not. The list is a space-separated list of power names. A '!' preceding a flag name means "not power".

  Thus, ANDLPOWERS(me, no_quota no_pay) would return 1 if I was powered no_quota and no_pay. ANDLPOWERS(me, poll !guest) would return 1 if I had the poll power but not the guest power.

  If there is a syntax error like a ! without a following flag, '#-1 INVALID POWER' is returned. Unknown powers are treated as being not set.


::: seealso
- [POWERS()]
- [ORLPOWERS()]
- [@power]
- [@power]
:::
# APOSS()
# %a
`aposs(<object>)`

  Returns the absolute possessive pronoun - his/hers/its/theirs - for an object. The %a substitution returns the absolute possessive pronoun of the enactor.


::: seealso
- [OBJ()]
- [POSS()]
- [SUBJ()]
:::
# ART()
`art(<string>)`

  This function returns the proper article, "a" or "an", based on whether or not `<string>` begins with a vowel.
# ASIN()
`asin(<sine>[, <angle type>])`

  Returns the angle with the given `<sine>` (arc-sine), with the angle expressed in the given `<angle type>`, or radians by default.

  See 'HELP ANGLES' for more on the angle type.


::: seealso
- [ACOS()]
- [ATAN()]
- [COS()]
- [CTU()]
- [SIN()]
- [TAN()]
:::
# ATAN()
# ATAN2()
`atan(<tangent>[, <angle type>])`<br>
`atan2(<number1>, <number2>[, <angle type>])`

  Returns the angle with the given `<tangent>` (arc-tangent), with the angle expressed in the given `<angle type>`, or radians by default.

  atan2(x, y) is like atan(fdiv(x, y)), except y can be 0, and the signs of both arguments are used in determining the sign of the result. It is useful in converting between cartesian and polar coordinates.

  See 'HELP ANGLES' for more on the angle type.


::: seealso
- [ACOS()]
- [ASIN()]
- [COS()]
- [CTU()]
- [SIN()]
- [TAN()]
:::
# ATRLOCK()
# ATTRLOCK()
`atrlock(<object>/<attrib>[, [on|off]])`

  When given a single `<object>`/`<attribute>` pair as an argument, returns 1 if the attribute is locked, 0 if unlocked, and #-1 if the attribute doesn't exist or can't be read by the function's caller.

  When given a second argument of "on" (or "off"), attempts to lock (or unlock) the specified attribute, as per @atrlock.

  A locked attribute is one which has the "locked" attribute flag, so this function is roughly equivilent to:

`hasattr(<object>/<attrib>, locked)`<br>
`set(<object>/<attribute>, [!]locked)`

  except that the attribute's owner is also changed when you lock it via atrlock().


::: seealso
- [@atrlock]
- [@atrchown]
- [HASFLAG()]
:::
# ATTRIB_SET()
`attrib_set(<object>/<attrib>[, <value>])`

  Sets or clears an attribute. With a `<value>`, it sets the attribute, without one, it clears the attribute. This is an easier-to-read replacement for the old set(`<object>`, `<attrib>`:`<value>`) notation, and a less destructive replacement for wipe() that won't destroy entire attribute trees in one shot.

  If there is a second argument, then attrib_set() will create an attribute, even if the second argument is empty (in which case attrib_set() will create an empty attribute). If the empty_attrs configuration option is off, the attribute will be set to a single space. This means that attrib_set(me/foo,%0) will _always_ create an attribute.


::: seealso
- [SET()]
- [@set]
- [ATTRIB_SET#()]
:::
# ATTRIB_SET#()
`attrib_set#(<object>/<attrib>[, <value>])`

  Sets or clears an attribute exactly as [ATTRIB_SET()] does, and returns `<object>`'s name followed by the `<object>`/`<attribute>` pair it was given, rather than the empty string. Use it when the calling code wants to report what it just set. On failure it returns the same error attrib_set() would.

  This is a SharpMUSH function; PennMUSH has no attrib_set#().

  **It cannot currently be called.** The parser's function-name token does not admit `#`, so
  `attrib_set#(me/foo, bar)` is never recognised as a call and the text is returned unchanged. Use
  [ATTRIB_SET()] until that is fixed.


::: seealso
- [ATTRIB_SET()]
- [SET()]
:::
# BAND()
`band(<integer>[, ... , <integerN>])`

  Does a bitwise AND of all its arguments, returning the result (a number with only the bits set in every argument set in it).


::: seealso
- [Bitwise functions]
- [LMATH()]
:::
# BASECONV()
`baseconv(<number>, <from base>, <to base>)`

  Converts `<number>`, which is in base `<from base>` into base `<to base>`. The bases can be between 2 (binary) and 64, inclusive.

  Numbers 36 and under use the standard numbers:

  "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"

  All bases over 36 use base64 url string:

  "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"

  In base 63 and base 64, - is always treated as a digit. Using base64 as a 'from' will also treat + as 62 and / as 63.
# BEEP()
`beep([<number>])`

  Returns `<number>` "alert" bell characters. `<number>` must be in the range 1 to 5, or, if unspecified, defaults to 1. This function may only be used by royalty and wizards.

# BEFORE()
`before(<string1>, <string2>)`

  Returns the portion of `<string1>` that occurs before `<string2>`. If `<string2>` isn't in `<string1>`, `<string1>` is returned. This is case-sensitive.

  Examples:
```sharp
say before(foo bar baz,bar)
You say, "foo"
say before(foo bar baz,r)
You say, "foo ba"
say before(foo bar baz,a)
You say, "foo b"
```


::: seealso
- [AFTER()]
- [FIRST()]
:::
# BENCHMARK()
`benchmark(<expression>, <number>[, <sendto>])`

  Evaluates `<expression>` `<number>` times, and returns the average, minimum, and maximum time it took to evaluate `<expression>` in microseconds. If a `<sendto>` argument is given, benchmark() instead pemits the times to the object `<sendto>`, and returns the result of the last evaluation of `<expression>`.

  Example:
```sharp
think benchmark(iter(lnum(1,100), ##), 200)
Average: 520.47   Min: 340   Max: 1382
think benchmark(iter(lnum(1,100), %i0), 200)
Average: 110.27   Min: 106   Max: 281
```
# BRACKETS()
`brackets(<string>)`

  Returns a count of the number of left and right square brackets, parentheses, and curly braces in the string, in that order, as a space-separated list of numbers. This is useful for finding missing or extra brackets in MUSH code. `<string>` is evaluated.

  Example:
```sharp
@desc me=This is [ansi(h,a test)] of the { brackets() function.
think brackets(v(desc))
1 1 2 2 1 0
```
# BNAND()
`bnand(<integer1>, <integer2>)`

  Returns `<integer1>` with every bit that was set in `<integer2>` cleared.


::: seealso
- [Bitwise functions]
:::
# BNOT()
`bnot(<integer>)`

  Returns the bitwise complement of `<integer>`. Every bit set in it is cleared, and every clear bit is set.


::: seealso
- [Bitwise functions]
:::
# BOR()
`bor(<integer>[, ... , <integerN>])`

  Does a bitwise OR of all its arguments, returning the result. (A number with a bit set if that bit appears in any of its arguments).


::: seealso
- [Bitwise functions]
- [LMATH()]
:::
# BOUND()
`bound(<number>, <lower bound>[, <higher bound>])`

  bound() returns `<number>` if it is between `<lower bound>` and `<higher bound>`. If it's lower than `<lower bound>`, `<lower bound>` is returned. If it's higher than `<higher bound>`, `<higher bound>` is returned.

  If you just want to know whether `<number>` is within the range of `<lower>` to `<higher>`, consider using lte(`<lower>`, `<number>`, `<higher>`) instead to get a boolean result.


::: seealso
- [ROUND()]
- [ROUND()]
- [ROUND()]
- [TRUNC()]
:::
# BXOR()
`bxor(<integer>[, ... , <integerN>])`

  Does a bitwise XOR of all its arguments, returning the result. (A number with a bit set if it's set in only one of its arguments).


::: seealso
- [Bitwise functions]
- [LMATH()]
:::
# CAPSTR()
`capstr(<string>)`

  Returns `<string>` with the first character capitalized.

  Example:
```sharp
think capstr(foo bar baz)
Foo bar baz
```


::: seealso
- [LCSTR()]
- [UCSTR()]
:::
# CAT()
# STRCAT()
`cat(<string>[, ... , <stringN>])`<br>
`strcat(<string1>[, ... , <stringN>])`

  These functions concatenate multiple strings together. cat() adds a space between each string; strcat() does not.

  Example:
```sharp
say cat(foo bar, baz blech)
You say, "foo bar baz blech"
say strcat(foo bar, baz blech)
You say, "foo barbaz blech"
```
# CENTER()
`center(<string>, <width>[, <fill>[, <rightfill>]])`

  This function will center `<string>` within a field `<width>` characters wide, using the `<fill>` string for padding on the left side of the string, and `<rightfill>` for padding on the right side. `<rightfill>` defaults to the mirror-image of `<fill>` if not specified. `<fill>` defaults to a space if neither `<fill>` nor `<rightfill>` are specified.

  If `<string>` divides `<width>` into uneven portions, the left side will be one character shorter than the right side.

  Examples:
```sharp
say center(X,5,-)
You say, "--X--"
```

    > say center(X,5,-=)<br>
    You say, "-=X=-"

    > say center(.NEAT.,15,-,=)<br>
    You say, "----.NEAT.====="

    > say center(hello,16,12345)<br>
    You say, "12345hello543215"


::: seealso
- [align()]
- [LJUST()]
- [RJUST()]
:::
# CHAIN()
`chain(<attribute list>, <base>[, <arg0>[, ... , <argN>]])`

  chain() threads a value through a sequence of user-defined attributes -- the functional "pipeline", or thread-first "arrow" (as in Clojure's `->`), pattern. `<attribute list>` is a space-separated list of `[<object>/]<attribute>` names.

  The `<base>` value is passed as %0 to the first attribute. The result of each attribute is then passed as %0 to the next, threading one accumulating value along the whole list. Any extra `<arg0>, <arg1>, ...` are passed unchanged as %1, %2, ... to *every* attribute, so each step can reach the same side inputs. The result of the last attribute is returned. If `<attribute list>` is empty, `<base>` is returned unchanged.

  A step may call ibreak() to short-circuit the pipeline: the remaining attributes are skipped, and the value produced by that step is returned. chain() counts as an iteration level, so itext(0) and inum(0) inside a step give the running value and the step number.

  Each attribute is evaluated as by ufun(). Object names in the list may not contain spaces (use "me" or a dbref), since spaces separate the attributes.

  chain() is close kin to fold(): fold() walks a list of *data*, passing each element in turn as %0; chain() walks a list of *attributes*, passing one threaded result as %0 while carrying the same optional arguments to each step.

  Example -- thread a string through two steps (%0 is the running value, %1 the shared side-arg):
```sharp
> &WRAP me=%1%0%1
> &SHOUT me=ucstr(%0)!
> say chain(WRAP SHOUT, hello, *)
You say, "*HELLO*!"
```

  WRAP wraps the base "hello" in the side-arg "*" to make "*hello*"; that result becomes %0 for SHOUT, which upper-cases it and appends "!".


::: seealso
- [fold()]
- [MAP()]
- [iter()]
- [IBREAK()]
- [JITER()]
- [u()]
- [@include attribute pipelines]
:::
# CHECKPASS()
`checkpass(<player>, <string>)`

  Returns 1 if `<string>` matches `<player>`'s password, and 0 otherwise. If `<player>` has no password, this function will always return 1.

  This function can only be used by wizards.


::: seealso
- [@password]
- [@newpassword]
:::
# CHR()
# ORD()
`chr(<number>)`<br>
`ord(<character>)`

  ord() returns the numerical value of the given character. chr() returns the character with the given numerical value.

  chr() refuses control characters (0-31 and 127-159) with #-1 UNPRINTABLE CHARACTER. Unlike PennMUSH, it accepts any Unicode code point, not just 0-255.

  Examples:
```sharp
say ord(A)
You say, "65"
say chr(65)
You say, "A"
```
# CLONE()
`clone(<object>[, <new name>[, <dbref>[, preserve]]])`

  This function clones `<object>`, as per @clone, and returns the dbref number of the clone, or #-1 if the object could not be cloned.

  The clone will have the same name as the original object unless you give a `<new name>` for it. Normally, the clone will be created with the first available dbref, but wizards and objects with the pick_dbref power may give the `<dbref>` of a garbage object to use instead.

  If the optional fourth argument is the string preserve, acts as @clone/preserve.<br>
  Note: If @create or @clone is restricted or disabled, clone() will also be restricted/disabled.


::: seealso
- [@clone]
- [CREATE()]
- [DIG()]
- [OPEN()]
:::
# CMDS()
`cmds(<player|descriptor>)`

  Returns the number of commands issued by a player during this connection as indicated by WHO.

  You must be a Wizard, Royalty or See_All to use this function on anyone but yourself.


::: seealso
- [Connection functions]
:::
# SENT()
`sent(<player|descriptor>)`

  Returns the number of characters sent by a player during this connection as indicated by SESSION.

  You must be a Wizard, Royalty or See_All to use this function on anyone but yourself.


::: seealso
- [Connection functions]
:::
# RECV()
`recv(<player|descriptor>)`

  Returns the number of characters received by a player during this connection as indicated by SESSION.

  You must be a Wizard, Royalty or See_All to use this function on anyone but yourself.


::: seealso
- [Connection functions]
:::
# COMP()
`comp(<value1>, <value2>[, <type>])`

  comp() compares two values. It returns 0 if they are the same, -1 if `<value1>` is less than/precedes alphabetically `<value2>`, and 1 otherwise.

  By default the comparison is a case-sensitive lexicographic (string) comparison. By giving the optional `<type>`, the comparison can be specified:

      `<type>`            Comparison<br>
        A               Maybe case-sensitive lexicographic (default)<br>
        I               Always case-insensitive lexicographic<br>
        D               Dbrefs of valid objects<br>
        N               Integers<br>
        F               Floating point numbers

  Whether or not the a sort type is case-sensitive or not depends on the particular MUSH and its environment.

::: seealso
- [STRMATCH()]
- [EQ()]
:::
# CON()
`con(<object>)`

  Returns the dbref of the first object in the `<object>`'s inventory.

  You can get the complete contents of any container you may examine, regardless of whether or not objects are dark. You can get the partial contents (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the contents of anything else, regardless of whether or not you have objects in it.


::: seealso
- [LCON()]
- [NEXT()]
:::
# COND()
# CONDALL()
# NCOND()
# NCONDALL()
`cond(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`<br>
`condall(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`<br>
`ncond(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`<br>
`ncondall(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`

  cond() evaluates `<cond>`s until one returns a true value. Should none return true, `<default>` is returned.

  condall() returns all `<expr>`s for those `<cond>`s that evaluate to true, or `<default>` if none are true.

  ncond() and ncondall() are identical to cond(), except it returns `<expr>`s for which `<cond>`s evaluate to false.

  Examples:
```sharp
say cond(0,This is false,#-1,This is also false,#123,This is true)
You say, "This is true"
```

    > say ncond(0,This is false,#-1,This is also false,#123,This is true)<br>
    You say, "This is false"

    > say ncondall(0,This is false,#-1,This is also false,#123,This is true)<br>
    You say, "This is falseThis is also false"


::: seealso
- [FIRSTOF()]
- [ALLOF()]
:::
# CONFIG()
`config([<option>])`

  With no arguments, config() returns a list of config option names. If `<option>` is given, config() returns the value of the given option Boolean configuration options will return values of "Yes" or "No".

  Example:
```sharp
think config(money_singular)
Penny
```

# CONN()
`conn(<player|descriptor>)`

  This function returns the number of seconds a player has been connected. `<player>` should be the full name of a player or a dbref. You can also use a `<descriptor>` to get connection information for a specific connection when a player is connected more than once. Wizards can also specify the descriptor of a connection which is still at the login screen.

  This function returns -1 for invalid `<player|descriptor>`s, offline players and players who are dark, if the caller is not able to see them.


::: seealso
- [Connection functions]
:::
# CONTROLS()
`controls(<object>, <victim>[/<attribute>])`

  With no `<attribute>`, this function returns 1 if `<object>` controls `<victim>`, or 0, if it does not. With an `<attribute>`, it will return 1 if `<object>` could successfully set `<attribute>` on `<victim>` (or alter `<attribute>`, if it already exists). If one of the objects does not exist, it will return #-1 ARGN NOT FOUND (where N is the argument which is the invalid object). If `<attribute>` is not a valid attribute name, it will return #-1 BAD ATTR NAME. You must control `<object>` or `<victim>`, or have the See_All power, to use this function.


::: seealso
- [VISIBLE()]
- [CONTROL]
:::
# CONVSECS()
# CONVUTCSECS()
`convsecs(<seconds>[, <timezone>])`<br>
`convutcsecs(<seconds>)`

  This function converts `<seconds>` (the number of seconds which have elapsed since midnight on January 1, 1970 UTC) to a time string. Because it's based on UTC, but returns local time, convsecs(0) is not going to be "Thu Jan 1 00:00:00 1970" unless you're in the UTC (GMT) timezone.

  If a `<timezone>` argument is given, the return value is based on that timezone instead of the MUSH server's local time. See [timezones] for more information on valid timezones.

  If Extended convtime() is supported (see @config compile), negative values for `<seconds>` representing dates prior to 1970 are allowed.

  convutcsecs(`<seconds>`) is an alias for convsecs(`<seconds>`, utc).

  Examples:
```sharp
say secs()
You say, "709395750"
```
```sharp
    > say convsecs(709395750)
    You say, "Wed Jun 24 10:22:54 1992"
```
```sharp
    > say convutcsecs(709395750)
    You say, "Wed Jun 24 14:22:30 1992"
```

::: seealso
- [CONVTIME()]
- [time()]
- [timefmt()]
:::
# CONVTIME()
# CONVUTCTIME()
`convtime(<time string>[, <timezone>[, <precision>]])`<br>
`convutctime(<time string>[, <precision>])`

  This functions converts a time string to the number of seconds since Jan 1, 1970 GMT. A time string is of the format:<br>
      Ddd MMM DD HH:MM:SS YYYY<br>
  where Ddd is the day of the week, MMM is the month, DD is the day of the month, HH is the hour in 24-hour time, MM is the minutes, SS is the seconds, and YYYY is the year. If you supply an incorrectly formatted string, it will return #-1.

  convutctime() and convtime() with a second argument of 'utc' assume the timestring is based on UTC time. Other time zones can be specified too. If no `<timezone>` is given, the server's timezone is used.

  If the extended convtime() is supported (See @config compile), more formats for the date are enabled, including ones missing the day of week and year, and a 'Month Day Year' format. In this case, convtime() can also handle dates prior to 1970 (in which case a negative number will be returned).

  Example:
```sharp
say time()
You say, "Wed Jun 24 10:22:54 1992"
```

    > say convtime(Wed Jun 24 10:22:54 1992)<br>
    You say, "709395774"


::: seealso
- [CONVSECS()]
- [time()]
- [timezones]
:::
# COS()
`cos(<angle>[, <angle type>])`

  Returns the cosine of `<angle>`. Angle must be in the given angle type, or radians by default.

  Examples:
```sharp
say cos(90, d)
You say, "0"
```
```sharp
    > say cos(1.570796)
    You say, "0"
```

  See 'HELP ANGLES' for more on the angle type.

::: seealso
- [ACOS()]
- [ASIN()]
- [ATAN()]
- [CTU()]
- [SIN()]
- [TAN()]
:::
# PCREATE()
`pcreate(<name>, <password>[, <dbref>])`

  Creates a player with a given `<name>` and `<password>`. This function can only be used by wizards.

  The optional third argument can be used to specify a garbage object to use for the new player.


::: seealso
- [@pcreate]
- [CREATE()]
- [DIG()]
- [OPEN()]
:::
# CREATE()
`create(<object>[, <cost>[, <dbref>]])`

   This function creates an object with name `<object>` for `<cost>` pennies, and returns the dbref number of the created object. It returns #-1 on error.

   Wizards may also specify a `<dbref>`; if this refers to a garbage object, the new object is created with this dbref.


::: seealso
- [@create]
- [PCREATE()]
- [DIG()]
- [OPEN()]
:::
# CTIME()
# CSECS()
`ctime(<object>[, <utc>])`<br>
`csecs(<object>[, <precision>])`

  ctime() returns the date and time that `<object>` was created. The time returned is in the server's local timezone, unless `<utc>` is true, in which case the time is in the UTC timezone.

  csecs() returns the time as the number of seconds since the epoch. Anyone can get the creation time of any object in the game.


::: seealso
- [MTIME()]
- [time()]
- [SECS()]
- [OBJID()]
:::
# ANGLES

  In any function which accepts an angle type, the argument can be one of 'd' for degrees, 'r' for radians, or 'g' for gradians. Gradians are not used often, but it's included for completeness.

  As a refresher, there are 180 degrees in pi radians in 200 gradians.


::: seealso
- [ACOS()]
- [ASIN()]
- [ATAN()]
- [COS()]
- [CTU()]
- [SIN()]
- [TAN()]
:::
# CTU()
`ctu(<angle>, <from>, <to>)`

  Converts between the different ways to measure angles. `<from>` controls what the angle is treated as, and `<to>` what form it is turned into. See HELP ANGLES for more information.

  Example:
```sharp
say 90 degrees is [ctu(90, d, r)] radians
You say, "90 degrees is 1.570796 radians"
```


::: seealso
- [ACOS()]
- [ASIN()]
- [ATAN()]
- [COS()]
- [SIN()]
- [TAN()]
:::
# DEC()
`dec(<integer>)`<br>
`dec(<string-ending-in-integer>)`

  dec() returns the given `<integer>` minus 1. If given a string that ends in an integer, it decrements only the final integer portion. That is:

```sharp
    > think dec(3)
    2
```
```sharp
    > think dec(hi3)
    hi2
```
```sharp
    > think dec(1.3.3)
    1.3.2
```
```sharp
    > think dec(1.3)
    1.2
```

  Note especially the last example, which will trip you up if you use floating point numbers with dec() and expect it to work like sub().

  If the null_eq_zero @config option is on, using dec() on a string which does not end in an integer will return `<string>`-1. When null_eq_zero is turned off, it will return an error.


::: seealso
- [INC()]
- [SUB()]
:::
# DECOMPOSEWEB()
`decomposeweb(<string>)`

  Returns `<string>` as HTML, ready to place in a web page. Characters such as `<` and `&` in the text are encoded, and colour, links, tags and other markup are written as the HTML the web portal uses for them.

  Example:
```sharp
think decomposeweb(a<b> [ansi(hr,red)])
a&lt;b&gt; <span style="color: #ff5555">red</span>
```

  This is a SharpMUSH function; PennMUSH has no decomposeweb().


::: seealso
- [DECOMPOSE()]
- [ansi()]
- [RENDER()]
:::
# DECOMPOSE()
`decompose(<string>)`

  decompose() works like escape() with the additional caveat that it inserts parse-able characters to recreate `<string>` exactly after one parsing. It takes care of multiple spaces, '%r's, and '%t's.

  Example:
```sharp
think decompose(This is \[a [ansi(y,test)]\][space(3)])
This is \[a%b[ansi(y,test)]\] %b%b
```


::: seealso
- [@decompile output switches]
- [ESCAPE()]
- [SECURE()]
:::
# DEFAULT()
`default([<obj>/]<attr>[, ... ,[<objN>]/<attrN>], <default>)`

  This function returns the value of the first possible `<obj>`/`<attr>`, as if retrieved via the get() function, if the attribute exists and is readable by you. Otherwise, it evaluates `<default>`, and returns that. Note that `<default>` is only evaluated if none of the given attributes exist or can be read. Note further than an empty attribute counts as an existing attribute.

  This is useful for code that needs to return the value of an attribute, or an error message or default case, if that attribute does not exist.

  Examples:
```sharp
&TEST me=apple orange banana
say default(me/Test, No fruits!)
You say "apple orange banana"
```
```sharp
    > &TEST ME
    > say default(me/Test, No fruits!)
    You say "No fruits!"
```

::: seealso
- [GET()]
- [HASATTR()]
- [u()]
- [EDEFAULT()]
- [UDEFAULT()]
- [UDEFAULT()]
- [STRFIRSTOF()]
:::
# STRDELETE()
# DELETE()
`strdelete(<string>, <first>, <len>)`

  Return a modified `<string>`, with `<len>` characters starting after the character at position `<first>` deleted. In other words, it copies `<first>` characters, skips `<len>` characters, and then copies the remainder of the string. If `<len>` is negative, deletes characters leftwards from `<first>`. Characters are numbered starting at 0.

  Examples:
```sharp
say strdelete(abcdefgh, 3, 2)
You say, "abcfgh"
```
```sharp
    > say strdelete(abcdefgh, 3, -2)
    You say, "abcdefgh"
```
  A negative `<len>` deletes nothing. PennMUSH's own help claims it deletes backwards from
  `<first>`, but fun_delete leaves the count negative and ansi_string_delete returns early on it,
  so 1.8.8 answers the string untouched; SharpMUSH follows the code.

  A `<first>` past the end of the string, and a `<len>` of zero, likewise answer the string
  untouched. A negative `<first>` is `#-1 OUT OF RANGE`.

  delete() is an alias for strdelete(), for backwards compatability.


::: seealso
- [STRREPLACE()]
- [STRINSERT()]
- [MID()]
- [LDELETE()]
:::
# DIE()
`die(<number of times to roll die>, <number of sides on die>[, <show>])`

  This function simulates rolling dice. It "rolls" a die with a given number of sides, a certain number of times, and adds the results. For example, DIE(2, 6) would roll "2d6" - two six-sided dice, generating a result in the range 2-12. The maximum number of dice this function will roll in a single call is 700. If `<show>` is true, the result will be a space-seperated list of the individual rolls rather than their sum.

  Examples:
```sharp
think die(3, 6)
6
think die(3, 6, 1)
5 2 1
```

::: seealso
- [RAND()]
:::
# DIG()
`dig(<name>[, <exit to>[, <exit from>[, <room dbref>, <to dbref>, <from dbref>]]])`

  This function digs a room called `<name>`, and optionally opens and links `<exit to>` and `<exit from>`, like the normal @dig command. It returns the dbref number of the new room.

  Wizards and objects with the pick_dbref power can supply optional fourth through sixth arguments to specify garbage objects to use for the new room and exits.


::: seealso
- [@dig]
- [OPEN()]
- [@open]
- [CREATE()]
- [PCREATE()]
:::
# DIGEST()
# MD5
# SHA1
# CHECKSUM
# HASH
`digest(list)`<br>
`digest(<algorithm>, <string>)`

  Returns a checksum (hash, digest, etc.) of `<string>` using the given `<algorithm>`. The result is a unique large number represented in base 16.

  Typically at least the following algorithms are supported:

  md4 md5 ripemd160 sha1 sha224 sha256 sha384 sha512 whirlpool

  Depending on the host's OpenSSL version and how it was configured, there might be more (or less) available. digest(list) returns the methods a particular server understands if the OpenSSL library version being used is recent enough (1.0.0 and higher), or '#-1 LISTING NOT SUPPORTED' on older versions. For portable code, stick with MD5, SHA1 and the SHA2 family.

  Example:
```sharp
think iter(digest(list), %i0(foo) => [digest(%i0, foo)], %b, %r)
...
MD4(foo) => 0ac6700c491d70fb8650940b1ca1e4b2
MD5(foo) => acbd18db4cc2f85cedef654fccc4a4d8
MDC2(foo) => 5da2a8f36bf237c84fddf81b67bd0afc
RIPEMD160(foo) => 42cfa211018ea492fdee45ac637b7972a0ad6873
SHA1(foo) => 0beec7b5ea3f0fdbc95d0dd47f3c5bc275da8a33
SHA224(foo) => 0808f64e60d58979fcb676c96ec938270dea42445aeefcd3a4e6f8db
...
```


::: seealso
- [ENCODE64()]
- [ENCRYPT()]
- [HMAC()]
:::
# DIST2D()
`dist2d(<x1>, <y1>, <x2>, <y2>)`

  Returns the distance between two points in the Cartesian plane that have coordinates (`<x1>`, `<y1>`) and (`<x2>`, `<y2>`).


::: seealso
- [DIST3D()]
- [LMATH()]
:::
# DIST3D()
`dist3d(<x1>, <y1>, <z1>, <x2>, <y2>, <z2>)`

  Returns the distance between two points in space, with coordinates (`<x1>`, `<y1>`, `<z1>`) and (`<x2>`, `<y2>`, `<z2>`).


::: seealso
- [DIST2D()]
- [LMATH()]
:::
# DIV()
# FLOORDIV()
# FDIV()
`div(<number1>, <number2>[, ... , <numberN>])`<br>
`fdiv(<number1>, <number2>[, ... , <numberN>])`<br>
`floordiv(<number1>, <number2>[, ... , <numberN>])`

  These functions divide `<number1>` by `<number2>` (and, for each subsequent argument, divide the previous result by `<numberN>`) and return the final result.

  div() returns the integer part of the quotient. floordiv() returns the largest integer less than or equal to the quotient; for positive numbers, they are identical, but for negative numbers they may differ. fdiv() returns the floating-point quotient.

  Examples:
```sharp
   div(13,4)          ==>   3      and     floordiv(13,4)     ==>   3
   div(-13,4)         ==>  -3      but     floordiv(-13,4)    ==>  -4
   div(13,-4)         ==>  -3      but     floordiv(13,-4)    ==>  -4
   div(-13,-4)        ==>   3      and     floordiv(-13,-4)   ==>   3

   fdiv(13,4)         ==>  3.25            fdiv(-13,4)        ==> -3.25
   fdiv(13,-4)        ==>  -3.25           fdiv(-13,-4)       ==>  3.25
```

  Note that add(mul(div(%0,%1),%1),remainder(%0,%1)) always yields %0, and add(mul(floordiv(%0,%1),%1),modulo(%0,%1)) also always yields %0.


::: seealso
- [MOD()]
- [LMATH()]
:::
# DOING()
`doing(<player|descriptor>)`

  When given the name of a player or descriptor, doing() returns the player's @doing. If no matching player or descriptor is found, or the descriptor is not yet connected to a player, an empty string is returned.


::: seealso
- [@poll]
- [@doing]
- [POLL()]
:::
# E()
# EVERY()
# SOME()
`every([<object>/]<attribute>, <list>[, <delimiter>[, <register>]])`<br>
`some([<object>/]<attribute>, <list>[, <delimiter>[, <register>]])`

  These functions evaluate `<attribute>` as a boolean predicate against each element of `<list>` (the element is passed as %0), and return 1 or 0. An element passes when the result is boolean-true, by the same rule as filterbool().

  every() returns 1 if EVERY element passes, and 0 otherwise. An empty list is vacuously true: every() returns 1.

  some() returns 1 if ANY element passes, and 0 otherwise. An empty list returns 0.

  If `<register>` is given, the q-register of that name is set to the delimiter-joined list of the elements that did NOT pass the predicate (an empty string when none failed), the same reject-capture convention as filterq(). Without a register, evaluation short-circuits (every() stops at the first failure, some() at the first success); requesting a register evaluates the whole list so every failure is collected.

  Example: validate input and name the offenders:
```sharp
> &ISNUM me=isnum(%0)
> think [every(ISNUM, 12 apples 7 pears, , bad)]: %q<bad>
0: apples pears
```

  The same shape works as a command guard: `@assert every(ISNUM, %0, , bad)=@pemit %#=Not numbers: %q<bad>`


::: seealso
- [FILTER()]
- [FILTER()]
- [FILTERQ()]
- [setq()]
- [CHAIN()]
:::
# EXP()
`e([<number>])`

  With no argument, returns the value of "e" (2.71828182845904523536, rounded to the game's float_precision setting).

  If a `<number>` is given, it returns e to the power of `<number>`.

  exp() is an alias for e().

::: seealso
- [POWER()]
- [LOG()]
:::
# EDEFAULT()
`edefault([<obj>/]<attr>, <default case>)`

  This function returns the evaluated value of `<obj>`/`<attr>`, as if retrieved via the get_eval() function, if the attribute exists and is readable by you. Otherwise, it evaluates `<default case>`, and returns that. `<default case>` is only evaluated if the attribute does not exist or cannot be read.

  Example:
```sharp
&TEST me=You have lost [rand(10)] marbles.
say edefault(me/Test,You have no marbles.)
You say "You have lost 6 marbles."
```

    > &TEST me<br>
    > say edefault(me/Test,You have no marbles.)<br>
    You say "You have no marbles."


::: seealso
- [GET()]
- [EVAL()]
- [u()]
- [DEFAULT()]
- [UDEFAULT()]
- [HASATTR()]
:::
# EDIT()
`edit(<string>, <search>, <replace>[, ... , <searchN>, <replaceN>])`

  For each given `<search>` and `<replace>` pair, edit() replaces all occurrences of `<search>` in `<string>` with the corresponding `<replace>`.

  If `<search>` is a caret (^), `<replace>` is prepended.<br>
  If `<search>` is a dollar sign ($), `<replace>` is appended.<br>
  If `<search>` is an empty string, `<replace>` is inserted between every character, and before the first and after the last.<br>
  If `<replace>` is an empty string, `<search>` is deleted from the string.

  Example:
```sharp
say edit(this is a test,^,I think%b,$,.,a test,an exam)
You say "I think this is an exam."
```

  edit() can not replace a literal single ^ or $. Use regedit() for that.


::: seealso
- [@edit]
- [REGEDIT()]
:::
# ELEMENTS()
`elements(<list of words>, <list of numbers>[, <delim>[, <osep>]])`

  This function returns the words in `<list of words>` that are in the positions specified by `<list of numbers>`. The `<list of words>` are assumed to be space-separated, unless a `<delim>` is given. If `<osep>` is given, the matching words are separated by `<osep>`, otherwise by `<delim>`.

  If any of the `<list of numbers>` is negative, it counts backwards from the end of the list of words, with -1 being the last word, -2 the word before last, and so on.

  Examples:
```sharp
say elements(Foo Ack Beep Moo Zot,2 4)
You say "Ack Moo"
```

    > say elements(Foof|Ack|Beep|Moo,3 1,|)<br>
    You say "Beep|Foof"

    > say elements(The last word is foo, -1)<br>
    You say "foo"


::: seealso
- [EXTRACT()]
- [INDEX()]
- [GRAB()]
:::
# ELOCK()
`elock(<object>[/<locktype>], <victim>)`

  elock() returns 1 if the `<victim>` would pass the @lock/`<locktype>` on `<object>`, and 0 if it would fail. Any locktype can be given, including user-defined "user:" @locks. If no `<locktype>` is given, it defaults to the Basic lock.

  You must be able to examine the lock, which means either that you must control `<object>`, it must be @set VISUAL, or the `<locktype>` lock must be @lset VISUAL.

  Examples:
```sharp
@lock/drop Dancing Slippers=#0
think elock(Dancing Slippers/drop, Princess)
0
```

    > @lock/user:test map==*Fred|=*George<br>
    > think elock(map/test,*Snape)<br>
    0


::: seealso
- [LOCKING]
- [locktypes]
- [TESTLOCK()]
- [LOCKFILTER()]
- [@lset]
:::
# EMIT()
# NSEMIT()
`emit(<message>)`<br>
`nsemit(<message>)`

  Sends a message to the room, as per @emit.

  nsemit() works like @nsemit.


::: seealso
- [PEMIT()]
- [REMIT()]
- [NSLEMIT()]
- [OEMIT()]
- [ZEMIT()]
:::
# ENCODE64()
# DECODE64()
# base64
`encode64(<string>)`<br>
`decode64(<string>)`

  encode64() returns `<string>` encoded using base-64 format.

  decode64() converts a base-64 encoded `<string>` back to its original form.


::: seealso
- [ENCRYPT()]
- [DIGEST()]
:::
# ENCRYPT()
# DECRYPT()
`encrypt(<string>, <password>[, <encode>])`<br>
`decrypt(<string>, <password>[, <encoded>])`

  encrypt() returns an encrypted string produced by a simple password-based encrypted algorithm. Good passwords are long passwords. This is not high-security encryption.

  If the optional `<encode>` argument is true, the resulting string is further encoded in base-64 so that it only contains alphanumeric characters.

  decrypt() decrypts a string encrypted with encrypt(). The `<encoded>` argument indicates that the encrypted string was base-64 encoded.


::: seealso
- [ENCODE64()]
- [DIGEST()]
:::
# ENTRANCES()
`entrances([<object>[, <type>[, <begin>[, <end>]]]])`

  With no arguments, the entrances() function returns a list of all exits, things, players, and rooms linked to your location, like @entrances. You can specify an object other than your current location with `<object>`. You can limit the type of objects found by specifying one or more of the following for `<type>`:<br>
        a        all (default)<br>
        e        exits<br>
        t        things<br>
        p        players<br>
        r        rooms

  You can also limit the range of the dbrefs searched by giving `<begin>` and `<end>`. If you control `<object>`, or have the Search or See_All powers, all objects linked to `<object>` are returned. Otherwise, only objects you can examine will be included.


::: seealso
- [lsearch()]
- [@entrances]
:::
# EQ()
`eq(<number1>, <number2>[, ... , <numberN>])`

  Takes two or more `<number>`s, and returns 1 if they are all equal, and 0 otherwise.


::: seealso
- [NEQ()]
- [LMATH()]
:::
# ESCAPE()
`escape(<string>)`

  The escape() function "escapes out" potentially "dangerous" characters, preventing function evaluation in the next pass of the parser. It returns `<string>` after adding the escape character ('\') at the beginning of the string, and before the following characters:

  %  ;  [  ]  {  }  \ ( ) , ^ $

  This function prevents code injection in strings entered by players. It is only needed when `<string>` will be passed through a command or function which will evaluate it again, which can usually be avoided. Since the function preserves the original string, it is, in most cases, a better choice than secure(), but decompose() is often better still.


::: seealso
- [DECOMPOSE()]
- [SECURE()]
:::
# EVAL()
# GET_EVAL()
`eval(<object>, <attribute>)`<br>
`get_eval(<object>/<attribute>)`

  eval() and get_eval() are similar to ufun(), in that they evaluate the given `<attribute>` on `<object>`. However, they change the enactor (%#) to the object executing the eval (%!). It does not modify the stack (%0-%9), so the attribute being evaled sees the same values for them that the calling code does. Unless you need this behavior, it is better to use u() instead, which hides the caller's stack.

  Example:
```sharp
&TEST Foo=%b%b%b-[name(me)] (%n)
&CMD Foo=$test: @emit ufun(me/test) ; @emit eval(me, test)
test
-Foo (Mike)
-Foo (Foo)
```


::: seealso
- [GET()]
- [u()]
- [GET()]
- [EDEFAULT()]
:::
# EXIT()
`exit(<object>)`

  Returns the dbref of the first exit in room `<object>`.

  You can get the complete exit list of any room you may examine, regardless of whether or not exits are dark. You can get the partial exit list (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the exit list of anything else, regardless of whether or not you have objects in it.


::: seealso
- [LEXITS()]
- [NEXT()]
:::
# EXTRACT()
`extract(<list>[, <first>[, <length>[, <delimiter>]]])`

  This function returns `<length>` elements of `<list>`, counting from the `<first>`th element. If `<length>` is not specified, the default is 1, so extract(`<list>`,3) acts like elements(`<list>`,3). If `<first>` is not specified, the default is the 1, so extract(`<list>`) acts like first(`<list>`).

  If `<first>` is negative, extract() will begin counting backwards from the end of `<list>`, so -1 starts at the last element, -2 the element before last, and so on.

  If `<length>` is negative, extract() will return up to and including the `<length>`th element from the right, so -1 will extract up to the last element, -2 up to the element before last, and so on.

  Examples:
```sharp
think extract(This is a test string,3,2)
a test
```

    > think extract(Skip the first and last elements, 2, -2)<br>
    the first and last

    > think extract(Get just the last three elements,-3, 3)<br>
    last three elements


::: seealso
- [INDEX()]
- [ELEMENTS()]
- [GRAB()]
:::
# FILTER()
# FILTERBOOL()
`filter([<obj>/]<attr>, <list>[, <delimiter>[, <osep>[, ..., <argN>]]])`<br>
`filterbool([<obj>]/<attr>, <list>[, <delimiter>[, <osep>[, ..., <argN>]]])`

  These functions returns the elements of `<list>` for which a user-defined function evaluates to "1" (for filter()), or to a boolean true value (for filterbool()). That function is specified by the first argument (just as with the ufun() function), and the element of the list being tested is passed to that user-defined function as %0. Up to 29 further `<arg>`s can be specified, and will be available in the function as v(1) to v(30).

  `<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`.

  filter(`<obj>`/`<attr>`, `<list>`) is roughly equivalent to squish(iter(`<list>`, switch(ufun(`<obj>`/`<attr>`, %i0),1,%i0,))) though the filter() version is much more efficient.

  Example:
```sharp
&IS_ODD test=mod(%0,2)
say filter(test/is_odd, 1 2 3 4 5 6)
You say, "1 3 5"
```


::: seealso
- [anonymous attributes]
- [FIRSTOF()]
- [ALLOF()]
- [LOCKFILTER()]
- [FILTERQ()]
- [E()]
- [E()]
- [boolean values]
:::
# FILTERQ()
`filterq(<register>, [<object>/]<attribute>, <list>[, <delimiter>[, <osep>[, <arg1>[, ... , <argN>]]]])`

  filterq() is filter() with reject-capture: it returns the elements of `<list>` for which `<attribute>` evaluates to exactly 1, osep-joined, and ALSO sets the q-register `<register>` to the elements that were filtered OUT (osep-joined; an empty string when nothing was rejected).

  The register is the FIRST argument, following the setq()/setr() convention, because filter()'s positions after `<osep>` already carry extra predicate arguments (available to each evaluation as %1, %2, ...) for PennMUSH compatibility. filterq() keeps those extra arguments, shifted one position to the right.

  Example:
```sharp
> &ISNUM me=isnum(%0)
> think Kept: [filterq(bad, ISNUM, 12 apples 7 pears)] / Dropped: %q<bad>
Kept: 12 7 / Dropped: apples pears
```


::: seealso
- [FILTER()]
- [FILTER()]
- [E()]
- [E()]
- [setq()]
:::
# FINDABLE()
`findable(<object>, <victim>)`

  This function returns 1 if `<object>` can locate `<victim>`, or 0 if it cannot. If one of the objects does not exist, it will return #-1 ARGN NOT FOUND (where N is the argument which is the invalid object).

  The object executing the function needs to be see_all or control both `<object>` and `<victim>`.


::: seealso
- [locate()]
- [LOC()]
:::
# FIRST()
`first(<list>[, <delimiter>])`

  Returns the first element of a list.


::: seealso
- [BEFORE()]
- [REST()]
- [LAST()]
- [FIRSTOF()]
- [STRFIRSTOF()]
:::
# FIRSTOF()
`firstof([<expr>, ... , <exprN>][, <default>])`

  Returns the first evaluated `<expr>` that is true. If no `<expr>` arguments are true, `<default>` is returned.

  The meaning of true or false is dependent on configuration options as explained in the 'BOOLEAN VALUES' help topics.

  This function evaluates arguments one at a time, stopping as soon as one is true.

  Examples:
```sharp
say firstof(0,2)
You say, "2"
```

    > say firstof(10,11,0)<br>
    You say, "10"

    > say firstof(grab(the cat,mommy),grab(in the hat,daddy),#-1 Error)<br>
    You say, "#-1 Error"

    > say firstof(get(%#/royal cheese),#-1 This Has No Meaning,0,)<br>
    You say, ""


::: seealso
- [ALLOF()]
- [boolean values]
- [STRFIRSTOF()]
- [FILTER()]
:::
# FLAGS()
`flags()`<br>
`flags([<object>[/<attribute>]])`

  With no arguments, flags() returns a string consisting of the flag letters for each flag on the MUSH. Note that some flags have no letter, and mutlple flags may have the same letter (and so will appear multiple times).

  If an `<object>` is given, flags() returns 'P', 'T', 'R' or 'E', depending on whether `<object>` is a player, thing, room, or exit, followed by the flag letter for each flag set on `<object>`.

  With an `<object>`/`<attribute>`, the flag letters for each flag set on the given `<attribute>` are returned.

  Examples:
```sharp
@create Test
@set Test=no_command puppet
think flags(Test)
Tnp
```

    > think flags(me/describe)<br>
    $vp


::: seealso
- [LFLAGS()]
- [LIST()]
:::
# LFLAGS()
`lflags()`<br>
`lflags(<object>[/<attribute>])`

  With an argument, lflags() returns a space-separated list consisting of the names of all the flags attached to `<object>`, or `<object>`'s `<attribute>`.

  Given no arguments, this function returns a space-separated list of all flag names known to the server, as per @list/flags.

  Examples:
```sharp
@create Test
@set Test=no_command puppet
think flags(Test)
NO_COMMAND PUPPET
```

    > think flags(me/describe)<br>
    NO_COMMAND VISUAL


::: seealso
- [FLAGS()]
- [LIST()]
:::
# FLIP()
# REVERSE()
`flip(<string>)`

  flip() reverses a string. reverse() is an alias for flip().

  Example:
```sharp
say flip(foo bar baz)
You say, "zab rab oof"
```


::: seealso
- [REVWORDS()]
:::
# FMOD()
`fmod(<number>, <divisor>)`

  Similar to remainder() but may take floating point arguments. The return value is `<number>` - n * `<divisor>`, where n is the quotient of `<number>` / `<divisor>`, rounded towards zero. The result has the same sign as `<number>` and a magnitude less than the magnitude of `<divisor>`.

  Example:
```sharp
think fmod(6.1,2.5)
1.1
```

::: seealso
- [DIV()]
- [DIV()]
- [MOD()]
- [LMATH()]
:::
# FOLLOWERS()
`followers(<object>)`

  Returns the list of things and players following object. You must control `<object>`.


::: seealso
- [FOLLOWING()]
- [follow]
- [unfollow]
:::
# FOLLOWING()
`following(<object>)`

  Returns the list of things and players that the object is following. You must control `<object>`.


::: seealso
- [FOLLOWERS()]
- [follow]
- [unfollow]
:::
# FRACTION()
`fraction(<number>[, <whole>])`

  This function returns a fraction representing the floating-point `<number>`, reduced to its
  lowest terms. Dividing the numerator by the denominator gives back exactly `<number>`.

  PennMUSH answers the *simplest* fraction within one part in 10^10 instead, which is not always
  the number you gave it; `fraction(pi())` is `348987/111086` there. Round `<number>` first if
  you want a simpler fraction than the one it names.

  If `<whole>` is true, and `<number>` is greater than 1.0 (or less than -1.0), the return value will be a whole number followed by the fraction representation of the decimal.

  Examples:
```sharp
think fraction(.75)
3/4
```

    > think fraction(pi())<br>
    3141593/1000000

    > think fraction(2)<br>
    2

    > think fraction(2.75)<br>
    11/4

    > think fraction(2.75, 1)<br>
    2 3/4
# FULLNAME()
`fullname(<object>)`

  fullname() returns the full name of object `<object>`. It is identical to name() except that for exits, fullname() returns the complete exit name, including all aliases.

  Example:
```sharp
say fullname(south)
You say, "South;sout;sou;so;s"
```


::: seealso
- [NAME()]
- [ACCNAME()]
- [INAME()]
- [ALIAS()]
- [ALIAS()]
:::
# FUNCTIONS()
`functions([<type>])`

  Returns a space-separated list of the names of functions. If `<type>` is "local", only @functions are listed. If "builtin", only builtin functions. If "all" or omitted, both are returned.


::: seealso
- [LIST()]
- [CONFIG()]
:::
# GET()
# XGET()
`get(<object>/<attribute>)`<br>
`xget(<object>, <attribute>)`

  These functions return the string stored in an `<object>`'s `<attribute>` attribute, without evaluating it. You must be able to examine the attribute. get() and xget() are identical, apart from the argument separator.

    Example:
```sharp
&test me=This is [a test].
think get(me/test)
This is [a test].
```


::: seealso
- [HASATTR()]
- [VISIBLE()]
- [u()]
- [DEFAULT()]
- [UDEFAULT()]
:::
# GETPIDS()
`getpids(<object>[/<attribute>])`

  Returns a space-separated list of semaphore queue process ids waiting on the given `<object>` and semaphore `<attribute>`. If `<attribute>` is not given, pids for all semaphores on the object are returned.


::: seealso
- [@ps]
- [@wait]
- [LPIDS()]
- [PIDINFO()]
- [semaphores]
:::
# GRAB()
# REGRAB()
# REGRABI()
`grab(<list>, <pattern>[, <delimiter>])`<br>
`regrab(<list>, <regexp>[, <delimiter>[, <osep>]])`<br>
`regrabi(<list>, <regexp>[, <delimiter>])`

  These functions return the first word in `<list>` which matches the pattern. For grab(), `<pattern>` is a wildcard pattern ([WILDCARDS]). For regrab() and regrabi(), the pattern is a regular expression. regrabi() is case-insensitive. `<delimiter>` defaults to a space.

  Basically, this is a much more efficient way to do:<br>
`elements(<list>, match(<list>, <pattern>[, <delimiter>])[, <delimiter>])`<br>
  or the regular expression variation thereof.


::: seealso
- [GRABALL()]
- [element()]
- [EXTRACT()]
- [ELEMENTS()]
- [regmatch()]
:::
# GRABALL()
# REGRABALL()
# REGRABALLI()
`graball(<list>, <pattern>[, <delim>[, <osep>]])`<br>
`regraball(<list>, <regexp>[, <delim>[, <osep>]])`<br>
`regraballi(<list>, <regexp>[, <delim>[, <osep>]])`

  These functions work identically to the grab() and regrab()/regrabi() functions, except they return all matches, not just the first: They return all words in the `<list>` which match `<pattern>`. If none match, an empty string is returned. `<delim>` defaults to a space, and `<osep>` defaults to `<delim>`.

  Examples:
```sharp
say graball(This is a test of a test,test)
You say "test test"
say graball(This|is|testing|a|test,tes*,|)
You say "testing|test"
say regraball(This is testing a test,s$)
You say "This is"
```


::: seealso
- [element()]
- [element()]
- [GRAB()]
- [regmatch()]
:::
# GREP()
# REGREP()
# WILDGREP()
# GREPI()
# REGREPI()
# WILDGREPI()
# PGREP()
`grep(<object>, <attrs>, <substring>)`<br>
`wildgrep(<object>, <attrs>, <pattern>)`<br>
`regrep(<object>, <attrs>, <regexp>)`<br>
`grepi(<object>, <attrs>, <substring>)`<br>
`regrepi(<object>, <attrs>, <regexp>)`<br>
`wildgrepi(<object>, <attrs>, <pattern>)`<br>
`pgrep(<object>, <attrs>, <substring>)`

  These functions return a list of attributes on `<object>` containing `<substring>`, matching the wildcard `<pattern>`, or matching the regular expression `<regexp>`. `<attrs>` is a wildcard pattern for attribute names to search.

  Parsing _does_ occur before this function is invoked. Therefore, "special" characters will need to be escaped out.

  grep()/wildgrep()/regrep() are case-sensitive.<br>
  grepi()/wildgrepi()/regrepi() are case-insensitive.

  pgrep() works like grep(), but also checks attributes inherited from parents.


::: seealso
- [@grep]
- [LATTR()]
- [WILDCARDS]
:::
# GT()
`gt(<number1>, <number2>[, ... , <numberN>])`

  Takes two or more numbers, and returns 1 if and only if each number is greater than the number after it, and 0 otherwise.


::: seealso
- [GTE()]
- [LT()]
- [LTE()]
- [EQ()]
- [NEQ()]
- [LMATH()]
:::
# GTE()
`gte(<number1>, <number2>[, ... , <numberN>])`

  Takes two or more numbers, and returns 1 if and only if each number is greater than or equal to the number after it, and 0 otherwise.


::: seealso
- [GT()]
- [LT()]
- [LTE()]
- [EQ()]
- [NEQ()]
- [LMATH()]
:::
# HASATTR()
# HASATTRP()
# HASATTRVAL()
# HASATTRPVAL()
`hasattr(<object>[/<attribute>][, <attribute>])`<br>
`hasattrp(<object>[/<attribute>][, <attribute>])`<br>
`hasattrval(<object>[/<attribute>][, <attribute>])`<br>
`hasattrpval(<object>[/<attribute>][, <attribute>])`

  The hasattr*() functions check to see if `<object>` has a given attribute. They return #-1 if the object does not exist or the attribute can't be examined by the player. Otherwise, they return 1 if the attribute is present and 0 if it is not.

  hasattr() checks to see if `<attribute>` exists on `<object>` only.

  hasattrp() also checks for `<attribute>` on `<object>`'s parent/ancestor.

  hasattrval() only returns 1 if `<attribute>` exists and is non-empty. An "empty" attr is one containing a null value (if the empty_attrs config option is on), or one containing a single space (if the option is off).

  hasattrpval() is like hasattrval() but also checks parents.

  All four functions will also work with one argument in the form of `<object>`/`<attribute>`. A
  single argument carrying no `/` is `#-1 BAD ARGUMENT FORMAT TO <function>`.


::: seealso
- [VISIBLE()]
- [LATTR()]
:::
# HASFLAG()
`hasflag(<object>[/<attrib>], <flag>)`

  With no `<attrib>`, hasflag() returns 1 if `<object>` has the `<flag>` flag set. If `<attrib>` is specified, the attribute is checked for the `<flag>` attribute flag instead. If the flag is not present, 0 is returned.

  hasflag() will accept a full flag name ("Wizard") or a flag letter ("W"). You can check the flags of any object, whether you control them or not.

  Example:
```sharp
think hasflag(me, wizard)
1
```


::: seealso
- [ORFLAGS()]
- [ANDFLAGS()]
- [ORFLAGS()]
- [ANDFLAGS()]
- [FLAGS()]
- [LFLAGS()]
- [attribute flags]
- [@flag]
- [HASPOWER()]
- [HASTYPE()]
:::
# HASPOWER()
`haspower(<object>, <power>)`

  Returns 1 if `<object>` has the named power, and 0 if it does not.

  You can check the powers of any object, whether you control it or not.


::: seealso
- [@power]
- [@power]
- [HASFLAG()]
:::
# HASROLE()
`hasrole(<object>, <role>)`

  Returns 1 if `<object>` holds the role named `<role>`, and 0 if not. `<role>` is the role's short name as @role/list shows it. An object holds `everyone`, the roles assigned to it, and, for a character linked to an account, the account's roles. A player that is not a guest also holds `player`, and #1 holds `god`.

  Example:
```sharp
think hasrole(*Ariel, moderator)
1
```


::: seealso
- [ROLES()]
- [PERMISSION()]
- [@role]
:::
# HASTYPE()
`hastype(<object>, <type list>)`

  Returns 1 if `<object>` belongs to one of the types given in `<type list>`, and 0 otherwise. Valid types are PLAYER, THING, ROOM, EXIT and GARBAGE.

  Example:
```sharp
@create Test Object
think hastype(test object, PLAYER EXIT)
0
think hastype(test object, PLAYER THING)
1
```


::: seealso
- [TYPES OF OBJECTS]
- [TYPE()]
:::
# HIDDEN()
`hidden(<player|descriptor>)`

  If you can see hidden players, this function returns 1 if `<player>` (or the player connected to `<descriptor>`) is hidden, and 0 otherwise. If you cannot see hidden players, hidden() returns #-1.


::: seealso
- [@hide]
:::
# HOME()
`home(<object>)`

  Returns the object's 'home', where it is @link'd to. This is the home for a player or thing, the drop-to of a room, or source of an exit.


::: seealso
- [@link]
:::
# HOST()
# HOSTNAME()
`host(<player|descriptor>)`

  Returns the hostname a player is connected from, as shown on the wizard WHO. This may be more reliable that get(`<player>`/lastsite) if the player has multple connections from different locations, and the function is called with a descriptor argument.

  The caller can use the function on himself, but using on any other player requires privileged power such as Wizard, Royalty or SEE_ALL.

  hostname() is an alias for host().


::: seealso
- [Connection functions]
- [IPADDR()]
- [LPORTS()]
- [LPORTS()]
:::
# IDLE()
# IDLESECS()
`idle(<player|descriptor>[, <precision>])`

  This function returns the number of seconds a player has been idle, much as WHO does. `<player name>` must be the full name of a player, or a player's dbref. You can also specify a `<descriptor>`, useful if a player is connected multiple times, or for connections which are still at the login screen. Players who are not connected have an idle time of "-1", as do dark wizards, when idle() is used on them by a non-priv'ed player.

  idlesecs() is an alias for idle().


::: seealso
- [Connection functions]
- [CONN()]
:::
# IF()
# IFELSE()
`if(<condition>, <true expression>[, <false expression>])`<br>
`ifelse(<condition>, <true expression>, <false expression>)`

  These functions evaluate the `<condition>` and return `<true expression>` if the `<condition>` is true, or `<false expression>` (if provided) if the `<condition>` is false. Only the returned `<expression>` is evaluated.


::: seealso
- [boolean values]
- [switch()]
- [@if]
- [@break]
- [COND()]
:::
# INAME()
`iname(<object>)`

  iname() returns the name of `<object>`, as it would appear if you were inside it. It is identical to name() except that if the object has a NAMEFORMAT or NAMEACCENT attribute, it is used.

  You must be see_all, control `<object>`, or be inside it to use this function.


::: seealso
- [@nameformat]
- [@nameaccent]
- [NAME()]
- [FULLNAME()]
- [ACCNAME()]
:::
# INC()
`inc(<integer>)`<br>
`inc(<string-ending-in-integer>)`

  inc() returns the integer plus 1. If given a string that ends in an integer, it increments only the final integer portion. That is:

  Examples:
```sharp
think inc(3)
4
```

    > think inc(hi3)<br>
    hi4

    > think inc(1.3.3)<br>
    1.3.4

  Note especially the last example, which will trip you up if you use floating point numbers with inc() and expect it to work like add().

  If the null_eq_zero @config option is on, using inc() on a string which does not end in an integer will return `<string>`1. When null_eq_zero is turned off, it will return an error.


::: seealso
- [DEC()]
- [ADD()]
- [SUB()]
:::
# INDEX()
`index(<list>, <character>, <first>, <length>)`

  This function is similar to extract(), except that it requires four arguments, while extract() uses defaults for its arguments if they aren't given. The function returns `<length>` items starting from the `<first>` position. Trailing spaces are trimmed.

  Examples:
```sharp
say index(Cup of Tea | Mug of Beer | Glass of Wine, |, 2, 1)
You say, "Mug of Beer"
```

    > say index(%rtoy boat^%rblue tribble^%rcute doll^%rred ball,^,2,2)<br>
    You say, "<br>
    blue tribble^<br>
    cute doll"


::: seealso
- [EXTRACT()]
- [ELEMENTS()]
- [GRAB()]
:::
# INSERT()
# LINSERT()
`linsert(<list>, <position>, <new item>[, <delim>])`

  If `<position>` is a positive integer, this inserts `<new item>` BEFORE the item at `<position>` from the left in `<list>`. That means that `<new item>` then becomes the `<position>`th element of `<list>`.

  If `<position>` is a negative integer, this inserts `<new item>` AFTER the item at the absolute value of `<position>` from the RIGHT in `<list>`. This is the same as reversing the list before inserting `<new item>`, and then reversing it again into correct order. For example, when `<position>` is -1, `<new item>` will be the last in the list; when `<position>` is -2, `<new item>` will be the second item from the right, and so on.

  If a `<delim>` is not given, a space is assumed. Null items are counted when determining position, as in 'items()'.

  Examples:
```sharp
say linsert(This is a string,4,test)
You say, "This is a test string"
say linsert(one|three|four,2,two,|)
You say, "one|two|three|four"
say linsert(meep bleep gleep,-3,GOOP)
You say, "meep GOOP bleep gleep"
```

  insert() is an alias for linsert(), for backwards compatability.


::: seealso
- [LREPLACE()]
- [LDELETE()]
- [STRINSERT()]
:::
# ISDAYLIGHT()
`isdaylight([<secs>[, <timezone>]])`

  Returns 1 if it's daylight savings in the specified timezone at the given time. Defaults to the host server's time zone and current time if not specified.


::: seealso
- [timezones]
- [SECS()]
:::
# ISAPPROVED()
`isapproved(<object>)`

  Returns 1 if `<object>` is royalty or above, or carries the APPROVED flag, and 0 otherwise. A guest is never approved, whatever else is set on it.

  APPROVED is the engine's general "this character has cleared whatever bar this game sets for full participation" flag. The engine ships the flag and this predicate and deliberately ships no policy for what earns it; a game decides that and sets the flag however it likes (royalty and above can set and unset it).

  Softcode and the server answer this question with the same code, so a game's `+`-verbs cannot drift from the engine's own checks. Games that want a different rule should wrap this in one function attribute and call that everywhere, rather than re-implementing the test.

  Example:
```sharp
think isapproved(me)
1
&FUN`IS`APPROVED #100=isapproved(%0)
```


::: seealso
- [HASFLAG()]
- [@flag]
- [FLAG LIST]
:::
# ISDBREF()
# ISOBJID()
`isdbref(<string>)`<br>
`isobjid(<string>)`

  isobjid() returns 1 if `<string>` is the object id of an existing object. If `<string>` is not a full objid, or is the objid of a garbage object, it returns 0.

  isdbref() functions the same, but will also return 1 if `<string>` is the dbref of an existing (or garbage) object.

  Examples:
```sharp
@stats
100 objects = 20 rooms, 20 exits, 20 things, 20 players, 20 garbage.
The next object to be created will be #33.
```

    > think isdbref(#33)<br>
    1<br>
    > think isobjid(#33:1234567890)<br>
    0

    > think csecs(#1)<br>
    1324654503<br>
    > think isdbref(#1)<br>
    1<br>
    > think isobjid(#)<br>
    0<br>
    > think isdbref(#1:1324654503)<br>
    1<br>
    > think isobjid(#1:1324654503)<br>
    1<br>
    > think isobjid(#1:9876543210)<br>
    0


::: seealso
- [database]
- [OBJIDS]
- [NUM()]
- [OBJID()]
:::
# ISINT()
`isint(<string>)`

  Returns 1 if its argument is an integer, and 0 otherwise. Integers can begin with a '+' or '-' sign, but the rest of the string must be digits.


::: seealso
- [ISNUM()]
:::
# ISNUM()
`isnum(<string>)`

  This function returns 1 if `<string>` is a number, and 0 if it is not. Numbers can begin with a '-' sign (for negatives), but the rest of the characters in the string must be digits, and an optional decimal point.


::: seealso
- [ISINT()]
:::
# ISREGEXP()
`isregexp(<string>)`

  This function returns 1 if `<string>` is a valid regular expression, and 0 if it is not.


::: seealso
- [regexp]
:::
# ISWORD()
`isword(<string>)`

  This function returns 1 if every character in `<string>` is a letter, or 0, if any character isn't a letter. Case does not matter.

# ITEMS()
`items(<list>, <delim>)`

  items() counts the number of items in a list using an arbitrary `<delim>`. Null items are counted, so:

`items(X|X,|)     => 2     (2 X items)`<br>
`items(X||X,|)    => 3     (2 X items and 1 null item)`<br>
`items(X X,%b)    => 2     (2 X items)`<br>
`items(X%b%bX,%b) => 3     (2 X items and 1 null item)`<br>
`items(,|)        => 1     (a single null item)`

   Another way to think about this is that items() counts the number of times `<delim>` appears in `<list>`, and adds 1.


::: seealso
- [WORDS()]
:::
# ITEMIZE()
# ELIST()
`itemize(<list>[, <delim>[, <conjunction>[, <punctuation>]]])`<br>
`elist(<list>[, <conjunction>[, <delim>[, <osep>[, <punctuation>]]]])`

  These functions take the elements of `<list>` (separated by `<delim>` or a space by default), and:<br>
   If there's just one, return it.<br>
   If there's two, return `<e1>` `<conjunction>` `<e2>`<br>
   If there's more than two, return `<e1>``<punc>` `<e2>``<punc>` ... `<conj>` `<en>`

  elist() uses `<osep>` after `<punc>`/`<conj>` instead of a space.<br>
  The default `<conjunction>` is "and", default punctuation is ",".<br>
  Examples:
```sharp
say itemize(eggs) * [itemize(eggs bacon)]
You say, "eggs * eggs and bacon"
say itemize(eggs bacon spam)
You say, "eggs, bacon, and spam"
say itemize(eggs bacon spam, ,&,;)
You say, "eggs; bacon; & spam"
```
# IBREAK()
`ibreak([<level>])`

  The ibreak() function stops an iter() from running at the end of the current loop. When used in nested iter()s, you can give a `<level>` to specify how many iter()s to break from. iter() will stop evaluating at the end of the current loop, and NOT immediately after ibreak() is called.

  Examples:
```sharp
say iter(1 2 3 4 5,switch(%i0,3,ibreak())Test %i0!)
You say, "Test 1! Test 2! Test 3!"
```

    > say iter(1 2 3 4 5,switch(%i0,3,ibreak(),Test %i0!))<br>
    You say, "Test 1! Test 2! "

    > say iter(a b c, iter(1 2 3, switch(%i0%i1, 2c, ibreak(2), %$0)))<br>
    You say, "1a 2a 3a 1b 2b 3b 1c "


::: seealso
- [iter()]
- [ilev()]
- [ilev()]
- [ilev()]
:::
# IPADDR()
`ipaddr(<player|descriptor>)`

  Returns the IP address of the connected player or descriptor. This may be more reliable than get(`<player>`/lastip) if the player has multple connections from different locations, and the function is called with a descriptor argument.

  The caller can use the function on himself, but using on any other player requires privileged power such as Wizard, Royalty or SEE_ALL.


::: seealso
- [Connection functions]
- [HOST()]
- [LPORTS()]
- [LPORTS()]
:::
# JITER()
`jiter(<attribute list>, <input>[, <osep>])`

  jiter() (juxtapositioned iteration) evaluates each attribute in the space-separated `<attribute list>` with the SAME `<input>` passed as %0, and returns the results side by side, joined by `<osep>` (default: one space).

  Where iter() and map() walk a list of data through one function, and chain() threads one value THROUGH a list of attributes (each step receiving the previous step's result), jiter() fans one input ACROSS a list of attributes: every step receives the original input. The classic use is computing the fields of a record from a single object.

  Each attribute is evaluated as by ufun(). Object names in the list may not contain spaces (use "me" or a dbref), since spaces separate the attributes.

  Example:
```sharp
> &FNAME me=name(%0)
> &FTYPE me=type(%0)
> say jiter(FNAME FTYPE, %#, |)
You say, "One|PLAYER"
```


::: seealso
- [CHAIN()]
- [MAP()]
- [iter()]
- [fold()]
- [u()]
:::
# LAST()
`last(<list>[, <delimiter>])`

  Returns the last element of a list. Elements in `<list>` are separated by `<delimiter>`, if given, or by a space if not.


::: seealso
- [FIRST()]
- [REST()]
- [BEFORE()]
- [AFTER()]
:::
# LATTR()
# LATTRP()
# REGLATTR()
# REGLATTRP()
`lattr(<object>[/<attribute pattern>][, <output separator>])`<br>
`lattrp(<object>[/<attribute pattern>][, <output separator>])`<br>
`reglattr(<object>[/<regexp>][, <output separator>])`<br>
`reglattrp(<object>[/<regexp>][, <output separator>])`

  lattr() returns a list of all the attributes on `<object>` which you can see, and which match the wildcard `<attribute pattern>`. If no `<attribute pattern>` is given, it defaults to "*". Note that this does not include branches in attribute trees; you must use the "**" wildcard to include those.

  The resulting list will be separated by `<output separator>`, or a space if no separator is given.

  reglattr() returns attributes whose names match the regexp `<regexp>`. The match is not case-sensitive (as attribute names are always upper-case), and the `` ` `` branch separator has no special meaning in the pattern.

  lattrp() and reglattrp() also include attributes inherited from parents.

  When returning large numbers of attributes, the results may be truncated due to buffer limits. In these cases, you can use nattr() and xattr() to retrieve the results in smaller pieces.


::: seealso
- [NATTR()]
- [XATTR()]
- [HASATTR()]
- [examine]
- [GREP()]
- [WILDCARDS]
:::
# NATTR()
# NATTRP()
# ATTRCNT()
# ATTRPCNT()
# REGNATTR()
# REGNATTRP()
`nattr(<object>[/<attribute pattern>])`<br>
`nattrp(<object>[/<attribute pattern>])`<br>
`regnattr(<object>[/<regexp>])`<br>
`regnattrp(<object>[/<regexp>])`

  nattr() returns the number of attributes on `<object>` that you can see which match the given `<attribute pattern>`. It is considerably faster than words(lattr()) and doesn't suffer from buffer length constraints. It's designed primarily for statistical purposes. `<attribute pattern>` defaults to "*", which does not include branches in attribute trees; use "**" if you need to count attribute trees.

  regnattr() matches attribute names against the regular expression `<regexp>`.

  nattrp() and regnattrp() also count matching attributes on the parent.

  attrcnt() and attrpcnt() are aliases for nattr() and nattrp() respectively.


::: seealso
- [LATTR()]
- [HASATTR()]
- [XATTR()]
- [WILDCARDS]
:::
# LCON()
`lcon(<object>[, <type>])`

  Returns a list of the dbrefs of objects which are located in `<object>`.

  You can get the complete contents of any object you may examine, regardless of whether or not objects are dark. You can get the partial contents (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the contents of anything else, regardless of whether or not you have objects in it.

  When used on exits, this function returns #-1.

  For compatability with other codebases, a `<type>` can be given to limit the results. Valid `<type>`s are:<br>
    player             - equivalent to lplayers(`<object>`)<br>
    connect            - equivalent to lvplayers(`<object>`)<br>
    thing (or object)  - equivalent to lthings(`<object>`)<br>
    listen             - return only listening objects<br>
    puppet             - return only THINGs set PUPPET


::: seealso
- [LEXITS()]
- [LPLAYERS()]
- [LTHINGS()]
- [CON()]
- [NEXT()]
- [LVCON()]
:::
# LCSTR()
# LCSTR2()
`lcstr(<string>)`<br>
`lcstr2(<string>)`

  Returns `<string>` with all letters converted to lowercase.

  If the MUSH is compiled with ICU Unicode support, lcstr2() does the same thing except the returned string might be a different length, and ansi colors and other markup are stripped.

  Example:
```sharp
say lcstr(Foo BAR bAz)
You say, "foo bar baz"
```


::: seealso
- [CAPSTR()]
- [UCSTR()]
:::
# LDELETE()
`ldelete(<list>, <position(s)>[, <delimiter>[, <osep>]])`

  This function deletes the element(s) of `<list>` at the given `<position(s)>`. Elements of `<list>` are separated by `<delimiter>`, which defaults to a space. Null items are counted, as in 'items()'. Elements of `<position(s)>` must be numeric, and are always separated by a space, not by `<delimiter>`. Elements of the returned list are separated by `<osep>`, which defaults to the `<delimiter>`.

  If a `<position>` is negative, ldelete() counts backwards from the end of the list; a position of -1 deletes the last element, -2 the element before last, and so on.

  All position calculations are performed on the original list. That is, ldelete(a b c, -1 -1) will return "a b", not "a", and ldelete(a b c, -1 -2) returns "a", not "b".

  Examples:
```sharp
say ldelete(This is a long test string,4)
You say, "This is a test string"
say ldelete(lemon|orange|pear|apple,2 3,|)
You say, "lemon|apple"
say ldelete(foo bar baz boing,3,,%b~%b)
You say, "foo ~ bar ~ boing"
```


::: seealso
- [STRDELETE()]
- [REMOVE()]
- [INSERT()]
:::
# LEFT()
`left(<string>, <length>)`

  Returns the first `<length>` characters from `<string>`.


::: seealso
- [RIGHT()]
- [MID()]
- [LJUST()]
:::
# NSLEMIT()
# LEMIT()
`lemit(<message>)`<br>
`nslemit(<message>)`

  lemit() emits a message in the caller's outermost room, as per @lemit.

  nslemit() like @nslemit.


::: seealso
- [@lemit]
- [REMIT()]
:::
# LETQ()
`letq([<reg1>, <value1>[, ... , <regN>, <valueN>], ]<expr>)`

  letq() saves the current values of the given q-`<reg>`isters, sets them to new `<value>`s, evaluates `<expr>` and then restores the saved registers. It does not restore registers that are not listed. None of the values can see the updated contents of the registers -- they are only visible to `<expr>`.

  It returns the result of `<expr>`.

  Examples:
```sharp
think setr(A, 1):[letq(A, 2, %qA)]:%qA
1:2:1
think setr(A, 1)[setr(B,1)]:[letq(A, 2, %qA[setr(B,2)])]:%qA%qB
11:22:12
```


::: seealso
- [setq()]
- [setq()]
- [LISTQ()]
- [LISTQ()]
- [LOCALIZE()]
- [ulocal()]
- [R()]
:::
# LEXITS()
`lexits(<room>)`

  Returns a list of the dbrefs of exits in a room.

  You can get the complete exit list of any room you may examine, regardless of whether or not exits are dark. You can get the partial exit list (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the exit list of anything else, regardless of whether or not you have objects in it.


::: seealso
- [LCON()]
- [EXIT()]
- [NEXT()]
- [LVEXITS()]
:::
# LJUST()
`ljust(<string>, <length>[, <fill>[, <truncate?>]])`

  This function returns `<string>`, padded with the string `<fill>` until it's `<length>` characters long. `<fill>` can be more than one character in length, and defaults to a single space.

  If `<string>` is longer than `<length>`, it will be returned unaltered, unless `<truncate?>` is true, in which case only the first `<length>` characters of `<string>` are returned.

  Examples:
```sharp
say ljust(foo,6)
You say, "foo   "
```

    > say %r0[ljust(foo,6,-)]7%r01234567<br>
    You say, "<br>
    0foo---7<br>
    01234567"

    > say ljust(foo,12,=+)<br>
    You say, "foo=+=+=+=+="

    > say ljust(This is too long,9,,1)<br>
    You say, "This is t"


::: seealso
- [align()]
- [CENTER()]
- [RJUST()]
- [LEFT()]
:::
# LINK()
`link(<object>, <destination>[, <preserve>])`

  This function links `<object>` to `<destination>`. While normally used on exits, it has all of the other capabilities of @link as well. It returns #-1 or 0 on failure, 1 on success. If the optional third argument is true, acts like @link/preserve.


::: seealso
- [@link]
- [OPEN()]
:::
# LIST()
`list(<option>[, <type>])`

  This is the function-equivilent of the @list command, and lists some useful information about the MUSH. `<option>` can be one of:

  motd        : Returns the current @motd<br>
  wizmotd     : Returns the current @motd/wizard. Wiz/Roy only.<br>
  downmotd    : Returns the current @motd/down. Wiz/Roy only.<br>
  fullmotd    : Returns the current @motd/full. Wiz/Roy only.<br>
  functions   : Returns a list of all built-in functions and @functions.<br>
  commands    : Returns a list of all built-in commands and @commands.<br>
  attribs     : Returns all standard attributes.<br>
  locks       : Returns the built-in lock types. Similar to llocks().<br>
  flags       : Returns all flags. Similar to lflags().<br>
  powers      : Returns all @powers.

  "commands"/"functions" return both built-in and local commands/functions by default. You can specify a `<type>` of either "builtin", "local" or "all" to limit this if you wish.


::: seealso
- [@list]
- [FLAGS()]
- [LFLAGS()]
- [CONFIG()]
- [FUNCTIONS()]
- [@motd]
- [@motd]
:::

`llocks()`
# LIT()
`lit(<string>)`

  This function returns `<string>` literally - without even squishing spaces, and without evaluating *anything*. This can be useful for writing ASCII maps with spaces or whatever.

  It can be a bit tricky to get a literal string with spaces into an attrib, however, since spaces are usually squished in setting an attribute. This example illustrates how to make it work:

    > @va me=$test: think {[lit(near       far)]}<br>
    Set.<br>
    > ex me/va<br>
    VA [#1]: $test: think {[lit(near       far)]}<br>
    > test<br>
    near       far

  Leaving out the {}'s will not work in the above.


::: seealso
- [DECOMPOSE()]
:::
# LMATH()
`lmath(<op>, <list>[, <delim>])`

  This function performs generic math operations on `<list>`, returning the result. Each element of the list is treated as one argument to an operation, so that lmath(`<op>`, 1 2 3) is equivalent to `<op>`(1, 2, 3). Using @function, one can easily write ladd, lsub, etc as per TinyMUSH.

  Supported `<op>`'s are:<br>
  add and band bor bxor dist2d dist3d div eq fdiv floordiv gt gte lt lte max mean median min mod modulo modulus mul nand neq nor or remainder stddev sub xor

  Examples:
```sharp
think lmath(add, 1|2|3, |)
6
```

    > think lmath(max, 1 2 3)<br>
    3

    > &FUN_FACTORIAL me=lmath(mul,lnum(1,%0))<br>
    > think u(fun_factorial,5)<br>
    120
# LN()
`ln(<number>)`

  Returns the natural log of `<number>`. This is equivilent to log(`<number>`, e).


::: seealso
- [LOG()]
:::
# LNUM()
`lnum(<number>)`<br>
`lnum(<start number>, <end number>[, <output separator>[, <step>]])`

  With one argument, lnum returns a list of numbers, from 0 to `<number - 1>`. For example, lnum(4) returns the list "0 1 2 3". This is useful for creating loops.

  With two arguments, the numbers range from the first to the second argument. For example, lnum(1,4) => 1 2 3 4

  With three arguments, the output is separated by the separator given in the third argument. lnum(1,4,|) => 1|2|3|4

  A fourth argument dictates the step. By default, the step is 1.<br>
  lnum(1,10,%b,2) -> 1 3 5 7 9<br>
  lnum(0,10,%b,2) -> 0 2 4 6 8 10

# LOC()
`loc(<object>)`

  For things and players, loc() returns the dbref of the object which contains `<object>`. For rooms, it returns the drop-to, if one is set, or #-1 otherwise. For exits, it returns the destination (the source is an exits home()). This will be #-1 for unlinked exits, #-2 for variable exits, and #-3 for exits @linked to "home".

  You must be able to examine `<object>`, or be near it, for this function to work. A special case exists when `<object>` is a player: As long as `<object>` is not set UNFINDABLE, and you are allowed to use the @whereis command, you can get `<object>`'s location.

  You can also get the location of the enactor using the %L substitution, whether you are near to/can examine it or not.


::: seealso
- [locate()]
- [RLOC()]
- [HOME()]
- [WHERE()]
- [RNUM()]
- [ROOM()]
- [@link]
- [UNFINDABLE]
- [@whereis]
:::
# LOCALIZE()
`localize(<code>)`

  localize() saves the q-registers, evaluates its argument, and restores the registers afterwards. It has the same effect as ulocal(), but doesn't require setting the code into an attribute.

  Examples:
```sharp
say setr(0, Outside)-[setr(0, Inside)]-%q0
You say, "Outside-Inside-Inside"
```

    > &INSIDE me=setr(0,Inside)<br>
    > say setr(0, Outside)-[ulocal(INSIDE)]-%q0<br>
    You say, "Outside-Inside-Outside"

    > say setr(0, Outside)-[localize(setr(0, Inside))]-%q0<br>
    You say, "Outside-Inside-Outside"


::: seealso
- [LETQ()]
- [setq()]
- [setq()]
- [R()]
- [ulocal()]
- [UDEFAULT()]
:::
# LOCK()
`lock(<object>[/<locktype>][, <new value>])`

  lock() returns the text string equivalent of the @lock on `<object>`. `<locktype>` can be any valid switch for @lock ("Enter", "user:foo", etc) and defaults to "Basic". You must be able to examine the lock.

  If a `<new value>` is given, lock() attempts to change the lock as @lock would first. You must control the object.


::: seealso
- [LOCKING]
- [locktypes]
- [ELOCK()]
- [LOCKFLAGS()]
- [LLOCKFLAGS()]
- [LSET()]
- [LLOCKS()]
- [LOCKOWNER()]
- [LOCKFILTER()]
:::
# LLOCKS()
# LOCKS()
`llocks([<object>])`<br>
`locks(<object>)`

`llocks() and locks() both list @locks set on <object>, including user-defined locks (prefixed with USER:)`

  If no object is given, llocks() returns all the predefined lock types available.

  Example:
```sharp
@lock me==me
@lock/use me==me
@lock/user:itsme me==me
th llocks(me)
Basic USER:ITSME Use
```


::: seealso
- [LOCK()]
- [LSET()]
- [LOCKFLAGS()]
- [LLOCKFLAGS()]
- [LOCKOWNER()]
:::
# LOCKFILTER()
`lockfilter(<key>, <dbrefs>[, <delim>])`

  lockfilter() goes through `<dbrefs>` and tests them all against the lock `<key>`, returning a list of all dbrefs that pass the `<key>`.

  `<key>` is evaluated from the caller's perspective.

  This is equivilent to filter(#lambda/testlock(`<key>`, %%0), `<dbrefs>`) but much more efficient, as the lock `<key>` is only parsed/compiled once.

  `<delim>` defaults to a space, and is the delimiter of `<dbrefs>` and the list returned by lockfilter().

  Examples:
```sharp
Get all male players with a name starting with 'W'.
think iter(lockfilter(NAME^W*&SEX:M*,lwho()),name(%i0))
Walker WalkerBot Wilco
```

    List all wizroys online:<br>
    > think iter(lockfilter(FLAG^WIZARD|FLAG^ROYALTY,lwho()),name(%i0))<br>
    Sketch Viila Tanaku Raevnos Zebranky Cheetah Walker

    List all players with an IC age > 20.<br>
    > think lockfilter(age:>20,lwho())
    #123 #456 #789
    Note: You can escape the first character of `<key>` using double back slashes, for example, if you are checking for an attribute named +FOO to have the value of BAR on all connected players:<br>
    > think map(#apply/name,lockfilter(\\+FOO:BAR,lwho()))<br>
    Mike Walker Qon


::: seealso
- [LOCKING]
- [LOCK()]
- [ELOCK()]
- [lock keys]
- [FILTER()]
- [TESTLOCK()]
:::
# LOCKFLAGS()
`lockflags(<object>[/<locktype>])`<br>
`lockflags()`

  If an `<object>` is given, lockflags() returns a string consisting of the one-character abbreviations for all the lock flags on `<object>`'s `<locktype>` lock, or Basic lock if no locktype is given. You must be able to examine the lock.

  Given no arguments, this function returns a string consisting of all the flag letters the server knows.


::: seealso
- [LLOCKFLAGS()]
- [LSET()]
- [LOCK()]
- [LLOCKS()]
- [LOCKOWNER()]
:::
# LLOCKFLAGS()
`llockflags(<object>[/<locktype>])`<br>
`llockflags()`

  If an `<object>` is given, llockflags() returns a space-separated list of the lock flags on `<object>`'s `<locktype>` lock, or Basic lock if no locktype is given. You must be able to examine the lock.

  Given no arguments, this function returns a space-separated list of all the names of all lock flags known to the server.


::: seealso
- [LOCKFLAGS()]
- [LSET()]
- [LOCK()]
- [LLOCKS()]
- [LOCKOWNER()]
:::
# LOCKOWNER()
`lockowner(<object>[/<locktype>])`

  This function returns the dbref of the executor who set the `<locktype>` lock on `<object>`, or the Basic lock if no `<locktype>` is given. You must be able to examine the lock. Legacy locks with an unknown creator return `#-1`; absent or inaccessible locks return `#-1 NO SUCH LOCK`.


::: seealso
- [LOCKFLAGS()]
- [LLOCKFLAGS()]
- [LSET()]
- [LOCK()]
- [LLOCKS()]
:::
# LISTSET()
`listset(<list>,<position>,<replacement>[,<input delimiter>[,<output delimiter>]])`

  Replaces the item at the one-based position in a list. Delimiters default to a space; the output delimiter defaults to the input delimiter. For example, `listset(a b c,2,x)` returns `a x c`.

  List replacement uses `listset()`. `lset()` sets lock flags.

::: seealso
- [LREPLACE()]
- [LSET()]
:::
# LSET()
`lset(<object>/<locktype>,[!]<flag>)`

  This function sets or clears flags on locks and returns an empty string. It requires side effects to be enabled.

  See [@lset] for more information on what flags are available.


::: seealso
- [LOCKFLAGS()]
- [LLOCKFLAGS()]
- [LOCK()]
- [LOCKOWNER()]
:::
# LOG()
`log(<number>[, <base>])`

  Returns the logarithm (base 10, or the given base) of `<number>`. `<base>` can be a floating-point number, or 'e' for the natural logarithm.


::: seealso
- [LN()]
:::
# LPARENT()
`lparent(<object>)`

  This function returns a list consisting of `<object>`'s dbref (as per num()), the dbref of its parent, grandparent, greatgrandparent, etc. The list will not, however, show parents of objects which the player is not privileged to examine. Ancestor objects are not included.


::: seealso
- [PARENT()]
- [lsearch()]
- [parent]
- [ANCESTORS]
:::
# LPLAYERS()
`lplayers(<object>)`

  This function returns the dbrefs of all players, connected or not, in `<object>`. DARK wizards aren't listed to mortals or those without the see_all power. You must be in `<object>` or control it to use this function.


::: seealso
- [LVPLAYERS()]
- [LCON()]
- [LTHINGS()]
:::
# LTHINGS()
# LOBJECTS()
`lthings(<object>)`

  This function returns the dbrefs of all things, dark or not, in `<object>`. You must be in `<object>` or control it to use this function.


::: seealso
- [LVTHINGS()]
- [LCON()]
:::
# LPOS()
`lpos(<string>, <character>)`

  This function returns a list of the positions where `<character>` appears in `<string>`, with the first character of the string being 0. Note that this differs from the pos() function, but is consistent with other string functions like mid() and strdelete().

  If `<character>` is a null argument, space is used. If `<character>` is not found anywhere in `<string>`, an empty list is returned.

  Example:
```sharp
say lpos(a-bc-def-g, -)
You say, "1 4 8"
```


::: seealso
- [POS()]
- [MEMBER()]
- [element()]
- [WORDPOS()]
:::
# LSTATS()
# STATS()
`lstats([<player>])`

  This function returns the breakdown of objects in the database, in a format similar to "@stats". If `<player>` is "all" (the default), a breakdown is done for the entire database. Otherwise, the breakdown is returned for that particular player.

  Only wizards and those with the Search power can LSTATS() other players. For the whole database the list returned is in the format:<br>
  `<Total objects>` `<Rooms>` `<Exits>` `<Things>` `<Players>` `<Garbage>`<br>
  For a single player the garbage column is omitted:<br>
  `<Total objects>` `<Rooms>` `<Exits>` `<Things>` `<Players>`

  PennMUSH's own help lists six columns for both forms, but fun_lstats prints the garbage column
  only for the whole database; SharpMUSH follows the code. A destroyed object is removed here
  rather than kept as garbage, so that column is always 0.

  stats() is an alias for lstats().

::: seealso
- [lsearch()]
:::
# LT()
`lt(<number1>, <number2>[, ... , <numberN>])`

  Takes two or more numbers, and returns 1 if and only if each number is less than the number after it, and 0 otherwise.

  Examples:
```sharp
th lt(1,2)
1
th lt(1,2,3)
1
th lt(1,3,2)
0
```


::: seealso
- [LTE()]
- [GT()]
- [GTE()]
- [LNUM()]
- [LMATH()]
:::
# LTE()
`lte(<number1>, <number2>[, ... , <numberN>])`

  Takes two or more numbers, and returns 1 if and only if each number is less than or equal to the number after it, and 0 otherwise.


::: seealso
- [LT()]
- [GT()]
- [GTE()]
- [LNUM()]
- [LMATH()]
:::
# LVCON()
`lvcon(<object>)`

  This function returns the dbrefs of all objects that are inside `<object>` and visible (non-dark). You must be in `<object>` or control it to use this function.


::: seealso
- [LCON()]
- [LVPLAYERS()]
- [LVTHINGS()]
- [LVEXITS()]
:::
# LVEXITS()
`lvexits(<room>)`

  This function returns the dbrefs of all visible (non-dark) exits from `<room>`. You must be in the room or control it to use this function.


::: seealso
- [LEXITS()]
- [LVCON()]
- [LVPLAYERS()]
- [LVTHINGS()]
:::
# LVPLAYERS()
`lvplayers(<object>)`

  This function returns the dbrefs of all connected and non-dark players in an object. You must be in the object or control it to use this function.


::: seealso
- [LPLAYERS()]
- [LVCON()]
- [LVTHINGS()]
- [LVEXITS()]
:::
# LVTHINGS()
# LVOBJECTS()
`lvthings(<object>)`

  This function returns the dbrefs of all non-dark things inside an object. You must be in the object or control it to use this function.


::: seealso
- [LTHINGS()]
- [LVPLAYERS()]
- [LVCON()]
- [LVEXITS()]
:::
# LWHO()
# LWHOID()
`lwho([<viewer>[, <status>]])`<br>
`lwhoid([<viewer>[, <status>]])`

  lwho() returns a list of the dbref numbers for all currently-connected players. When mortals use this function, the dbref numbers of hidden wizards or royalty do NOT appear on the dbref list.

  If a `<viewer>` is given, and used by a See_All object, lwho() returns the output of lwho() from `<viewer>`'s point of view.

  `<status>` can be used to include "#-1" dbrefs for unconnected ports, and must be one of "all", "online" (the default) or "offline". It is primarily useful when using a `<status>` with lports(), to make the dbrefs and ports match up. Only See_All players can see offline dbrefs.

  lwhoid() returns a list of objid's instead.


::: seealso
- [MWHO()]
- [NMWHO()]
- [XWHO()]
- [LPORTS()]
:::
# MAP()
`map([<object>/]<attribute>, <list>[, <delim>[, <osep>]])`

  This function works much like ITER(). The given `<attribute>` is evaluated once for each element of `<list>`, and the results of the evaluations are returned. For each evaluation, the current list element is passed to the attribute as %0, and its position in the list as %1. Elements of `<list>` are separated by `<delim>`, or a space if none is given, and the results are returned separated by `<osep>`, if given, or the delimiter otherwise.

  This is roughly equivilent to, though slightly more efficient than:<br>
`iter(<list>, ulambda(<object>/<attribute>, %i0, inum(0)), <delim>, <osep>)`

  Examples:
```sharp
&times_two me=mul(%0,2)
```

    > say map(times_two, 5 4 3 2 1)<br>
    You say, "10 8 6 4 2"

    > say map(times_two,1;2;3;4;5,;)<br>
    You say, "2;4;6;8;10"


::: seealso
- [anonymous attributes]
- [iter()]
- [@dolist]
:::
# MAX()
`max(<number1>[, ... , <numberN>])`

  This function returns the largest number in its list of arguments. It can take any number of arguments.


::: seealso
- [MIN()]
- [LMATH()]
- [BOUND()]
- [ALPHAMAX()]
:::
# AVG()
# MEAN()
`mean(<number1>[, ... , <numberN>])`

  Returns the mean (arithmetic average) of its arguments.

  avg() is an alias for mean(), for Rhost compatibility.


::: seealso
- [MEDIAN()]
- [STDDEV()]
- [LMATH()]
:::
# MEDIAN()
`median(<number>[, ... , <numberN>])`

  Returns the median (the middlemost numerically) of its arguments.


::: seealso
- [AVG()]
- [STDDEV()]
- [LMATH()]
:::
# MEMBER()
`member(<list>, <word>[, <delimiter>])`

  member() returns the position where `<word>` first occurs in `<list>`. If `<word>` is not present in `<list>`, it returns 0. Elements of `<list>` are `<delimiter>`-separated, or space-separated if no `<delimiter>` is given.

  member() is case-sensitive, and does not perform wildcard matching. If you need to do a wildcard match, use match(). To compare two strings (instead of a word and list elements), consider comp().


::: seealso
- [element()]
- [GRAB()]
- [COMP()]
- [STRMATCH()]
:::
# MERGE()
`merge(<string1>, <string2>, <characters>)`

  This function merges `<string1>` and `<string2>`, depending on `<characters>`. If a character in `<string1>` is the same as one in `<characters>`, it is replaced by the character in the corresponding position in `<string2>`. The two strings must be of the same length.

  Example:
```sharp
say merge(AB--EF,abcdef,-)
You say, "ABcdEF"
```

  Spaces need to be treated specially. An empty argument is considered to equal a space, for `<characters>`.

  Example:
```sharp
say merge(AB[space(2)]EF,abcdef,)
You say, "ABcdEF"
```


::: seealso
- [SPLICE()]
- [TR()]
:::
# MESSAGE()
`message(<recipients>, <message>, [<object>/]<attribute>[, <arg0>[, ... , <arg9>][, <switches>]])`

  message() is the function form of @message/silent, and sends a message, formatted through an attribute, to a list of objects. See [@message] for more information.

  `<switches>` is a space-separated list of one or more of "nospoof", "spoof", "oemit" and "remit", and makes message() behaviour as per @message/`<switches>`. For backwards-compatability reasons, all ten `<arg>` arguments must be given (even if empty) to use `<switches>`.

  Examples:
```sharp
&formatter #123
think message(me, Default> foo bar baz, #123/formatter, foo bar baz)
Foo Bar Baz
&formatter #123=Formatted> [iter(%0,capstr(%i0))]
think message(me, Default> foo bar baz, #123/formatter, foo bar baz)
Formatted> Foo Bar Baz
```

  > think message(here, default, #123/formatter, backwards compatability is annoying sometimes,,,,,,,,,,remit)<br>
  Formatted> Backwards Compatability Is Annoying Sometimes


::: seealso
- [@message]
- [OEMIT()]
- [REMIT()]
- [speak()]
:::
# MID()
`mid(<string>, <first>, <length>)`

  mid() returns `<length>` characters from `<string>`, starting from the `<first>` character. If `<length>` is positive, it counts forwards from the `<first>` character; for negative `<length>`s, it counts backwards. Note that the first character in `<string>` is numbered 0, not 1.

  Examples:
```sharp
say mid(testing, 2, 2)
You say, "st"
say mid(testing, 2, -2)
You say, "es"
```


::: seealso
- [LEFT()]
- [RIGHT()]
- [STRDELETE()]
:::
# MIN()
`min(<number1>[, ... , <numberN>])`

  This function returns the smallest number in its list of arguments. It can take any number of arguments.


::: seealso
- [MAX()]
- [LMATH()]
- [BOUND()]
- [ALPHAMIN()]
:::
# MOD()
# MODULO()
# MODULUS()
# REMAINDER()
`modulo(<number>, <number>[, ..., <numberN>])`<br>
`remainder(<number>, <number>[, ..., <numberN>])`

  remainder() returns the remainder of the integer division of the first number by the second (and subsequent) number(s) (ie, the remainder from calling div() with the same arguments).

  modulo() returns the modulo of the given numbers (from calling floordiv() with the same arguments).

  For positive numbers, these are the same, but they may be different for negative numbers:

     modulo(13,4)       ==>  1      and     remainder(13,4)    ==>  1<br>
     modulo(-13,4)      ==>  3      but     remainder(-13,4)   ==>  -1<br>
     modulo(13,-4)      ==>  -3     but     remainder(13,-4)   ==>  1<br>
     modulo(-13,-4)     ==>  -1     and     remainder(-13,-4)  ==>  -1

  remainder()s result always has the same sign as the first argument. modulo()s result always has the same sign as the second argument.

  mod() and modulus() are aliases for modulo().


::: seealso
- [DIV()]
- [LMATH()]
:::
# MONEY()
`money(<integer>)`<br>
`money(<object>)`

  SharpMUSH does not track money, so money() always returns `#-1 NOT SUPPORTED` and tells you so. In PennMUSH it returns the name of an amount of money, or the pennies `<object>` holds. See [compatibility economy].

  Example:
```sharp
> think money(me)
#-1 NOT SUPPORTED
```


::: seealso
- [score]
:::
# MTIME()
# MSECS()
`mtime(<object>[, <utc?>])`<br>
`msecs(<object>[, <precision>])`

  mtime() returns the date and time that one of `<object>`'s attributes or locks was last added, deleted, or modified. The time returned is in the server's local timezone, unless `<utc?>` is true, in which case the time is in the UTC timezone.

  msecs() returns the time as the number of seconds since the epoch.

  Only things, rooms, and exits have modification times. You must be able to examine an object to see its modification time.


::: seealso
- [CTIME()]
- [time()]
- [SECS()]
- [CONVTIME()]
- [CONVSECS()]
:::
# MUDNAME()
# MUDURL()
`mudname()`<br>
`mudurl()`

  These functions return the name of the MUSH and the MUSH's website address, as set in the 'mud_name' and 'mud_url' @config options.

  Example:
```sharp
say mudname()
You say, "TestMUSH"
say mudurl()
You say, "http://www.testmush.com"
```


::: seealso
- [CONFIG()]
:::
# MUL()
`mul(<number1>, <number2>[, ... , <numberN>])`

  Returns the product of some numbers.


::: seealso
- [LMATH()]
- [DIV()]
- [DIV()]
:::
# MWHO()
# MWHOID()
`mwho()`<br>
`mwhoid()`

  mwho() returns a list of the dbref numbers for all current-connected, non-hidden players. It's exactly the same as lwho() used by a mortal, and is suitable for use on privileged global objects who need an unprivileged who-list. In some cases, lwho(`<viewer>`) may be preferable to mwho(), as it includes hidden players for `<viewer>`s who can see them.

  mwhoid() returns a list of objids instead.


::: seealso
- [LWHO()]
- [NMWHO()]
:::
# ALIAS()
# FULLALIAS()
`alias(<object>[, <new alias>])`<br>
`fullalias(<object>)`

  alias() returns the first of `<object>`'s aliases. fullalias() returns all the aliases set for `<object>`. Note that, while any object can have an alias set, they are only meaningful for players and exits.

  With two arguments, alias() attempts to change the alias for `<object>` to `<new alias>`, as per @alias.

  Examples:
```sharp
ex *Noltar/ALIAS
ALIAS [#7$v]: $;No;Nol;Noli;Nolt
say alias(*Noltar)
You say, "$"
say fullalias(*Noltar)
You say, "$;No;Nol;Noli;Nolt"
```


::: seealso
- [FULLNAME()]
:::
# NAME()
`name(<object>[, <new name>])`

  name() returns the name of object `<object>`. For exits, name() returns only the displayed name of the exit.

  With two arguments, name() attempts to rename `<object>` to `<new name>`, as per @name.


::: seealso
- [FULLNAME()]
- [ACCNAME()]
- [INAME()]
- [ALIAS()]
- [MONIKER()]
:::
# MONIKER()
# CNAME()
`moniker(<object>)`

  Returns `<object>`'s accented name, with the color template from its @moniker applied. moniker() always returns the colored name, even if monikers are disabled via @config.


::: seealso
- [monikers]
- [@moniker]
- [NAME()]
- [MONIKER()]
- [INAME()]
- [ACCNAME()]
:::
# NAMELIST()
`namelist(<player-list>[, [<object>/]<attribute>])`

  namelist() takes a list of players of the form used by the page command and returns a corresponding list of dbrefs. Invalid and ambiguous names return the dbrefs #-1 and #-2, respectively.

  If an `<object>`/`<attribute>` is given, the specified attribute will be called once for each invalid name, with the name as %0 and the dbref returned (#-1 for an unmatched name, #-2 for an ambiguous one) as %1.

  Example:
```sharp
&test me=pemit(%#,Bad name "%0")
say namelist(#1 Javelin "ringo spar" bogus, test)
Bad name "bogus"
You say, "#1 #7 #56 #-1"
```


::: seealso
- [NAMEGRAB()]
- [NAME()]
- [locate()]
- [NUM()]
- [PMATCH()]
:::
# NAMEGRAB()
# NAMEGRABALL()
`namegrab(<dbref list>, <name>[, <delimiter>])`<br>
`namegraball(<dbref list>, <name>[, <delimiter>])`

  The namegrab() function returns the first dbref in the list that would match `<name>` as if you were checking num() or locate(). An exact match has priority over partial matches.

  namegraball() returns all dbrefs whose names would be matched by `<name>`.

  Examples: #0 = Room Zero, #1 = One, #2 = Master Room<br>
    > say namegrab(#0 #1 #2,room)<br>
    You say, "#0"<br>
    > say namegrab(#0 #1 #2,master room)<br>
    You say, "#2"<br>
    > say namegraball(#0 #1 #2,room)<br>
    You say, "#0 #2"


::: seealso
- [NAMELIST()]
- [locate()]
:::
# NAND()
# NCAND()
# CNAND()
`nand(<boolean1>[, ... , <booleanN>])`<br>
`ncand(<boolean1>[, ... , <booleanN>])`<br>
`cnand(<boolean1>[, ... , <booleanN>])`

  These functions return 1 if at least one of their arguments are false, and 0 if all are true. nand() always evaluates all of its arguments, while ncand() stops evaluating after the first false value. cnand() is a SharpMUSH spelling of ncand(), for code written against servers that name the cancelling form that way; PennMUSH has no cnand().

  Equivalent to not(and()) and not(cand()), but more efficient.


::: seealso
- [LMATH()]
- [AND()]
- [AND()]
- [OR()]
- [NOR()]
:::
# NEARBY()
`nearby(<object 1>, <object 2>)`

  Returns 1 if `<object 1>` is "nearby" `<object 2>`, and 0 otherwise. "Nearby" means the objects are in the same location, or that one is located inside the other. You must control at least one of the objects; if you don't, or if one of the objects can't be found, nearby() returns #-1.


::: seealso
- [locate()]
- [FINDABLE()]
:::
# NEQ()
`neq(<number1>, <number2>[, ... , <numberN>])`

  Returns 0 if all the given `<number>`s are the same, and 1 otherwise. Basically the same as [not(eq(`<number1>`, `<number2>`[, ... , `<numberN>`]))] but more efficient.


::: seealso
- [EQ()]
- [NOT()]
- [LMATH()]
:::
# NEXT()
`next(<object>)`

  If `<object>` is an exit, then next() will return the next exit in `<object>`'s source room. If `<object>` is a thing or a player, then next() will return the next object in the contents list of `<object>`'s location. Otherwise, it returns a #-1. #-1 is also used to denote that there are no more exits or objects after `<object>`.

  You can get the complete contents of any container you may examine, regardless of whether or not objects are dark. You can get the partial contents (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the contents of anything else, regardless of whether or not you have objects in it. These rules apply to exits, as well.


::: seealso
- [LCON()]
- [LEXITS()]
- [CON()]
- [EXIT()]
:::
# NEXTDBREF()
`nextdbref()`

  This function returns the next dbref on the free list; when the next object is @created (or @dug, or @opened, or @pcreated, etc.), it will have this dbref.


::: seealso
- [@stats]
- [LSTATS()]
:::
# NOR()
# NCOR()
`nor(<boolean1>[, ... , <booleanN>])`<br>
`ncor(<boolean1>[, ... , <booleanN>])`

  These functions return 1 if all their arguments are false, and 0 if any are true. nor() always evaluates all arguments, while ncor() stops evaluating after the first true value.

  Equivalent to not(or()) and not(cor()), but more efficient.


::: seealso
- [AND()]
- [OR()]
- [XOR()]
- [NOT()]
- [NAND()]
- [LMATH()]
:::
# NOT()
`not(<boolean>)`

  not() returns 1 if `<boolean>` is false, and 0 if it's true.

  The definition of truth and falsehood depends on configuration settings; see [boolean values] for details.


::: seealso
- [Boolean functions]
- [T()]
- [AND()]
- [OR()]
- [NOR()]
- [XOR()]
:::
# NUM()
`num(<object>)`

  Returns the dbref number of `<object>`. `<object>` must reference a valid object, as per [MATCHING].


::: seealso
- [locate()]
- [RNUM()]
- [PMATCH()]
:::
# NVCON()
# NCON()
`ncon(<object>)`<br>
`nvcon(<object>)`

  These functions return a the number of objects inside `<object>`. They are identical to words(lcon(`<object>`)) and words(lvcon(`<object>`)), respectively, but are more efficient and do not suffer from buffer constraints.


::: seealso
- [NVEXITS()]
- [NVPLAYERS()]
- [XVCON()]
- [LCON()]
- [LVCON()]
:::
# NVEXITS()
# NEXITS()
`nexits(<room>)`<br>
`nvexits(<room>)`

  These functions return a count of the exits in a room. They are equivilent to words(lexits(`<room>`)) and words(lvexits(`<room>`)) respectively, though are more efficient, and don't suffer from buffer constraints.


::: seealso
- [NVCON()]
- [NVPLAYERS()]
- [XVEXITS()]
- [LEXITS()]
- [LVEXITS()]
:::
# NVPLAYERS()
# NPLAYERS()
`nplayers(<object>)`<br>
`nvplayers(<object>)`

  These functions return a count of the players in `<object>`. They are equivilent to words(lplayers(`<object>`)) and words(lvplayers(`<object>`)) respectively, though are more efficient and do not suffer from buffer constraints.


::: seealso
- [NVCON()]
- [NVEXITS()]
- [XVPLAYERS()]
- [LPLAYERS()]
- [LVPLAYERS()]
:::
# NVTHINGS()
# NTHINGS()
# NOBJECTS()
# NVOBJECTS()
`nthings(<object>)`<br>
`nvthings(<object>)`

  These functions return a count of the things in a container. They are equivilent to words(lthings(`<object>`)) and words(lvthings(`<object>`)) respectively, though are more efficient and do not suffer from buffer constraints.


::: seealso
- [NVCON()]
- [NVEXITS()]
- [XVTHINGS()]
- [LTHINGS()]
- [LVTHINGS()]
:::
# NMWHO()
# NWHO()
`nwho([<viewer>])`<br>
`nmwho()`

  nwho() returns a count of all currently-connected players. When mortals use this function, hidden players are NOT counted. See_All players can specify a `<viewer>` to get a count of the number of players that `<viewer>` can see is online.

  nmwho() returns a count of all currently connected, non-hidden players. It's exactly the same as nwho() used by a mortal, and is suitable for use on privileged global objects that always need an unprivileged count of who is online.

  These functions are equivilent to words(lwho([`<viewer>`])) and words(mwho()), but are more efficient, and don't suffer from buffer constraints.


::: seealso
- [LWHO()]
- [MWHO()]
- [XWHO()]
- [XWHO()]
:::
# OBJ()
# %o
`obj(<object>)`

  Returns the objective pronoun - him/her/it - for an object. The %o substitution will return the objective pronoun of the enactor.


::: seealso
- [SUBJ()]
- [POSS()]
- [APOSS()]
:::
# OBJEVAL()
`objeval(<object>, <expression>)`

  Allows you to evaluate `<expression>` from the viewpoint of `<object>`. If side-effect functions are enabled, you must control `<object>`; if not, you must either control `<object>` or have the see_all power. If `<object>` does not exist or you don't meet one of the criterion, the expression evaluates with your privileges.


::: seealso
- [S()]
:::
# OBJID()
`objid(<object>)`

  This function returns the object id of `<object>`, a value which uniquely identifies it for the life of the MUSH. The object id is the object's dbref, a colon character, and the object's creation time, in milliseconds since the epoch, equivalent to [num(`<object>`)]:[csecs(`<object>`,ms)] (PennMUSH stores seconds, so its objids end in [csecs(`<object>`)] instead; see help COMPATIBILITY IDENTITY)

  The object id can be used nearly anywhere the dbref can, and ensures that if an object's dbref is recycled, the new object won't be mistaken for the old object.

  The substitution %: returns the object id of the enactor.


::: seealso
- [NUM()]
- [CTIME()]
- [CTIME()]
- [%#]
:::
# OBJMEM()
`objmem(<object>)`

  This function returns the amount of memory, in bytes, being used by the object. It can only be used by players with Search powers.

  **Not implemented.** SharpMUSH always answers 0, whatever object it is asked about, so no caller can tell a large object from a small one.


::: seealso
- [PLAYERMEM()]
:::
# OEMIT()
# NSOEMIT()
`oemit([<room>/]<object> [... <object>], <message>)`<br>
`nsoemit([<room>/]<object> [... <object>], <message>)`

  Sends `<message>` to all objects in `<room>` (default is the location of `<object>`(s)) except `<object>`(s), as per @oemit.

  nsoemit() works like @nsoemit.

# OPEN()
`open(<exit name>[, <destination>[, <source>[, <dbref>]]])`

  This function attempts to open an exit named `<exit name>`. The exit will be opened in the room `<source>`, if given, or the caller's current location if no `<source>` is specified.

  If a `<destination>` is given, it will attempt to link the exit to `<destination>` after opening it.

  Wizards and objects with the pick_dbref power can specify a garbage dbref to use for the new exit.

  It returns the dbref of the newly created exit, or #-1 on error.


::: seealso
- [@open]
- [@link]
- [DIG()]
- [LINK()]
- [CREATE()]
- [PCREATE()]
:::
# OR()
# COR()
`or(<boolean1>, <boolean2>[, ... , <booleanN>])`<br>
`cor(<boolean1>, <boolean2>[, ... , <booleanN>])`

  These functions take a number of boolean values, and return 1 if any of them are true, and 0 if all are false. or() always evaluates all of its arguments, while cor() stops evaluating as soon as one is true.

  Prefer cor(): it skips work the answer no longer needs. Use or() only when every argument has a side effect that must run.


::: seealso
- [boolean values]
- [AND()]
- [NOR()]
- [FIRSTOF()]
- [ALLOF()]
- [LMATH()]
:::
# ORFLAGS()
# ORLFLAGS()
`orflags(<object>, <string of flag characters>)`<br>
`orlflags(<object>, <list of flag names>)`

  These functions return 1 if `<object>` has any of the given flags, and 0 if it does not. orflags() takes a string of single flag letters, while orlflags() takes a space-separated list of flag names. In both cases, a ! before the flag means "not flag".

  If there is a syntax error like a ! without a following flag, '#-1 INVALID FLAG' is returned. Unknown flags are treated as being not set.

  Examples: Check to see if %# is set Wizard, Dark, or not set Ansi.<br>
    > say orflags(%#, WD!A)<br>
    > say orlflags(%#, wizard dark !ansi)


::: seealso
- [ANDFLAGS()]
- [FLAGS()]
- [LFLAGS()]
- [ORLPOWERS()]
:::
# ORLPOWERS()
`orlpowers(<object>, <list of powers>)`

  This function returns 1 if `<object>` has at least one of the powers in a specified list, and 0 if it does not. The list is a space-separated list of power names. A '!' preceding a flag name means "not power".

  Thus, ORLPOWERS(me, poll login) would return 1 if I have the poll and login powers. ORLFLAGS(me, functions !guest) would return 1 if I have the functions power or are not a guest.

  If there is a syntax error like a ! without a following power, '#-1 INVALID POWER' is returned. Unknown powers are treated as being not set.


::: seealso
- [POWERS()]
- [ANDLPOWERS()]
- [@power]
- [@power]
- [ORFLAGS()]
:::
# OWNER()
`owner(<object>[/<attribute>])`<br>
`owner(<object>[/<attribute>], <new owner>[, preserve])`

  Given just an object, it returns the owner of the object. Given an object/attribute pair, it returns the owner of that attribute.

  If `<new owner>` is specified, the ownership is changed, as in @chown or @atrchown. If the optional third argument is "preserve", privileged flags and powers will be preserved ala @chown/preserve.<br>
  If changing ownership, #-1 or 0 is returned on failure, 1 on success.


::: seealso
- [LOCKOWNER()]
- [@chown]
- [@atrchown]
:::

# PARENT()
`parent(<object>[, <new parent>])`

  This function returns the dbref number of an object's parent. You must be able to examine the object to do this. If you specify a second argument, parent() attempts to change the parent first. You must control `<object>`, and be allowed to @parent to `<new parent>`.


::: seealso
- [@parent]
- [ANCESTORS]
- [pfun()]
- [LPARENT()]
:::
# PEMIT()
# NSPEMIT()
# PROMPT()
# NSPROMPT()
`pemit(<object list|port numbers>, <message>)`<br>
`nspemit(<object list|port numbers>, <message>)`<br>
`prompt(<object list>, <message>)`<br>
`nsprompt(<object list>, <message>)`

  With an `<object list>`, pemit() will send each object on the list a message, as per the @pemit/list command. It returns nothing. It respects page-locks and HAVEN flags on players. With `<port numbers>`, pemit() sends the message to the specified ports only, like @pemit/port/list.

  nspemit() works like @nspemit/list.

  prompt() adds a telnet GOAHEAD to the end of the message, as per the @prompt command. nsprompt() that works like @nsprompt.


::: seealso
- [@prompt]
- [@nspemit]
- [PROMPT_NEWLINES]
:::
# PERMISSION()
`permission(<object>, <permission>)`

  Returns 1 if `<object>` holds `<permission>`, and 0 if not. The answer is the same one the game and the web portal use when that object tries the action, so softcode can check ahead of time instead of keeping its own list of staff. `<permission>` may be built in or one the game defined with `@permission/define`, such as `scene.close`. Any other name returns `#-1 NO SUCH PERMISSION`; @permission lists them all.

  Example:
```sharp
think permission(me, wiki.delete)
0
```


::: seealso
- [HASROLE()]
- [roles]
- [@role]
:::
# PI()
`pi()`

  Returns the value of "pi" (3.14159265358979323846264338327, rounded to the game's float_precision setting).
# PIDINFO()
`pidinfo(<pid>[, <list of fields>[, <output separator>]])`

  This function returns information about a process id if the player has permission to see the process. The `<list of fields>` is a space-separated list that may contain the following elements:

    queue       the queue ("wait" or "semaphore") for the process<br>
    executor    the queueing object<br>
    time        remaining time for timed queued entries (or -1)<br>
    object      the semaphore object for semaphores (or #-1)<br>
    attribute   the semaphore attribute for semaphores (or #-1)<br>
    command     the queued command

  If `<list of fields>` is not provided, all fields are returned. The fields are separated by `<output separator>`, which defaults to a space.


::: seealso
- [@ps]
- [LPIDS()]
- [GETPIDS()]
:::
# PLAYERMEM()
`playermem(<player>)`

  This function returns the amount of memory, in bytes, being used by everything owned by the player. It can only be used by players with Search powers.


::: seealso
- [OBJMEM()]
:::
# PLAYER()
`player(<port>)`

  Returns the dbref of the player connected to a given port. Mortals can only use this function on their own ports, while See_All players can use it on any port.


::: seealso
- [LPORTS()]
- [LPORTS()]
:::
# PMATCH()
`pmatch(<name>)`

  pmatch() attempts to find a player called `<name>`, which should be the full or partial name of a player (possibly prefixed with a "*") or a dbref. First, it checks to see if `<name>` is the dbref, full name, or alias of a player; if so, their dbref is returned. Otherwise, it checks for partial matches against the names of online players. If there are no matches, #-1 is returned. If there are multiple matches, pmatch() returns #-2. Otherwise, the matching player's dbref is returned.

  pmatch() does not check for the string "me". If you wish to do that, you should use locate (for example, locate(`<player>`, `<name>`, PFym)).


::: seealso
- [NUM()]
- [NAMELIST()]
- [locate()]
:::
# MOTD()
# WIZMOTD()
# DOWNMOTD()
# FULLMOTD()
`motd()`<br>
`wizmotd()`<br>
`downmotd()`<br>
`fullmotd()`

  These functions return the Message of the Day that [@motd] set, one function per `<type>`: motd() the connect MotD, wizmotd() the wizard one, downmotd() the one shown when logins are disabled, and fullmotd() the one shown when every connection is in use. A MotD that has not been set returns the empty string.

  motd() is readable by anyone, since every player sees the connect MotD on the way in. The other three are Wizard-only and return `#-1 PERMISSION DENIED` to anyone else, matching who [@motd/list] shows them to.

  These are SharpMUSH functions; PennMUSH exposes the same text only through [@motd].


::: seealso
- [@motd]
- [POLL()]
- [@poll]
:::
# POLL()
`poll()`

  This function returns the current @poll.


::: seealso
- [@poll]
- [DOING()]
- [@doing]
:::
# LPIDS()
`lpids([<object>[, <queue types>]])`

  This function returns a list of queue process ids (pids). Only commands queued by objects with the same owner as `<object>` are listed. If you have the see_queue @power, you can specify "all" for `<object>` to get pids for everyone's queue entries. `<object>` defaults to the caller, or "all" for priviledged callers.

  `<queue types>` should be a list of one or more of the following words, to filter the pids returned:<br>
    wait        --  Only return wait queues<br>
    semaphore   --  Only return semaphore queues<br>
    independent --  Only return commands queued by `<object>` specifically, instead of all objects with the same owner as `<object>`.<br>
  If not specified, it defaults to "wait semaphore".


::: seealso
- [@ps]
- [GETPIDS()]
- [PIDINFO()]
:::
# LPORTS()
# PORTS()
`lports([<viewer>[, <status>]])`<br>
`ports(<player name>)`

  These functions return the list of descriptors ("ports") that are used by connected players. lports() returns all ports, in the same order as lwho() returns dbrefs, and ports() returns those a specific player is connected to, from most recent to least recent. Mortals can use ports() on themselves, but only See_All players can use ports() on others, or use lports().

  If lports() is given a `<viewer>`, only the ports of connections which `<viewer>` can see are returned, in the same way as lwho(`<viewer>`) works.

  The `<status>` argument for lports() controls whether or not ports which are not connected to (ie, at the login screen) are included, and must be one of "all", "online" or "offline".

  These port numbers also appear in the wizard WHO, and can be used with @boot/port, page/port, and the functions that return information about a connection to make them use a specific connection rather than the least-idle one when a player has multiple connections open. Players can get information about their own connections. See_all is needed to use them to get information about other people's ports.


::: seealso
- [LWHO()]
- [PLAYER()]
- [Connection functions]
:::
# POS()
`pos(<needle>, <haystack>)`

  This function returns the position that `<needle>` begins in `<haystack>`. Unlike most other string functions, the first character of `<haystack>` is numbered 1, not 0. If `<needle>` is not present in `<haystack>`, pos() returns #-1.


::: seealso
- [MEMBER()]
- [element()]
- [LPOS()]
- [WORDPOS()]
:::
# POSS()
# %p
`poss(<object>)`

  Returns the possessive pronoun - his/her/its - for an object. The %p substitution also returns the possessive pronoun of the enactor.


::: seealso
- [SUBJ()]
- [OBJ()]
- [APOSS()]
:::
# POWER()
`power(<number>, <exponent>)`

  Returns `<number>` to the power of `<exponent>`.

  (For the functional version of @power, see [POWERS()].)


::: seealso
- [ROOT()]
:::
# POWERS()
`powers()`<br>
`powers(<object>)`<br>
`powers(<object>, <power>)`

  With no arguments, powers() returns a space-separated list of all defined @powers on the MUSH. With one argument, it returns a list of the powers possessed by `<object>`.

  With two arguments, it attempts to set `<power>` on `<object>`, as per @power `<object>`=`<power>`.


::: seealso
- [ANDLPOWERS()]
- [ORLPOWERS()]
- [@power]
- [@power]
:::
# QUOTA()
`quota(<player>)`

  Returns the player's quota, the maximum number of objects they can create if quotas are in effect. Returns 99999 for players with the No_Quota @power, so it's safe to use in numerical comparisons.

  You must control `<player>` or have the See_All or Quotas @powers to use this function.


::: seealso
- [@quota]
- [@quota administrative quota changes]
- [@quota administrative quota changes]
- [QUOTAS]
- [@power]
- [@power]
:::
# R()
# %q
# R-FUNCTION
`r(<register>[, <type>])`

  The r() function can be used to access registers. It can retrieve the value of q-registers set with setq() and related functions, as well as the 30 stack values (the first ten of which are also available via %0-%9), and also iter() and switch() context (also available through itext() and stext(), respectively). The registers() function can be used to obtain a list of available registers.

  `<type>` defaults to "qregisters", and must be one of:

    qregisters - registers set with setq(), setr() and similar functions<br>
    args       - the stack, usually accessed via %0-%9. There are up to 30 stack registers, plus named stack registers from regexp $-commands<br>
    iter       - itext() context from iter() or @dolist. Must be an int, or "L" for the outermost itext().<br>
    switch     - stext() context from switch() or @switch. Must be an int, or "L" for the outermost stext()<br>
    regexp     - regexp capture names from re*() regexp functions

  qregisters can also be accessed via the %qX (for one-char register names) or %q`\<X\>` (for registers with longer names) substitutions.


::: seealso
- [setq()]
- [LETQ()]
- [LISTQ()]
- [LISTQ()]
- [REGISTERS()]
- [V()]
- [ilev()]
- [STEXT()]
- [ilev()]
- [STEXT()]
:::
# RAND()
`rand()`<br>
`rand(<num>)`<br>
`rand(<min>, <max>)`

  Return a random number.

  The first form returns a floating-point number in the range 0 <= n < 1.

  The second form returns an integer between 0 and `<num>`-1, inclusive (or between 0 and `<num>`+1, for negative `<num>`s).

  The third returns an integer between `<min>` and `<max>`, inclusive.

  If called with an invalid argument, rand() returns an error message<br>
  beginning with #-1.


::: seealso
- [RANDWORD()]
:::
# RANDWORD()
# PICKRAND()
`randword(<list>[, <delimiter>])`

  Returns a randomly selected element from `<list>`. Elements of the list are separated by `<delimiter>`, which defaults to a space.

  pickrand() is an alias for randword().


::: seealso
- [RAND()]
- [RANDEXTRACT()]
:::
# RANDEXTRACT()
`randextract(<list>[, <count>[, <delim>[, <type>[, <osep>]]]])`

  Returns up to `<count>` random elements from the `<delim>`-separated `<list>`. The following `<type>`s are available:<br>
    R - Grab `<count>` elements from `<list>` at random, but don't duplicate any elements<br>
    L - Grab `<count>` elements from `<list>`, in order, starting at a random element<br>
    D - Grab `<count>` elements from `<list>` at random, with duplicates allowed

  randextract() may return less than `<count>` elements for `<type>`s L and R, depending on the random element chosen and the length of `<list>`. Elements of the returned list are separated by `<osep>`, which defaults to `<delim>`. `<delim>` defaults to a single space, `<count>` defaults to 1, and `<type>` defaults to R.

  Examples:
```sharp
say randextract(this is a test,3)
You say "this test a"
say randextract(this@is@a@test,3,@)
You say "this@a@test"
say randextract(this is a test,3,,L,*)
You say "this*is*a"
say randextract(this is a test,6,,D)
You say, "this test is this is is"
```


::: seealso
- [RAND()]
- [RANDWORD()]
:::
# REGEDIT()
# REGEDITALL()
# REGEDITI()
# REGEDITALLI()
`regedit(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`<br>
`regediti(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`<br>
`regeditall(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`<br>
`regeditalli(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`

  These functions edit `<string>`, replacing the part of the string which matches the regular expression `<regexp>` with the accompanying `<replace>`. In `<replace>`, the string "$`<number>`" is expanded during evaluation to the `<number>`th sub-expression, with $0 being the entire matched section. If you use named sub-expressions `(?<name>subexpr)`, they are referred to with "$`<name>`". Note that, with named sub-expressions, the "<>" are literal.

  regedit() only replaces the first match, while regeditall() replaces all matches. The versions ending in i are case insensitive. The `<replace>` argument is evaluated once for each match, allowing for more complex transformations than is possible with straight replacement.

  Escape grouping parentheses for the default MUSH argument parser with `%(`/`%)`, as shown below. See [regexp syntax] for other escaping rules.

  Examples:
```sharp
say regedit(this test is the best string, %(?<char>.%)est, $<char>rash)
You say "this trash is the best string"
say regeditall(this test is the best string, %(.%)est, capstr($1)rash)
You say "this Trash is the Brash string"
```


::: seealso
- [EDIT()]
- [@edit]
- [regmatch()]
- [GRAB()]
:::
# REGREPLACE()
`regreplace(<string>, <regexp>, <replacement>[, <flags>])`

  Replaces every part of `<string>` that matches `<regexp>` with `<replacement>`, and returns the result. `<replacement>` may refer to captured groups with `$1`, `$2` and so on, or by name with `${name}`.

  `<flags>` is a string of letters; only `i` (match case-insensitively) is meaningful. Replacement is always global, so a `g` is accepted and changes nothing. An invalid `<regexp>` returns `#-1 INVALID REGEX`.

  Its replacement tokens and numeric group order are those of .NET, unlike `regedit()` softcode captures. Use `lit()` when the replacement contains literal braces, for example `lit(${name})`.

  This is a SharpMUSH function. PennMUSH spells the same idea [REGEDIT()], which takes alternating pattern/replacement pairs instead of a flags argument and uses `$1` / `$<name>` softcode capture substitutions.


::: seealso
- [REGEDIT()]
- [regmatch()]
- [regexp syntax]
:::
# REMIT()
# NSREMIT()
`remit(<object list>, <message>)`<br>
`nsremit(<object list>, <message>)`

  Sends a message to the contents of all the objects specified in `<object list>`, as per @remit/list.

  nsremit() works like @nsremit/list.


::: seealso
- [@remit]
- [PEMIT()]
- [NSLEMIT()]
:::
# REMOVE()
`remove(<list>, <words>[, <delimiter>])`

  This function removes the first occurrence of every word in the list `<words>` from `<list>`, and returns the resulting `<list>`. It is case sensitive.

  Elements of `<list>` and `<words>` are both separated by `<delimiter>`, which defaults to a space.


::: seealso
- [INSERT()]
- [LDELETE()]
- [SETDIFF()]
:::
# RENDER()
`render(<string>, <formats>)`

  This function renders the given `<string>` into a given format. Most useful when coding bots, or inserting text into an SQL database to display on a website. `<formats>` is a space-separated list of one or more of the following:

    ansi      --  Convert colors to raw ANSI tags (requires Can_Spoof power)<br>
    html      --  Escape HTML entities (< to &lt;, etc) and convert Pueblo to HTML tags<br>
    noaccents --  Downgrade accented characters, as per stripaccents()<br>
    markup    --  Leave any markup not already converted by ansi/html as internal markup tags. Without this, unhandled markup<br>
                  will be stripped, as per stripansi()

  Examples:
```sharp
say render(<Test 1> & [tagwrap(u,Test 2)], html)
You say, "&lt;Test 1&gt; &amp; \<u\>Test 2</u>"
```


::: seealso
- [STRIPACCENTS()]
- [STRIPANSI()]
- [pueblo]
- [@sql]
- [TAGWRAP()]
- [json()]
:::
# REPEAT()
`repeat(<string>, <number>)`

  This function simply repeats `<string>`, `<number>` times. No spaces are inserted between each repetition.

  Example:
```sharp
say repeat(Test, 5)
You say, "TestTestTestTestTest"
```


::: seealso
- [SPACE()]
:::
# LREPLACE()
# REPLACE()
`lreplace(<list>, <position(s)>, <new item>[, <delimiter>[, <osep>]])`

  This replaces the item(s) at the given `<position(s)>` in `<list>` with `<new item>`. `<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`. Null items are counted when determining position.

  If `<position>` is negative, it counts backwards from the end of the list. A `<position>` of -1 will replace the last element, -2 the element before last, and so on.

  Examples:
```sharp
say lreplace(Turn north at the junction,2,south)
You say, "Turn south at the junction"
```

    > say lreplace(Turn north at the junction,-1,crossroads)<br>
    You say, "Turn north at the crossroads"

    > say lreplace(blue|red|green|yellow,3,white,|)<br>
    You say, "blue|red|white|yellow"

    > say lreplace(this starts and ends the same, 1 -1, foo)<br>
    You say, "foo starts and ends the foo"

  replace() is an alias for lreplace(), for backwards compatability.


::: seealso
- [LDELETE()]
- [INSERT()]
- [SETDIFF()]
- [SPLICE()]
- [STRREPLACE()]
:::
# REST()
`rest(<list>[, <delimiter>])`

  Returns a list minus its first element.


::: seealso
- [AFTER()]
- [FIRST()]
- [LAST()]
:::
# REVWORDS()
`revwords(<list>[, <delimiter>[, <output separator>]])`

  This function reverses the order of words in a list. List elements are separated by `<delimiter>`, which defaults to a space. Elements in the reversed list are separated by `<ouput separator>`, which defaults to the delimiter.

  Example:
```sharp
say revwords(foo bar baz eep)
You say, "eep baz bar foo"
```


::: seealso
- [FLIP()]
:::
# RIGHT()
`right(<string>, <length>)`

  Returns the `<length>` rightmost characters from `<string>`.


::: seealso
- [LEFT()]
- [MID()]
:::
# RJUST()
`rjust(<string>, <length>[, <fill>[, <truncate?>]])`

  This function returns `<string>`, padded on the left with the string `<fill>` until it's `<length>` characters long. `<fill>` can be more than one character in length, and defaults to a single space.

  If `<string>` is longer than `<length>`, it will be returned unaltered, unless `<truncate?>` is true, in which case only the last `<length>` characters of `<string>` are returned.

  Examples:
```sharp
say -[rjust(foo,6)]-
You say, "-   foo-"
```

    > say %r0[rjust(foo,6,-)]%r01234567<br>
    You say, "<br>
    0---foo7<br>
    01234567"

    > say rjust(foo,12,=-)<br>
    You say, "=-=-=-=-=foo"

    > say rjust(This is too long,9,,1)<br>
    You say, " too long"


::: seealso
- [align()]
- [CENTER()]
- [LJUST()]
- [RIGHT()]
:::
# RLOC()
`rloc(<object>, <levels>)`

  This function may be used to the get the location of `<object>`'s location (and on through the levels of locations), substituting for repeated nested loc() calls. `<levels>` indicates the number of loc()-equivalent calls to make; i.e., loc(loc(`<object>`)) is equivalent to rloc(`<object>`,2). rloc(`<object>`,0) is equivalent to num(`<object>`), and rloc(`<object>`,1) is equivalent to loc(`<object>`).

  If rloc() encounters a room, the dbref of that room is returned. If rloc() encounters an exit, the dbref of that exit's destination is returned. You must control `<object>`, be near it, or it must be a findable player.


::: seealso
- [LOC()]
- [WHERE()]
- [ROOM()]
- [RNUM()]
- [locate()]
:::
# RNUM()
`rnum(<container>, <object>)`

  This function looks for an object called `<object>` located inside `<container>`. If a single matching object is found, its dbref is returned. If several matching objects are found, #-2 is returned, and if nothing matches, or you lack permission, #-1 is returned.

  You must be in `<container>`, or be able to examine it, to use this function.

  This function has been deprecated and may be removed in a future patchlevel; locate(`<container>`, `<object>`, i) should be used instead.


::: seealso
- [locate()]
- [NUM()]
- [RLOC()]
- [ROOM()]
:::
# ROLES()
`roles(<object>)`

  Returns the short names of the roles `<object>` holds, its own and its account's, highest priority first, with `everyone` last.

  Example:
```sharp
think roles(*Ariel)
moderator player everyone
```


::: seealso
- [HASROLE()]
- [PERMISSION()]
- [@role]
:::
# ROOM()
`room(<object>)`

  Returns the "absolute" location of an object. This is always a room; it is the container of all other containers of the object. The "absolute" location of an object is the place @lemit messages are sent to and NO_TEL status determined. You must control the object, be See_All, or be near the object in order for this function to work. The exception to this are players; if `<object>` is a player, the ROOM() function may be used to find the player's absolute location if the player is not set UNFINDABLE.


::: seealso
- [LOC()]
- [RLOC()]
- [RNUM()]
- [WHERE()]
:::
# ROOT()
`root(<number>, \<n\>)`

  Returns the n-th root of `<number>`. The 2nd root is the square root, the 3rd the cube root, and so on.

  Examples:
```sharp
think root(27, 3)
3
think power(3, 3)
27
```


::: seealso
- [SQRT()]
- [POWER()]
:::
# ROUND()
# CEIL()
# FLOOR()
`round(<number>, <places>[, <pad>])`<br>
`floor(<number>)`<br>
`ceil(<number>)`

  round() rounds `<number>` to `<places>` decimal places. `<places>` must be between 0 and config(float_precision). If the optional `<pad>` argument is true, the result will be padded with 0s if it would otherwise have fewer than `<places>` digits after the decimal point.

  floor() rounds `<number>` down, and ceil() rounds `<number>` up, to 0 decimal places.

  Examples:
```sharp
think round(3.14159, 2)
3.14
think round(3.5, 3, 1)
3.500
think ceil(3.14159)
4
think floor(3.14159)
3
```


::: seealso
- [BOUND()]
- [TRUNC()]
:::
# S()
# S-FUNCTION
`s(<string>)`

  This function performs a second round of evaluation on `<string>`, and returns the result. It should be considered extremely dangerous to use on user input, or any other string which you don't have complete control over. There are very few genuine uses for this function; things can normally be achieved another, safer way.

  Example:
```sharp
&test me=$eval *: say When we eval %0, we get [s(%0)]
eval \[ucstr(test)]
You say, "When we eval [ucstr(test)], we get TEST"
```


::: seealso
- [OBJEVAL()]
- [DECOMPOSE()]
:::
# SCAN()
`scan(<looker>, <command>[, <switches>])`<br>
`scan(<command>)`

  This function works like @scan, and returns a space-separated list of dbref/attribute pairs containing $-commands that would be triggered if `<command>` were run by `<looker>`. You must control `<looker>` or be See_All to use this function. Only objects you can examine are included in the output.

  If no `<looker>` is specified, it defaults to the executor.

  `<switches>` is a space-separated list of strings to limit which objects are checked for $-commands. Valid switches are:<br>
    room      --  check `<looker>`'s location and its contents<br>
    me        --  check `<looker>`<br>
    inventory --  check objects in `<looker>`'s inventory<br>
    self      --  check `<looker>` and objects in `<looker>`'s inventory<br>
    zone      --  check `<looker>`'s location's zone, and `<looker>`'s own zone<br>
    globals   --  check objects in the Master Room<br>
    all       --  all of the above (the default)<br>
    break     --  once a match is found, don't check in other locations

  The order of searching for the "break" switch is the same as the order for normal $-command matching, as described in [evaluation order].


::: seealso
- [@scan]
- [@sweep]
- [MASTER ROOM]
- [evaluation order]
- [$-commands]
:::
# SCRAMBLE()
`scramble(<string>)`

  This function scrambles a string, returning a random permutation of its characters. Note that this function does not pay any attention to spaces or other special characters; it will scramble these characters just like normal characters.

  Example:
```sharp
say scramble(abcdef)
You say, "cfaedb"
```


::: seealso
- [SHUFFLE()]
:::
# SECS()
`secs([<precision>])`

  This function returns the number of elapsed seconds since midnight, January 1, 1970 UTC. UTC is the base time zone, formerly GMT. This is a good way of synchronizing things that must run at a certain time.


::: seealso
- [CONVSECS()]
- [time()]
:::
# SECURE()
`secure(<string>)`

  This function returns `<string>` with all "dangerous" characters replaced by spaces. Dangerous characters are<br>
    ( ) [ ] { } $ % , ^ ;<br>
  Note that the use of this function is very rarely needed.


::: seealso
- [DECOMPOSE()]
- [ESCAPE()]
:::
# SET()
`set(<object>[/<attribute>], <flag>)`<br>
`set(<object>, <attribute>:<value>)`

  This function is equivalent to @set, and can be used to toggle flags and set attributes. The two arguments to the function are the same as the arguments that would appear on either side of the '=' in @set. This function returns nothing.

  The attribute-setting ability of set() is deprecated. You should use attrib_set() instead; it's easier to read, and allows you to clear attributes, too.


::: seealso
- [ATTRIB_SET()]
- [@set]
- [WIPE()]
:::
# SETDIFF()
`setdiff(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

  This function returns the difference of two sets -- i.e., the elements in `<list1>` that aren't in `<list2>`. The list that is returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in [SORTING]. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

  Example:
```sharp
say setdiff(foo baz gleep bar, bar moof gleep)
You say, "baz foo"
```


::: seealso
- [SETINTER()]
- [SETSYMDIFF()]
- [SETUNION()]
:::
# SETSYMDIFF()
`setsymdiff(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

  This function returns the symmetric difference of two sets -- i.e., the elements that only appear in one or the other of the lists, but not in both. The list that is returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in [SORTING]. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

  Example:
```sharp
say setsymdiff(foo baz gleep bar, bar moof gleep)
You say, "baz foo moof"
```


::: seealso
- [SETDIFF()]
- [SETINTER()]
- [SETUNION()]
:::
# SETINTER()
`setinter(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

  This function returns the intersection of two sets -- i.e., the elements that are in both `<list1>` and `<list2>`. The list that is returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in [SORTING]. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

  Example:
```sharp
say setinter(foo baz gleep bar, bar moof gleep)
You say, "bar gleep"
```


::: seealso
- [SETDIFF()]
- [SETSYMDIFF()]
- [SETUNION()]
:::
# LISTQ()
# UNSETQ()
`listq([<pattern>])`<br>
`unsetq([<pattern1> [<pattern2> [...]]])`

  listq() returns a space-separated list of set q-registers with values available in the current q-register scope. If `<pattern>` is provided, then only those that match the wildcard pattern `<pattern>` will be returned.

  unsetq() without arguments clears all registers. Otherwise, unsetq() treats its argument as a list of register name patterns, and will unset all those registers within the local scope.

  If unsetq() is inside of a letq(), and does not have an argument, it will clear the registers that letq() has protected. unsetq() with arguments clears the specified registers.

  unsetq(`<arg>`) will clear all registers returned by listq(`<arg>`).

  Example:
```sharp
think setq(name,Walker,num,#6061,loc,Bahamas)[listq()]
LOC NAME NUM
think setq(name,Walker,num,#6061,loc,Bahamas)[listq(n*)]
NAME NUM
think setq(name,Walker,num,#6061,loc,Bahamas)[unsetq(name)][listq()]
LOC NUM
think setq(name,Walker,num,#6061,loc,Bahamas)[unsetq(n*)][listq()]
LOC
```


::: seealso
- [setq()]
- [LETQ()]
- [R()]
- [LOCALIZE()]
- [REGISTERS()]
- [WILDCARDS]
:::
# REGISTERS()
`registers([<pattern>[, <types>[, <osep>]]])`

  The registers() function returns a list of the names of all existing registers of the specified `<types>`. `<types>` is a space-separated list containing zero or more of:

    qregisters - registers set with setq(), setr() and similar functions<br>
    args       - %0-%9 arguments<br>
    iter       - itext() context from iter() or @dolist<br>
    switch     - stext() context from switch() or @switch<br>
    regexp     - regexp capture names

  If `<types>` is empty, all types of registers are included. If `<pattern>` is specified, only registers whose name matches `<pattern>` will be included. The results are separated by `<osep>`, which defaults to a single space.

  The list returned may contain duplicates (for instance, if %0 and %q0 both have a value, the list will include "0" twice), and is not sorted in any particular order.


::: seealso
- [LISTQ()]
- [setq()]
- [setq()]
- [LETQ()]
- [R()]
- [V()]
- [STEXT()]
- [ilev()]
:::
# SETUNION()
`setunion(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

  This function returns the union of two sets -- i.e., all the elements of both `<list1>` and `<list2>`, minus any duplicate elements. The list returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in [SORTING]. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

  Examples:
```sharp
say setunion(foo baz gleep bar, bar moof gleep)
You say, "bar baz foo gleep moof"
```

    > say setunion(1.1 1.0, 1.000)<br>
    You say, "1.0 1.000 1.1"

    > say setunion(1.1 1.0, 1.000, %b, f)<br>
    You say, "1.0 1.1"


::: seealso
- [SETDIFF()]
- [SETINTER()]
- [SETSYMDIFF()]
:::
# SHA0()
`sha0(<string>)`

  Returns the SHA-0 cryptographic hash of the string. See RFC 3174 for more information. Deprecated; use digest() and higher strength algorithms instead. On servers with newer versions of OpenSSL that no longer provide the algorithm, returns #-1 NOT SUPPORTED.


::: seealso
- [digest().]
:::
# SHL()
`shl(<number>, <count>)`

  Performs a leftwards bit-shift on `<number>`, shifting it `<count>` times. This is equivalent to mul(`<number>`, power(2, `<count>`), but much faster.


::: seealso
- [SHR()]
:::
# SHR()
`shr(<number>, <count>)`

  Performs a rightwards bit-shift on `<number>`, shifting it `<count>` times. This is equivalent to div(`<number>`, power(2, `<count>`), but much faster.


::: seealso
- [SHL()]
:::
# SHUFFLE()
`shuffle(<list>[, <delimiter>[, <osep>]])`

  This function shuffles the order of the items of a list, returning a random permutation of its elements.

  `<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`.

  Example:
```sharp
say shuffle(foo bar baz gleep)
You say, "baz foo gleep bar"
```


::: seealso
- [SCRAMBLE()]
- [RANDWORD()]
:::
# SIGN()
`sign(<number>)`

  Essentially returns the sign of a number -- 0 if the number is 0, 1 if the number is positive, and -1 if the number is negative. This is equivalent to bound(`<number>`, -1, 1).

  Example:
```sharp
say sign(-4)
You say, "-1"
```

    > say sign(2)<br>
    You say, "1"

    > say sign(0)<br>
    You say, "0"


::: seealso
- [ABS()]
- [BOUND()]
:::
# SIN()
`sin(<angle>[, <angle type>])`

  Returns the sine of `<angle>`, which should be expressed in the given angle type, or radians by default.

  See 'HELP ANGLES' for more on the angle type.


::: seealso
- [ACOS()]
- [ASIN()]
- [ATAN()]
- [COS()]
- [CTU()]
- [TAN()]
:::
# SORT()
`sort(<list>[, <sort type>[, <delimiter>[, <osep>]]])`

  This sorts a list of words. If no second argument is given, it will try to detect the type of sort it should do. If all the words are numbers, it will sort them in order of smallest to largest. If all the words are dbrefs, it will sort them in order of smallest to largest. Otherwise, it will perform a lexicographic sort.

  The second argument is a sort type. See [SORTING].

  The optional third argument gives the list's delimiter character. If not present, `<delimiter>` defaults to a space. The optional fourth argument gives a string that will delimit the resulting list; it defaults to `<delimiter>`.


::: seealso
- [SORTBY()]
- [SORTKEY()]
:::
# SORTBY()
`sortby([<obj>/]<attrib>, <list>[, <delimiter>[, <output separator>]])`

  This sorts an arbitrary list according to the ufun `<obj>`/`<attrib>`. This ufun should compare two arbitrary elements, %0 and %1, and return zero (equal), a negative integer (element 1 is less than element 2) or a positive integer (element 1 is greater than element 2), similar to the comp() function.

  A simple example, which imitates a normal alphabetic sort:<br>
    > &ALPHASORT test=comp(%0,%1)<br>
    > say sortby(test/ALPHASORT,foo bar baz)<br>
    You say, "bar baz foo"

  A slightly more complicated sort. #1 is "God", #2 is "Amby", "#3" is "Bob":<br>
    > &NAMESORT me=comp(name(%0),name(%1))<br>
    > say sortby(NAMESORT,#1 #2 #3)<br>
    You say, "#2 #3 #1"

  Warning: the function invocation limit applies to this function. If this limit is exceeded, the function will fail _silently_. List and function sizes should be kept reasonable.


::: seealso
- [anonymous attributes]
- [SORTING]
- [SORT()]
- [SORTKEY()]
:::
# SORTKEY()
`sortkey([<obj>/]<attrib>, <list>[, <sort type>[, <delimiter>[, <osep>]]])`

  This function creates a list of keys by passing every element of `<list>` into the ufun given in `<attrib>`. The list is then sorted according to the sorting method in `<sort type>`, or is automatically guessed (as per [SORTING]).

  This is equivalent to:<br>
    > &munge_sort me=sort(%0[, `<sort type>`])<br>
    > say munge(munge_sort, map(`<attrib>`, `<list>`), `<list>`)

  Only there is no risk with delimiters occurring within the list.

  A simple example, which sorts players by their names:<br>
    > @@ #1 is "God", #2 is "Amby", "#3" is "Bob"<br>
    > &KEY_NAME me=name(%0)<br>
    > say sortkey(key_name, #1 #2 #3)<br>
    You say, "#2 #3 #1"


::: seealso
- [anonymous attributes]
- [SORTING]
- [SORTBY()]
:::
# SORTING
  In functions where you can specify a sorting method, you can provide one of these sort types:

  Type    Sorts:<br>
   a       Sorts lexicographically (Maybe case-sensitive).<br>
   i       Sorts lexicographically (Always case-insensitive).<br>
   d       Sorts dbrefs.<br>
   n       Sorts integer numbers.<br>
   f       Sorts decimal numbers.<br>
   m       Sorts strings with embedded numbers and dbrefs (as names).<br>
   name    Sorts dbrefs by their names. (Maybe case-sensitive)<br>
   namei   Sorts dbrefs by their names. (Always case-insensitive)<br>
   conn    Sorts dbrefs by their connection time.<br>
   idle    Sorts dbrefs by their idle time.<br>
   owner   Sorts dbrefs by their owner dbrefs.<br>
   loc     Sorts dbrefs by their location dbref.<br>
   ctime   Sorts dbrefs by their creation time.<br>
   mtime   Sorts dbrefs by their modification time.<br>
   lattr   Sorts attribute names.

  The special sort key attr:`<aname>` or attri:`<aname>` will sort dbrefs according to their `<aname>` attributes. For example: Separating by &factions or &species attrs. attr is probably case-sensitive, and attri is case-insensitive.

  Prefixing the sort type with a minus sign, -, reverses the order of the sort.

  Whether or not the 'a' sort type is case-sensitive or not depends on the particular mush and its environment.


::: seealso
- [SORT()]
- [SORTBY()]
- [SORTKEY()]
- [SETUNION()]
- [SETINTER()]
- [SETDIFF()]
:::
# SOUNDLIKE()
# SOUNDSLIKE()
`soundslike(<word>, <word>[, <hash type>])`<br>
`soundlike(<word>, <word>[, <hash type>])`

  The soundslike function returns 1 if the two words have the same hash code (see [soundex()] for information), which means, in general, if they sound alike. The hash type can be 'soundex' (Default) or 'phone' for a different algorithm that might give better results with some words.

  Examples:
```sharp
think soundslike(robin,robbyn)
1
think soundslike(robin,roebuck, phone)
0
```


::: seealso
- [soundex()]
:::
# SPACE()
`space(<number>)`

  Prints `<number>` spaces. Useful for times when you want to be able to use lots of spaces to separate things. Same as [repeat(%b, `<number>`)].

  Example:
```sharp
say a[space(5)]b
Amberyl says, "a     b"
```


::: seealso
- [REPEAT()]
:::
# SPELLNUM()
`spellnum(<number>)`

  Given a number, return its written-out representation in words.

  Example:
```sharp
think spellnum(12345)
twelve thousand three hundred forty-five
```


::: seealso
- [ORDINAL()]
:::
# ORDINAL()
`ordinal(<integer>)`

  Given an integer, return its written-out ordinal representation in words.

  Example:
```sharp
think ordinal(1)
first
```


::: seealso
- [SPELLNUM()]
:::
# SPLICE()
`splice(<list1>, <list2>, <word>[, <delimiter>])`

  This function splices `<list1>` and `<list2>` together. `<list1>` and `<list2>` are space-separated lists of words.

  If a word in `<list1>` is the same as `<word>`, it is replaced by the word in the corresponding position in `<list2>`. Both lists must have the same<br>
  number of words.

  Example:
```sharp
say splice(foo bar baz,eek moof gleep,bar)
You say, "foo moof baz"
```


::: seealso
- [MERGE()]
:::
# MAPSQL()
`mapsql([<object>/]<attribute>, <query>[, <osep>[, <dofieldnames>[, <param1>[, <param2>[, ...]]]]])`

  Performs an SQL query if the MUSH is configured to connect to an SQL database server. This function requires a WIZARD flag or the Sql_Ok power.

  `<query>` is evaluated, so it's useful to either read it from another attribute with u() or use lit() to protect commas. If you will be interpolating user-provided values into the query, be careful to escape them with sqlescape().

  Each row of the result is passed to `<attribute>`, with the first nine columns available as %1-%9, and twenty more available via v(10) - v(29). %0 is set to the row number, which will start with 1. The fields will also be available as named arguments, with r(`<field name>`, args) returning the appropriate value.

  If `<dofieldnames>` evaluates to a true boolean, then the first call will be with row number (%0) set to 0, and %1-%9 and v(10) - v(29) will be set to the field names.

  **Prepared Statements (Parameterized Queries):** If more than 4 arguments are provided, mapsql() automatically uses prepared statements. The 5th argument onwards are treated as parameters that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe.

  Example using prepared statements:
```sharp
  > &DisplayRow me=User %1 has email %2
  > think mapsql(me/DisplayRow,lit(SELECT name\, email FROM users WHERE status = ?),%r,0,active)
  ```

  See [SQL Examples] for examples.


::: seealso
- [anonymous attributes]
- [SQLESCAPE()]
- [SQL()]
- [@sql]
- [@mapsql]
:::
# SQL()
`sql(<query>[, <row separator>[, <field separator>[, <register>[, <param1>[, <param2>[, ...]]]]]])`

  Performs an SQL query if the MUSH is configured to connect to an SQL database server. This function requires a WIZARD flag or the Sql_Ok power.

  By default, SELECT queries will return their data space-separated. Usually, it's more useful to specify a character to delimit rows returned (and sometimes another character to delimit the fields/columns returned, if they may contain spaces).

  `<query>` is evaluated, so it's useful to either read it from another attribute with u() or use lit() to protect commas. If you will be interpolating user-provided values into the query, be careful to escape them with sqlescape().

  A query that doesn't return any rows, such as an UPDATE or SELECT that has no matches will return a null string.

  If `<register>` is specified, and `<query>` alters the database (such as an UPDATE or INSERT query), there is a side-effect: the number of affected rows is stored into the specified q-register.

  **Prepared Statements (Parameterized Queries):** If more than 4 arguments are provided, sql() automatically uses prepared statements. The 5th argument onwards are treated as parameters that replace `?` placeholders in the query. This is the recommended way to prevent SQL injection attacks, as parameters are properly escaped and type-safe.

  Example using prepared statements:
```sharp
  > think sql(lit(SELECT name FROM users WHERE id = ?),%r,%b,,123)
  ```

  See [SQL Examples] for more examples.


::: seealso
- [SQLESCAPE()]
- [MAPSQL()]
- [@sql]
- [setq()]
- [R()]
- [@mapsql]
:::
# SQL Examples

  Example of using sqlescape() to prevent injection attacks:<br>
    > &SEL_GETID obj=SELECT id FROM mytable WHERE name = '[sqlescape(%0)]'<br>
    > &DOIT obj=$do *: think setq(0,sql(u(SEL_GETID,%0),~,|)); @@ More cmds

  Using mapsql() to format results:<br>
    > @@ Field, Type, Null?, Key?, Default, Extra<br>
    > &each_row me=align(<15 <15 <5 <5 <10 <14,%1,%2,%3,%4,%5,%6)<br>
    > &tabledesc me=mapsql(each_row,describe `[sqlescape(%0)]`,%r,1)<br>
    > think u(tabledesc,quotes)<br>
    Field           Type            Null  Key   Default    Extra<br>
    quoteid         int(11)               PRI              auto_increment<br>
    quote           text

    > &each_bb me=(%0) - %1 (%2)<br>
    > &query me=SELECT `name`, count(*) AS `total` FROM `bbs` GROUP BY `name` ORDER BY `name`<br>
    > think mapsql(each_bb, v(query), %r)<br>
    (1) - Announcements (5)<br>
    (2) - Advertisements (20)

  The same thing can be done using named arguments:<br>
    > &each_bb me=(%0) - [r(name, args)] ([r(total, args)])

  Using sql() to update fields:<br>
    > &update me=UPDATE `foo` SET `time` = '[sqlescape(%0)]' WHERE `loc` = '[sqlescape(%1)]'<br>
    > &foo me=$foo *: think sql(u(update, %0, %L),,0)%q0 rows updated.<br>
    > foo bar<br>
    5 rows updated.

# SQLESCAPE()
`sqlescape(<string>)`

  This function performs SQL-server-implemented escaping of `<string>`. It's important to escape arbitrary data before passing it to the sql() and mapsql() functions, or @sql command, to prevent SQL injection attacks.

  Example:
```sharp
think sqlescape(You don't say)
You don\'t say
```

    OR

    You don''t say


  When used in an SQL query, the results of an sqlescape() function should be enclosed in single quotes.

  You must be a WIZARD or have the Sql_Ok power to use this function.


::: seealso
- [SQL()]
- [MAPSQL()]
- [@sql]
- [@mapsql]
:::
# SQRT()
`sqrt(<number>)`

  Returns the square root of `<number>`. `<number>` cannot be negative.


::: seealso
- [ROOT()]
:::
# SQUISH()
`squish(<string>[, <character>])`

  This function removes the leading and trailing `<character>`s from `<string>`, and condenses all inter-word `<character>`s to a single`<character>`. If no character is given, a space is used.

  Examples:
```sharp

    > say squish(%b%bfoo bar%b%bbaz blech%b%b%beek%b)
    You say, "foo bar baz blech eek"
    > say squish(||a|| b|c|d|, |)
    You say, "a| b|c|d"
```


::: seealso
- [TRIM()]
:::
# STARTTIME()
# RESTARTTIME()
`starttime()`<br>
`restarttime()`

  starttime() returns the time the MUSH was last started, and restarttime() returns the time it was last restarted, including @shutdown/reboots. The times are in the same format as time().

  Example:
```sharp
say starttime()%r[restarttime()]
You say "Sat Dec  7 00:09:13 1991
You say "Sat Dec  7 00:09:13 1991
@shutdown/reboot
say starttime()%r[restarttime()]
You say "Sat Dec  7 00:09:13 1991
Tue Sep 22 13:54:04 2015
```


::: seealso
- [CONVTIME()]
- [RESTARTS()]
:::
# RESTARTS()
`restarts()`

  Returns the number of times the server has been rebooted with @shutdown/reboot since the last full startup.


::: seealso
- [STARTTIME()]
- [STARTTIME()]
:::
# SSL()
`ssl(<player|descriptor>)`

  This function returns 1 if the player is using an SSL connection, and 0 otherwise. If SSL connections are disabled, it always returns 0. You must be See_All to use this function on another player.


::: seealso
- [TERMINFO()]
:::
# STEP()
`step([<obj>/]<attr>, <list>, <step>[, <delim>[, <osep>]])`

  This function is similar to map(), except you can pass up to 30 elements of the list at a time, in %0-%9 and v(10)-v(29). `<step>` must be between 1 and 30, with a step of 1 equivalent to map(). If the elements of the list can't be split up evenly, the last evaluation will run with some of the registers unset; the %+ substitution or the registers() function can be used to see which/how many are set.

  Example:
```sharp
&foo me=%0 - %1 - %2
think step(foo, a b c d e, 3,, %r)
a - b - c
d - e -
```

  Using registers() to avoid extra delimiters:<br>
    > &foo me=iter(registers(,args),v(%i0),,%b-%b)<br>
    > think step(foo, a b c d e, 3,, %r)<br>
    a - b - c<br>
    d - e


::: seealso
- [MAP()]
- [iter()]
- [fold()]
- [anonymous attributes]
- [REGISTERS()]
:::
# STDDEV()
`stddev(<number1>[, ... , <numberN>])`

  Returns the sample standard deviation of its arguments.


::: seealso
- [AVG()]
- [MEDIAN()]
- [LMATH()]
:::
# STRFIRSTOF()
# STRALLOF()
`strfirstof(<expr>[, ... , <exprN>], <default>)`<br>
`strallof(<expr>[, ... , <exprN>], <osep>)`

  strfirstof() returns the first `<expr>` which evaluates to a non-empty string (a string at least 1 character long). If all `<expr>`s evaluate empty, `<default>` is returned instead.

  strallof() returns all `<expr>`s which evaluate to non-empty strings, with each expression separated by `<osep>`.

  Examples:
```sharp
say strfirstof(,  ,@@(Nothing),foo,default)
You say, "foo"
```

    > say strfirstof(get(%#/fullname), u(%#/ansiname), %n)<br>
    You say, "Mike"

    > say strallof(,  ,foo,@@(Nothing),%b,bar|baz,#-1,|)<br>
    You say, "foo| |bar|baz|#-1"


::: seealso
- [ALLOF()]
- [FIRSTOF()]
- [FIRST()]
- [STRLEN()]
- [CAT()]
- [DEFAULT()]
:::
# STRINSERT()
`strinsert(<string>, <position>, <insert>)`

  This function returns `<string>`, with `<insert>` added before the `<position>` character in `<string>`. Note that the first character in `<string>` is numbered 0, not 1.

  If `<position>` is less than 0, an error is returned. If `<position>` is greater than the length of `<string>`, `<insert>` is appended to it.

  Examples:
```sharp
think strinsert(barbaz, 0, foo)
foobarbaz
think strinsert(Myname, 2, %b)
My name
```


::: seealso
- [STRDELETE()]
- [INSERT()]
- [STRREPLACE()]
:::
# STRIPACCENTS()
`stripaccents(<string>[, <smart>])`

  Returns the string with accented characters converted to non-accented. As with the accent() function, this assumes the ISO 8859-1 character set.

 If the second argument is true, it does more a intelligent conversion that might result in one character being turned into several. When it's false, or not given, one character in the input string corresponds to one character in the result.


::: seealso
- [accent()]
- [@nameaccent]
- [ACCNAME()]
- [STRIPANSI()]
- [RENDER()]
:::
# STRIPANSI()
`stripansi(<string>)`

  Returns the string with all ansi and HTML codes removed.


::: seealso
- [STRIPACCENTS()]
- [ansi()]
- [TAG()]
- [RENDER()]
:::

# STRDISTANCE()

`strdistance(<source>, <target>)`

  Returns the minimum number of grapheme insertions, deletions, or substitutions needed to change source into target. Each edit costs one; transposing two graphemes costs two. Comparison is ordinal and case-sensitive. Markup is ignored, and Unicode normalization is not applied: composed `chr(233)` (e with an acute accent) and decomposed `e[chr(769)]` (e followed by a combining acute accent) each contain one grapheme but differ from each other. No optional flags are supported.

  Identical inputs return zero. When one input is empty, the result is the other input's grapheme count. These cases remain subject to the same bounds: each input may contain at most 65,536 UTF-16 code units and 4,096 graphemes, and the product of the two grapheme counts may not exceed 4,000,000. Exceeding any bound returns `#-1 STRING DISTANCE WORK LIMIT EXCEEDED`. The work check happens before allocating comparison rows; row storage grows with the shorter input.

  Use the result to offer a spelling suggestion for a help topic or keyword and let the player choose it. Do not use approximate matches to select targets for destructive commands. Existing `suggest()` keeps its case-folded, UTF-16-based ranking; this function does not change that behavior.

  Examples:

```sharp
strdistance(kitten,sitting)
strdistance(ansi(r,[chr(30028)][chr(128512)]),[chr(30028)][chr(128570)])
strdistance(,e[chr(769)][chr(128512)])
```

  These return `3`, `1`, and `2`, respectively. `chr(30028)` is a wide CJK character, `chr(128512)` and `chr(128570)` are two different emoji (a grinning face and a grinning cat face), and `e[chr(769)]` is e with a combining acute accent.

::: seealso
- [SUGGEST()]
- [GRAPHEMECOUNT()]
- [GRAPHEMES()]
:::

# PRINTF()
`printf(<format>[, <value>...])`

  Builds compact reports from a format and its values. Directives have the form `%[flags][width][.precision]type`, where type is `s` (text), `d` (signed 64-bit integer), or `f` (decimal). Each directive consumes one value. `%%` emits a literal percent and consumes none. Missing or extra values return `#-1 PRINTF ARGUMENT COUNT MISMATCH`; unsupported, incomplete, or repeated flags return `#-1 INVALID PRINTF FORMAT`.

  Normal MUSH percent substitutions happen first. Use `lit()` around the format to pass its percent signs unchanged, or double each percent sign at the MUSH layer. Thus `printf(lit(%s),name)` and `printf(%%s,name)` both return `name`; `printf(%%%%)` returns one percent sign.

  Width is a minimum number of display columns, with spaces on the left by default. `-` moves padding to the right. Numeric fields also accept `+` for a positive sign and `0` for zeros after the sign; `-` takes precedence over `0`. String precision is a maximum number of display columns and keeps whole grapheme clusters. Integer precision is a minimum digit count. Decimal precision is fractional digits, defaults to six, and rounds ties to even.

  Numeric values use a strict invariant grammar: an optional sign, ASCII digits, and (for decimals) a decimal point. Spaces, separators, exponent notation, and Tiny math coercions are not accepted. Values outside signed 64-bit integer or .NET decimal range return the usual number error. Literal and string-value markup survives. Generated numeric text inherits the first input character's markup; markup on a directive's percent sign wraps its field. Padding is plain unless covered by that directive markup. Controls and newlines in strings are retained and use the library's display-width policy.

  The format may contain at most 65,536 UTF-16 code units, 1,024 directives including percent escapes, and 128 value fields. Width and string/integer precision are at most 65,536; decimal precision is at most 28. Exceeding these bounds returns `#-1 PRINTF FIELD LIMIT EXCEEDED` before large padding is allocated. Directives cannot cut through a grapheme cluster. The shared 5,242,880 UTF-16-unit result ceiling also applies; exceeding it stops evaluation with `#-1 OUTPUT EXCEEDED MAXIMUM SIZE`.

  Examples:
```sharp
printf(lit(%-8s %4d),Ore,12)
printf(lit(%+08.2f),12.345)
printf(lit(%4s|%-4s),ansi(r,chr(30028)),ansi(b,chr(128512)))
```

::: seealso
- [DISPLAYWIDTH()]
- [align()]
- [TABLE()]
- [WRAP()]
:::

# DISPLAYWIDTH()
`displaywidth(<string>)`

  Returns the terminal columns occupied by the text, ignoring its markup. A wide CJK character occupies two columns. Combining marks add no columns; joined emoji are measured as whole clusters by MarkupString. Empty text returns 0. Control characters such as tabs and newlines occupy no columns. This is the same as `strlen(<string>,0)`; plain [STRLEN()] also counts each control character as one.

  A display column differs from a Unicode scalar (one code point), a grapheme cluster (a base plus its combining marks, or a joined emoji sequence), and a UTF-16 code unit (the indexing unit used by the .NET string API). Use [GRAPHEMECOUNT()] and [GRAPHEMES()] for cluster operations. These functions do not normalize or repair text.

  Examples: `displaywidth(chr(30028))` returns `2`; `graphemecount(chr(30028))` returns `1`. `chr(30028)` is a wide CJK character.

::: seealso
- [STRLEN()]
- [GRAPHEMECOUNT()]
- [GRAPHEMES()]
:::

# GRAPHEMECOUNT()
`graphemecount(<string>)`

  Returns the number of extended grapheme clusters in the text, ignoring markup. Combining accents, emoji modifiers, joined emoji, and paired flag indicators remain with their cluster. Empty text returns 0. Segmentation follows the released MarkupString library and the runtime Unicode rules, so the original composed or decomposed spelling is retained.

  Examples: `graphemecount(e[chr(769)])` returns `1`, an e and its combining accent; `graphemecount([chr(128105)][chr(8205)][chr(128105)][chr(8205)][chr(128103)][chr(8205)][chr(128102)])` returns `1`, a family emoji built from four emoji and three zero-width joiners.

::: seealso
- [DISPLAYWIDTH()]
- [GRAPHEMES()]
:::

# GRAPHEMES()
`graphemes(<string>[, <output-separator>])`

  Inserts the output separator between whole grapheme clusters, retaining ANSI, HTML, and custom markup. The default separator is one space. Any separator text is accepted, including multiple characters and markup; an explicitly empty separator returns the original text with its markup. Empty input returns empty output. No separator is inserted before the first or after the last cluster, and existing spaces in the input remain clusters. There is no escaping or quoting of clusters containing the separator; choose a separator suitable for your data.

  Examples: `graphemes(e[chr(769)][chr(30028)],|)` returns the accented e, a `|`, then the CJK character; `graphemes(e[chr(769)][chr(30028)],)` returns its input unchanged.

  All three Unicode functions take normally evaluated arguments and use the usual function invocation and recursion limits. The evaluator permits at most 5,242,880 UTF-16 code units per function result. GRAPHEMES checks the expanded length before constructing its output and returns `#-1 OUTPUT EXCEEDED MAXIMUM SIZE` if it would exceed that ceiling. Cluster length itself has no separate fixed limit. Text is not normalized; malformed UTF-16 is retained under the library's segmentation policy.

::: seealso
- [DISPLAYWIDTH()]
- [GRAPHEMECOUNT()]
- [FLIP()]
:::

# STRLEN()
`strlen(<string>[, <count controls>])`

  Returns terminal display columns, ignoring markup. Wide CJK characters count as two columns and combining marks add no columns. Use [GRAPHEMECOUNT()] to count whole grapheme clusters.

  By default each control character, such as a tab (`%t`) or a newline (`%r`), counts as one, as it does in PennMUSH. If `<count controls>` is false, control characters count as zero, because they take up no columns. That matches [DISPLAYWIDTH()]. If `<count controls>` is true or omitted, the default applies.

  Examples:
```sharp
say strlen(foobar)
You say, "6"
say strlen(a%tb)
You say, "3"
say strlen(a%tb,0)
You say, "2"
```


::: seealso
- [WORDS()]
- [STRFIRSTOF()]
:::
# STRMATCH()
`strmatch(<string>, <pattern>[, <register list>])`

  This function matches `<pattern>` against the entire `<string>`. It returns 1 if it matches and 0 if it doesn't. It is not case-sensitive, and `<pattern>` may contain wildcards.

  If `<register list>` is given, there is a side-effect: Wildcards and patterns are stored in q-registers, in the order they are given. `<register list>` is a space-separated list of register names.

  Examples:
```sharp
say strmatch(Foo bar baz, *Baz)
You say, "1"
```

    > say strmatch(Foo bar baz,*Foo)<br>
    You say, "0"

    > say strmatch(Foo bar baz,*o*a*)<br>
    You say, "1"

    > say strmatch(foo:bar,*:*,0 1)/%q0/%q1<br>
    You say, "1/foo/bar"

    > say strmatch(foo:bar=baz,*:*=*,L1 L2 right)/%q`<L1>`/%q`<L2>`/%q`<right>`<br>
    You say, "1/foo/bar/baz"


::: seealso
- [COMP()]
- [element()]
- [setq()]
- [R()]
- [WILDCARDS]
:::
# STRREPLACE()
`strreplace(<string>, <start>, <length>, <text>)`

  Returns `<string>` with the `<length>` characters starting at `<start>` replaced by `<text>`. As with most other string functions, the first character is at position 0.

  If `<start>` is less than 0, an error is returned, and if `<start>` is greater than the length of `<string>`, `<string>` is returned.

  strreplace() attempts to preserve ansi: if `<text>` contains ansi, it will be kept the same. If `<text>` contains no ansi, but `<string>` does, `<text>` will be inserted with the same ansi as the text it replaces. To force `<text>` to be inserted with no ansi, even if `<string>` has ansi, wrap it in ansi(n,....).

  Examples:
```sharp
say strreplace(abcXYZgh, 3, 3, def)
You say, "abcdefgh"
```

    > think strreplace(Fix teh typo, 4, 3, the)<br>
    Fix the typo


::: seealso
- [STRDELETE()]
- [STRINSERT()]
- [LDELETE()]
- [LREPLACE()]
:::
# SUB()
`sub(<number1>, <number>[, ... , <number>])`

  sub() subtracts `<number>` from `<number1>`. If more than one `<number>` argument is given, each is subtracted from the result of the previous subtraction in turn. The result of the final subtraction is returned.


::: seealso
- [ADD()]
- [DEC()]
- [LMATH()]
- [VSUB()]
:::
# SUBJ()
# %s
`subj(<object>)`

  Returns the subjective pronoun - he/she/it - for an object. You can also use the %s substitution to get the subjective pronoun of the enactor.


::: seealso
- [APOSS()]
- [OBJ()]
- [POSS()]
:::
# RESWITCH()
# RESWITCHI()
# RESWITCHALL()
# RESWITCHALLI()
`reswitch(<str>, <re1>, <list1>[, ... , <reN>, <listN>][, <default>])`<br>
`reswitchall(<str>, <re1>, <list1>[, ... , <reN>, <listN>][, <default>])`<br>
`reswitchi(<str>, <re1>, <list1>[, ... , <reN>, <listN>][, <default>])`<br>
`reswitchalli(<str>, <re1>, <list1>[, ... , <reN>, <listN>][, <default>])`

  These functions are just like switch() except they compare `<string>` against a series of regular expressions, not wildcard patterns.

  reswitch() and reswitchall() are case-sensitive.

  reswitch() and reswitchi() return the `<list>` which corresponds to the first matched `<re>`, while reswitchall() and reswitchalli() return the `<list>`s corresponding to all matched `<re>`s. If no `<re>`s match, all four functions return `<default>`.

  The string "#$" in the `<list>`s will be replaced with the value of `<str>`, /before/ `<list>` is evaluated. You can also use $N in `<list>` to refer to the Nth subpattern which matched in `<re>`, with $0 being the entire matching string. Use $`<name>` (the '<>' are literal) to refer to named subpatterns.


::: seealso
- [switch()]
- [regmatch()]
- [REGEDIT()]
- [regexp]
:::
# SWITCH WILDCARDS
  @switch, @select, switch(), and switchall() normally do wildcard matching between their first argument and the `<expr>`ession arguments, with the normal * and ? special characters. However, if one of the `<expr>`essions starts with "`<" or ">`", a less-than or greater-than check is done instead of wildcard matching for that pair.

  switch(X, >Y, A, B) returns A if X is greater than Y, and B if it's not.<br>
  switch(X, >=Y, A, B) returns A if X is greater than or equal to Y, and B if it's not.

  switch(X, <Y, A, B) returns A if X is less than Y, and B if it's not.<br>
  switch(X, <=Y, A, B) returns A if X is less than or equal to Y, and B if it's not.

  If X and Y are numbers, the test is like using gt()/lt() or gte()/lte().

  If X and Y are non-numeric strings, the result of comp(X,Y) is used to determine which string is alphabetically before (less than) the other.

  If you need to have a leading `< or >` that's treated like a normal character in a wildcard match, use \\`< or \\>` (the \\ will turn into \ when the argument is evaluated, and then that single \ will stop the greater/less than check).


::: seealso
- [WILDCARDS]
:::
# STEXT()
# SLEV()
# %$
# %$0
`slev()`<br>
`stext([\<n\>])`<br>
  %$`\<n\>`

  slev() returns the current nesting depth of switch*(), reswitch*() and @switch/@selects. stext() returns the `<string>` being matched for the current switch, or the `\<n\>`th switch where n=0 is the current switch, n=1 is the switch the current switch is nested in, and so on. It is a safer replacement for the "#$" token, which (because it is replaced before evaluation) is unsafe with user input, and unsuitable for use in nested switches.

  stext(L) returns the `<string>` for the outermost switch, and is equivilent to stext(slev()). %$`\<n\>` is equivilent to stext(`\<n\>`), for `\<n\>`s of 0-9 or L.

  Examples:
```sharp
&cmd.whois me=$whois *: @pemit %#=switch(pmatch(%0),#-*, I don't know '%0', '%0' is %$0)
@switch foo=f*, say switch(bar, b*,%$1 %$0!)
You say, "foo bar!"
```


::: seealso
- [switch()]
- [RESWITCH()]
- [@switch]
:::
# T()
`t(<expression>)`

  Returns 1 if `<expression>` is a true boolean value, and 0 otherwise. The definitions of true and false vary depending on the value of the 'tiny_booleans' @config option. See [boolean values] for details.


::: seealso
- [NOT()]
- [IF()]
- [COND()]
- [@break]
- [OR()]
- [AND()]
:::
# TABLE()
`table(<list>[, <field width>[, <line length>[, <delimiter>[, <osep>]]]])`

  This function returns the elements of `<list>` in a tabular format. All other parameters are optional. `<field width>` specifies how wide each table entry is allowed to be, and defaults to 10. If `<field width>` begins with a "`<", it is left-aligned. ">`" makes it right-aligned, and "-" makes it centered. Elements longer than `<field width>` are truncated to fit.

  `<line length>` is how wide a table row can be, and defaults to 78. `<delimiter>` is the delimiter used in `<list>`, and defaults to a space. `<osep>` is a single character to be used between entries in the table, and also defaults to a space.

  Examples:
```sharp
think table(a b areallylongone d)
a          b          areallylon d
```

    > think table(the quick brown fox, 10, 25, , |)<br>
    the       |quick<br>
    brown     |fox


::: seealso
- [align()]
:::
# TAN()
`tan(<angle>[, <angle type>])`

  Returns the tangent of `<angle>`, which should be expressed in the given angle type, or radians by default. See HELP ANGLES for more information.


::: seealso
- [ACOS()]
- [ASIN()]
- [ATAN()]
- [COS()]
- [CTU()]
- [SIN()]
:::
# TEL()
`tel(<object>, <destination>[, <silent>[, <inside>]])`

  This function will teleport `<object>` to `<destination>`, exactly as @teleport `<object>`=`<destination>`. `<silent>` is an optional boolean that, if true, makes the function act like @teleport/silent. `<inside>` is an optional boolean that, if true, makes the function act like @teleport/inside.


::: seealso
- [@teleport]
:::
# TERMINFO()
`terminfo(<player|descriptor>)`

  Returns a list with at least one element - the type of client used by the player, or "unknown" if the client being used doesn't support being asked to identify itself using RFC 1091.

  Other elements in the list describe client capabilities, and currently include:<br>
  pueblo           present if the client is in Pueblo mode.<br>
  telnet           present if the client understands the telnet protocol.<br>
  gmcp             present if GMCP is negotiated via telnet; see help oob()<br>
  ssl              present if the client is using an SSL/TLS connection.<br>
  websocket        present if the client is connected via WebSocket.<br>
  portal           present if the connection is a background portal (system) session.<br>
  prompt_newlines  see [PROMPT_NEWLINES]<br>
  stripaccents     client is receiving 7-bit ascii, no accented characters

  One of the color styles shown in [COLORSTYLE] will also be included.

  Other fields may be added in the future, if, for example, MXP support is ever added.

  You must have see_all, or use terminfo() on yourself, to see all information or use a `<descriptor>`. Mortals using terminfo() on another player will always receive "unknown" for the client name, and will not get telnet/gmcp/ssl/prompt_newlines in the output list.


::: seealso
- [PUEBLO()]
- [WIDTH()]
- [WIDTH()]
- [SSL()]
- [@SOCKSET]
- [OOB()]
:::
# JSON FUNCTIONS
  JSON functions are used to create and modify JSON objects.

`isjson()    json()     json_array()     json_group_by()     json_map()     json_query()     json_mod()`

  This function sends a JSON object to GMCP and WebSocket connections.

`oob()`


::: seealso
- [JSON PATHS]
:::
# WEBSOCKET_HTML()
# WEBSOCKET_JSON()
`websocket_html(<html>[, <player>])`<br>
`websocket_json(<json>[, <player>])`

  Reserved for sending raw HTML or a raw JSON payload out-of-band to a WebSocket client, defaulting to the caller when no `<player>` is given.

  **Neither function is implemented yet.** Both validate their arguments and then return an error; no data reaches any connection. They are registered so that softcode written against them keeps its name, and so that this gap is visible from in-game help rather than only from the source. Use [OOB()] for GMCP, which does work.

  These are SharpMUSH functions; PennMUSH has neither.


::: seealso
- [OOB()]
- [json()]
:::
# OOB()
# GMCP
`oob(<players>, <package>[, <message>])`

  This function sends an out-of-band message using the General MUD Communication Protocol (GMCP - http://www.gammon.com.au/gmcp) and a WebSocket.

  `<players>` is a space-separated list of player names/dbrefs to send the message to. Player names which contain spaces should be given in "quotes".

  `<package>` is the name of the package/message type.

  If specified, `<message>` is a JSON-formatted message to be sent. Use the JSON() function to construct valid JSON.

  You must be a wizard or have the Send_OOB power to send messages to anyone but yourself.

  Returns the number of descriptors the message was sent to on success, or a string starting with #-1 on error.


::: seealso
- [json()]
:::
# ISJSON()
`isjson(<text>)`

  This function returns 1 if its argument is valid JSON, 0 if not.

  Examples:
```sharp
think isjson(1)
1
think isjson(true)
1
think isjson(unquoted)
0
think isjson("quoted")
1
```


::: seealso
- [json()]
:::
# JSON_GROUP_BY()
`json_group_by([<object>/]<attribute>, <list>[, <delimiter>])`

  json_group_by() buckets the elements of `<list>` by a computed key: `<attribute>` (or a #lambda) is evaluated once per element (the element passed as %0, as in filter() and map()), and its result becomes that element's group key. The result is a JSON object mapping each key, in first-seen order, to a JSON array of the elements that produced it.

  Because the key is computed by an attribute, it can be anything derived from the element: an attribute fetched off a dbref, a substring, a classification. An empty list yields {}. Use json_query() to take the result apart.

  Example: group the contents of a room by faction:
```sharp
> &FACTIONOF me=get(%0/FACTION)
> think json_group_by(FACTIONOF, lcon(here))
{"Rebels":["#12","#40"],"Empire":["#7"]}
```


::: seealso
- [json()]
- [json_array()]
- [json_query()]
- [FILTER()]
- [MAP()]
- [CHAIN()]
:::
# JSON PATHS

  json_mod() and the extract argument for json_query() take a path string that describes what part of a JSON object or array to act on. All paths start with a $ to indicate the base JSON value, and 0 or more specifiers in the following formats:

  .FIELD - the name of a field in a JSON object.<br>
  [N]    - the Nth element of a JSON array. Note that the brackets need to be escaped.


::: seealso
- [json_mod()]
- [json_query()]
:::
# TESTLOCK()
`testlock(<key>, <victim>)`

  testlock() returns 1 if the `<victim>` would pass the lock defined in `<key>` as run by the caller, and 0 if it would fail.

  testlock() evaluates the lock from the caller's perspective.

  Example:
```sharp
think testlock(TYPE^PLAYER&FLAG^WIZARD, *Gandalf)
1
think testlock(TYPE^PLAYER&FLAG^WIZARD, *Bilbo)
0
Note: You can escape the first character of <key> using double back slashes, for example, to check if a player has an attribute named +FOO with a value of BAR
think testlock(\\+FOO:BAR,*Walker)
0
```



::: seealso
- [LOCKING]
- [LOCK()]
- [ELOCK()]
- [LOCKFILTER()]
- [locktypes]
:::
# TEXTFILE()
# TEXTENTRIES()
# TEXTSEARCH()
# DYNHELP()
`textfile(<type>, <entry>)`<br>
`textentries(<type>, <pattern>[, <osep>])`<br>
`textsearch(<type>, <pattern>[, <osep>])`

  textfile() returns the text of entries from cached text files (such as "help", "news", "events", etc.) All whitespace and newlines are included, so you may want to edit %r's and squish the result if you plan to use the text as a list of words rather than a display.

  textentries() returns the topic names in `<type>` matching `<pattern>`, separated by `<osep>` (a space by default). `<pattern>` is a wildcard pattern matched against the topic name; use `*` for every topic, or textsearch() where you want matching by content instead.

  Both textfile() and textentries() return #-1 NO SUCH FILE for a `<type>` that is not a text-file category, and #-1 PERMISSION DENIED for an administrator-only one (ahelp) to anyone who is not a wizard or royalty.

  textsearch() returns the names of all topics whose contents matches the given `<pattern>`, the same as "help/search `<pattern>`", with topic names separated by `<osep>`.

  Example:
```sharp
say textentries(help, ?who())
You say, "CWHO() LWHO() MWHO() NWHO() XWHO() ZWHO()"
```

    > say textsearch(help, pronouns, |)<br>
    You say, "1.6.0P0|GENDER|SEX"

    > say textfile(help, ln\(\))<br>
    You say, "  ln(`<number>`)

      Returns the natural log of `<number>`.


::: seealso
- [LOG()]
    "
:::

::: seealso
- [WILDCARDS]
:::
# ETIME()
`etime(<seconds>[, <width>[, <precision>]])`

  This function formats a number of seconds using the same rules as the 'On for' and 'Idle' columens in WHO's output. The optional `<width>` argument controls the maximum size of the returned string.

  The elapsed time is split into years, weeks, days, hours, minutes and seconds fields. As many non-zero fields as can fit into `<width>` characters are used, in that order. If all fields are 0, seconds are displayed.

  Examples:
```sharp
think etime(59)
59s
think etime(60)
1m
think etime(61)
1m  1s
think etime(61, 5)
1m
```


::: seealso
- [etimefmt()]
- [TIMESTRING()]
- [STRINGSECS()]
:::
# TIMESTRING()
`timestring(<seconds>[, <pad flag>[, <precision>]])`

  The timestring function takes a number of seconds as input and returns the amount of time formatted into days, hours, minutes, and seconds. If `<pad flag>` is 1, all time periods will be used even if the number of seconds is less than a day, hour, or minute. If `<pad flag>` is 2, all numbers will be 2 digits long.

  Examples:
```sharp
say timestring(301)
You say, " 5m  1s"
say timestring(301,1)
You say, "0d  0h  5m  1s"
say timestring(301,2)
You say, "00d 00h 05m 01s"
```


::: seealso
- [STRINGSECS()]
- [CONVSECS()]
- [ETIME()]
- [etimefmt()]
:::
# STRINGSECS()
`stringsecs(<timestring>[, <precision>])`

  The stringsecs() function takes a string of the form produced by timestring() or etime() and converts it back into seconds.

  Examples:
```sharp
say stringsecs(5m 1s)
You say, "301"
```

    > say stringsecs(3y 2m 7d 5h 23m)<br>
    You say, "95232300"


::: seealso
- [TIMESTRING()]
- [etimefmt()]
- [CONVTIME()]
- [ETIME()]
:::
# TR()
`tr(<string>, <find>, <replace>)`

  This function translates every character in `<string>` that exists in `<find>` to the character at an identical position in `<replace>`. Ranges of characters separated by -'s are accepted. `<find>` and `<replace>` must be the same length after expansion of ranges. If a character exists more than once in `<find>`, only the last instance will be counted. The example below is the common ROT-13 algorithm for lower case strings, demonstrated with every letter explicitly listed, and with the equivalent but briefer character ranges. Literal -'s can be in `<find>` and `<replace>` if they are the first or last characters in the arguments.

   Examples:
```sharp
say tr(hello,abcdefghijklmnopqrstuvwxyz,nopqrstuvwxyzabcdefghijklm)
You say, "uryyb"
say tr(uryyb, a-z, n-za-m)
You say, "hello"
```


::: seealso
- [MERGE()]
- [SPLICE()]
:::
# TRIM()
# TRIMPENN()
# TRIMTINY()
`trim(<string>[, <characters to trim>[, <trim style>]])`<br>
`trimpenn(<string>[, <characters to trim>[, <trim style>]])`<br>
`trimtiny(<string>[, <trim style>[, <characters to trim>]])`

  trim() strips leading and/or trailing occurrences of each of the `<characters to trim>` from `<string>`.

  `<characters to trim>` defaults to a space.

  If no `<trim style>` is specified, characters are trimmed from both the left and right sides of the string. If the 'l' trim style is specified, characters are only trimmed from the left side. If the 'r' trim style is specified, characters are only trimmed from the right side. If the 'b' trim style is specified, or a style is omitted, characters are trimmed off of both sides of the string.

  Normally, the arguments for trim() are in the same order as trimpenn(). However, if the tiny_trim_fun @config option is on, the `<characters to trim>` and `<trim style>` arguments are reversed. Use trimpenn() or trimtiny() if you want to specify a particular argument sequence no matter how the option is set.

  Examples:
```sharp
say trim(%b%bfoo bar baz%b%b%beek%b%b)
You say, "foo bar baz   eek"
say trim(***BLAM***,*)
You say, "BLAM"
say trim(-----> WOW <---,-,r)
You say, "-----> WOW <"
say trim(=~=~=~= Trim Test =~=~=~=,= ~)
You say "Trim Test"
```


::: seealso
- [SQUISH()]
- [EDIT()]
:::
# TRUNC()
# VAL()
`trunc(<string>)`

  This function truncates floating point numbers to integers. It can also be used to return the leading numeric prefix of a string. If `<string>` does not start with a number, 0 is returned.

  Examples:
```sharp
say trunc(3.141593)
You say, "3"
say trunc(101Dalmations)
You say, "101"
```

  val() is an alias for trunc().


::: seealso
- [ROUND()]
- [ROUND()]
- [BOUND()]
- [ROUND()]
- [LEFT()]
:::
# TYPE()
`type(<object>)`

  This function returns the type of an object - one of PLAYER, THING, EXIT, ROOM or GARBAGE - or #-1 if the object can't be found.

  Examples:
```sharp
@create Test
think type(Test)
THING
think type(me)
PLAYER
think type(here)
ROOM
```


::: seealso
- [HASTYPE()]
- [TYPES OF OBJECTS]
:::
# UCSTR()
# UCSTR2()
`ucstr(<string>)`<br>
`ucstr2(<string>)`

  Returns `<string>` with all letters converted to uppercase.

  If the MUSH is compiled with ICU Unicode support, ucstr2() does the same thing except the returned string might be a different length, and ansi colors and other markup are stripped.

  Example:
```sharp
say ucstr(Foo BAR baz)
You say, "FOO BAR BAZ"
say ucstr2(gr[chr(252)]n)
```

  The second example says "GRUN" with an umlaut on the U; `chr(252)` is u with an umlaut.


::: seealso
- [LCSTR()]
- [CAPSTR()]
:::
# UDEFAULT()
# ULDEFAULT()
`udefault([<object>/]<attribute>, <default case>[, <arg0>[, ... , <arg29>]])`<br>
`uldefault([<object>/]<attribute>, <default case>[, <arg0>[, ... <arg29>]])`

  If the given `<attribute>` on `<object>` (or the caller, if no `<object>` is given) can be read, the attribute is evaluated, and the result returned. Up to thirty `<arg>`s can be passed to the attribute, as per ufun().

  If the attribute cannot be read, `<default case>` is evaluated and returned instead. The `<default case>` is not evaluated if the attribute exists.

  uldefault() saves the global q-registers (%q0-%q9, %qa-%qz, etc) before evaluation, and restores them afterwards, as per ulocal().

  Examples:
```sharp
&TEST me=center(%0,5,*)
say udefault(Test,-- BOOM --,ACK)
You say "*ACK*"
&TEST me
say udefault(me/Test,-- BOOM --,ACK)
You say "-- BOOM --"
```


::: seealso
- [GET()]
- [EVAL()]
- [u()]
- [DEFAULT()]
- [EDEFAULT()]
- [ulocal()]
- [LOCALIZE()]
:::
# UNIQUE()
`unique(<list>[, <sort type>[, <delim>[, <osep>]]])`

  unique() returns a copy of `<list>` with consecutive duplicate items removed. It does not sort the list. The optional `<sort type>` describes what type of data is in the list; see [SORTING] for details. If no type is given, the elements are compared as strings. Elements of `<list>` are separated by `<delim>`, which defaults to a space. Each element of the output is separated by `<osep>`, which defaults to `<delim>`.

  Examples:
```sharp
think unique(a b b c b)
a b c b
think unique(1 2 2.0 3, f)
1 2 3
think unique(1|2|3|3, n, |, _)
1_2_3
```


::: seealso
- [SETUNION()]
- [SORT()]
:::
# V()
# V-FUNCTION
`v(<variable>)`<br>
`v(<integer>)`<br>
`v(<attribute>)`

  The first form of this function returns the value of the `<variable>` %-sub. In most cases, using the %-sub is preferable. Not all %-subs are accessible this way; only the following `<variable>`s are valid:

    0-9, #, @, !, n, l, and c.

  Unlike %-subs, the v() function is not case-sensitive: v(n) and v(N) are both equivilent to %n (whereas %N is equivilent to [capstr(%n)]).

  v() can also return the value of stack registers. v(0) is equivilent to %0, but v() can return up to 30 registers (v(0) through v(29)). Calling v() with an integer arg that is not between 0 and 29 (inclusive) will return an out-of-range error.

  The final form of this function is equivilent to get(me/`<attribute>`), but is usually slightly more efficient.


::: seealso
- [STACK]
- [registers]
- [%]
- [GET()]
- [R()]
- [attributes]
:::
# VADD()
`vadd(<vector1>, <vector2>[, <delimiter>[, <osep>]])`

  Returns the sum of two vectors. A vector is a list of numbers separated by spaces or `<delimiter>`.

  > think vadd(1 2 3, 4 5 6)<br>
  5 7 9<br>
  > think vadd(0|0|0, 1|2|3, |)<br>
  1|2|3


::: seealso
- [Vector functions]
:::
# VCROSS()
`vcross(<vector1>, <vector2>[, <delimiter>[, <osep>]])`

  Returns the 3-dimensional vector that is the cross product of its 3-dimensional argument vectors. The cross product is defined as:

   x = Ay * Bz - By * Az<br>
   y = Az * Bx - Bz * Ax<br>
   z = Ax * By - Bx * Ay

  > think vcross(4 5 6, 7 8 9)<br>
  -3 6 -3


::: seealso
- [Vector functions]
:::
# VDIM()
`vdim(<vector>[, <delimiter>])`

  Returns the dimensionality of a vector.

  > think vdim(1 2 3 4)<br>
  4


::: seealso
- [Vector functions]
:::
# VDOT()
`vdot(<vector1>, <vector2>[, <delimiter>[, <osep>]])`

  Returns the dot product of two vectors. A dot product is the sum of the products of the corresponding elements of the two vectors, e.g. vdot(a b c,d e f) = ad + be + cf. The vectors must be of the same length.

  > think vdot(1 2 3, 2 3 4)<br>
  20


::: seealso
- [Vector functions]
:::
# VMIN()
`vmin(<vector1>, <vector2>[, <delimiter>[, <osep>]])`

  Returns a new vector made out of the minimums of each corresponding pair of numbers from the two vectors. The vectors must be of the same length.

  > think vmin(1 2 3, 4 1 2)<br>
  1 1 2


::: seealso
- [Vector functions]
:::
# VMAX()
`vmax(<vector1>, <vector2>[, <delimiter>[, <osep>]])`

  Returns a new vector made out of the maximums of each corresponding pair of numbers from the two vectors. The vectors must be of the same length.

  > think vmax(1 2 3, 4 1 2)<br>
  4 2 3


::: seealso
- [Vector functions]
:::
# VERSION()
# NUMVERSION()
`version()`<br>
`numversion()`

  version() returns a string which contains various version information for the MUSH you're on. numversion() returns an integer representation of the version/patchlevel which can be used for softcode comparison.

  Example:
```sharp
say version()
You say "SharpMUSH version 1.8.1 patchlevel 4 [12/06/2005]"
say numversion()
You say "1008001004"
```

    > say version()<br>
    You say, "SharpMUSH version 1.8.5 patchlevel 7 [03/16/2015] (rev ebdea0a)"<br>
    > say numversion()<br>
    You say, "1008005007"


::: seealso
- [@version]
:::
# VISIBLE()
`visible(<object>, <victim>[/<attribute>])`

  If no attribute name is provided, this function returns 1 if `<object>` can examine `<victim>`, or 0, if it cannot. If an attribute name is given, the function returns 1 if `<object>` can see the attribute `<attribute>` on `<victim>`, or 0, if it cannot.

  If `<object>`, `<victim>`, or `<attribute>` is invalid, the function returns 0.


::: seealso
- [CONTROLS()]
- [VISUAL]
:::
# VMAG()
`vmag(<vector>[, <delimiter>])`

  Returns the magnitude of a vector, using a Euclidean distance metric. That is, for vector a b c d, returns sqrt(a^2+b^2+c^2+d^2).

  > think vmag(3 4)<br>
  5


::: seealso
- [Vector functions]
:::
# VMUL()
`vmul(<vector1|number1>, <vector2|number2>[, <delimiter>[, <osep>]])`

  Returns the result of either multiplying a vector by a number, or the element-wise product of two vectors. The element-wise product of a b c by w x z is aw bx cz

  > think vmul(1 2 3, 2)<br>
  2 4 6<br>
  > think vmul(1 2 3, 2 3 4)<br>
  2 6 12


::: seealso
- [Vector functions]
:::
# VSUB()
`vsub(<vector1>, <vector2>[, <delimiter>[, <osep>]])`

  Returns the difference between two vectors.

  > think vsub(3 4 5, 3 2 1)<br>
  0 2 4


::: seealso
- [Vector functions]
:::
# VUNIT()
`vunit(<vector>[, <delimiter>])`

  Returns the unit vector (a vector of magnitude 1), which points in the same direction as the given vector.

  > think vunit(2 0 0)<br>
  1 0 0<br>
  > think vmul(vunit(5 6 7), vmag(5 6 7))<br>
  5 6 7


::: seealso
- [Vector functions]
:::
# WIDTH()
# HEIGHT()
# SCREENWIDTH
# SCREENHEIGHT
`width(<player|descriptor>[, <default>])`<br>
`height(<player|descriptor>[, <default>])`

  These two functions return the screen width and height for a connected player. If the player's client is capable of doing so, it will let the mush know what the correct sizes are on connection and when the client is resized.

  The defaults are 78 for width, and 24 for height, the normal minimal values. These can be overridden when calling the function by providing the default to the function. Players can change the value that will be returned when the functions are called on them with the special SCREENWIDTH and SCREENHEIGHT commands, both of which take a number as their sole argument, and set the appropriate field.

  When used on something that's not a visible player, the functions return the default values.

  The intent of these functions is allow softcode that does formatting to be able to produce a display that can make full use of any given screen size.
# WHERE()
`where(<object>)`

  This function returns the "true" location of an object. This is the standard location (i.e. where the object is) for things and players, the source room for exits, and #-1 for rooms.

  In other words, the "true" location of an object is where it is linked into the database. For example, an exit appears in the room of its "home", not its "location" (the LOC() function on an exit<br>
  will return the latter). A room's "real" location is always Nothing (the LOC() function will return its drop-to).


::: seealso
- [ROOM()]
- [LOC()]
- [RNUM()]
- [locate()]
- [HOME()]
- [@whereis]
:::
# WIPE()
`wipe(<object>[/<attribute pattern>])`

  This function is equivalent to @wipe, and attempts to wipe all the attributes on `<object>` whose names match `<attribute pattern>`, or "*" if no pattern is given. It returns nothing. Like @wipe, this function will destroy entire attribute trees; to safely remove a single attribute, use attrib_set() instead.


::: seealso
- [@wipe]
- [ATTRIB_SET()]
- [SET()]
:::
# WORDPOS()
`wordpos(<list>, <number>[, <delimiter>])`

  Returns the number of the word within `<list>` where the `<number>`th character falls. Characters and words are numbered starting with 1, and `<delimiter>`s between words are treated as belonging to the word that follows them. If the list is less than `<number>` characters long, #-1 is returned. `<delimiter>` defaults to a space.

  Example:
```sharp
say wordpos(foo bar baz, 5)
You say, "2"
```


::: seealso
- [MEMBER()]
- [POS()]
:::
# WORDS()
`words(<list>[, <delimiter>])`

  words() returns the number of elements in `<list>`. Elements of `<list>` are separated by `<delimiter>`, which defaults to a space.

  When the `<delimiter>` is a space, empty elements are not counted.

  Examples:
```sharp
think words(1 2%b%b3, %b)
3
```

    > think words(1|2||3, |)<br>
    4


::: seealso
- [STRLEN()]
- [ITEMS()]
:::
# WRAP()
`wrap(<string>, <width>[, <first line width>[, <line separator>]])`

  This function takes `<string>` and splits it into lines containing no more than `<width>` characters each. If `<first line width>` is given, the first line may have a different width. If `<line separator>` is given, it is inserted between each line; by default the separator is a newline (%r).

  Examples:
```sharp
@desc here=wrap(Wrapped paragraph, 72)
@desc here=wrap([space(4)]Indented paragraph, 72)
@desc here=iter(wrap(Hanging indent, 72, 76, %r), switch(#@, >1, space(4))%i0, %r, %r)
```
# XATTR()
# XATTRP()
# REGXATTR()
# REGXATTRP()
`xattr(<object>[/<attribute pattern>], <start>, <count>[, <osep>])`<br>
`xattrp(<object>[/<attribute pattern>], <start>, <count>[, <osep>])`<br>
`regxattr(<object>[/<regexp>], <start>, <count>[, <osep>])`<br>
`regxattrp(<object>[/<regexp>], <start>, <count>[, <osep>])`

  xattr() fetches `<count>` or fewer attribute names from `<object>` starting at position `<start>`. It is useful when the number of attributes on an object causes lattr() to exceed the buffer limit. The resulting list is separated by `<osep>`, which defaults to a space. `<start>` begins at 1.

  It is equivalent to<br>
`extract(lattr(<object>[/<attribute pattern>]), <start>, <count>, <osep>)`

  `<attribute pattern>` is a wildcard pattern which defaults to "*"; use "**" to get all attributes, including leaf attributes in trees. regxattr() matches attributes against the regular expression `<regexp>`.

  xattrp() and regxattrp() will include attributes from parents. Do note that parent attributes are listed _after_ child attributes, not sorted alphabetically.


::: seealso
- [NATTR()]
- [LATTR()]
- [WILDCARDS]
- [regexp]
:::
# XOR()
`xor(<boolean1>, <boolean2>[, ... , <booleanN>])`

  Takes two or more booleans and returns a 1 if one, and only one, of the inputs is equivalent to true(1).


::: seealso
- [boolean values]
- [AND()]
- [OR()]
- [NOT()]
- [NOR()]
- [LMATH()]
:::
# XVCON()
# XCON()
`xcon(<object>, <start>, <count>)`<br>
`xvcon(<object>, <start>, <count>)`

  xcon() fetches `<count>` or fewer item dbrefs from `<object>`'s contents starting at position `<start>`. It is useful when the number of objects in a container causes lcon() to exceed the buffer limit.

  It is equivalent to extract(lcon(`<object>`), `<start>`, `<count>`)

  xvcon() is identical, but follows the restrictions of lvcon().


::: seealso
- [NVCON()]
- [LCON()]
- [LVCON()]
:::
# XVEXITS()
# XEXITS()
`xexits(<room>, <start>, <count>)`<br>
`xvexits(<room>, <start>, <count>)`

  xexits() fetches `<count>` or fewer exit dbrefs from `<room>` starting at position `<start>`. It is useful when the number of exits in a container causes lexits() to exceed the buffer limit.

  It is equivalent to extract(lexits(`<room>`), `<start>`, `<count>`)

  xvexits() is identical, but follows the restrictions of lvexits().


::: seealso
- [NVEXITS()]
- [LEXITS()]
- [LVEXITS()]
:::
# XVPLAYERS()
# XPLAYERS()
`xplayers(<object>, <start>, <count>)`<br>
`xvplayers(<object>, <start>, <count>)`

  xplayers() fetches `<count>` or fewer player dbrefs from `<object>`'s contents starting at position `<start>`. It is useful when the number of players in a container causes lplayers() to exceed the buffer limit. It is equivalent to

`extract(lplayers(<object>), <start>, <count>)`

  xvplayers() is identical, but follows the restrictions of lvplayers().


::: seealso
- [NVPLAYERS()]
- [LPLAYERS()]
- [LVPLAYERS()]
- [XVTHINGS()]
- [XVEXITS()]
:::
# XVTHINGS()
# XTHINGS()
# XOBJECTS()
# XVOBJECTS()
`xthings(<object>, <start>, <count>)`<br>
`xvthings(<object>, <start>, <count>)`

  xthings() fetches `<count>` or fewer non-player dbrefs from `<object>`'s contents starting at position `<start>`. It is useful when the number of things in a container causes lthings() to exceed the buffer limit. It is equivalent to:

`extract(lthings(<object>), <start>, <count>)`

  xvthings() is identical, except it follows the restrictions of lvthings().


::: seealso
- [NVTHINGS()]
- [LTHINGS()]
- [LVTHINGS()]
- [XVPLAYERS()]
- [XVEXITS()]
:::
# XWHO()
# XWHOID()
# XMWHO()
# XMWHOID()
`xwho([<looker>, ]<start>, <count>)`<br>
`xmwho(<start>, <count>)`<br>
`xwhoid([<looker>, ]<start>, <count>)`<br>
`xmwhoid(<start>, <count>)`

  xwho() fetches `<count>` or fewer player dbrefs from the list of connected players, starting at position `<start>`. It is useful when the number of players connected causes lwho() or pemits in +who $-commands to exceed buffer limits. If a `<looker>` is given, only includes players who `<looker>` can see are online. It is equivalent to:

`extract(lwho([<looker>]), <start>, <count>)`

  xmwho() does not include hidden players (like mwho()).

  xwhoid() and xmwhoid() return objids instead of dbrefs.


::: seealso
- [LWHO()]
- [MWHO()]
- [NMWHO()]
- [ZWHO()]
:::
# ZFIND()
`zfind(<zone>[, <osep>])`

  Returns the dbrefs of every object @chzone'd to `<zone>` that you may examine, separated by `<osep>` (a space by default).

  You must be See_All or pass `<zone>`'s @lock/zone. Objects you could not examine are left out rather than erroring, so the result is what you may see and not necessarily the whole zone.

  This is a SharpMUSH function; PennMUSH offers the zone lists through [lsearch()] and [ZWHO()].


::: seealso
- [ZWHO()]
- [ZONE()]
- [@chzone]
- [lsearch()]
:::
# ZWHO()
# ZMWHO()
`zwho(<object>[, <viewer>])`<br>
`zmwho(<object>)`

  These functions return the dbrefs of all currently-connected players in locations @chzone'd to `<object>`. zmwho() does not include hidden players, while zwho() returns all players that the caller can see are online. You must be See_All or pass `<object>`'s @lock/zone to use these functions.

  See_All players can pass a `<viewer>` argument to zwho() to get only those players that `<viewer>` can see is online.


::: seealso
- [LWHO()]
- [NMWHO()]
- [ZONE()]
- [ZFUN()]
- [ZEMIT()]
:::
# ZEMIT()
# NSZEMIT()
`zemit(<zone>, <message>)`<br>
`nszemit(<zone>, <message>)`

  zemit() emits `<message>` in every room @chzone'd to `<zone>`, as per @zemit.

  nszemit() works like @nszemit.


::: seealso
- [@zemit]
- [ZONE()]
- [ZFUN()]
- [ZWHO()]
- [zones]
:::
# ZFUN()
`zfun(<attribute>[, <arg0>[, <arg1>[, ... , <arg29>]]])`

  This function evaluates an attribute on the caller's Zone object. It is essentially identical to

`ufun(zone(me)/<attribute>[, <arg0>[, ... , <arg29>]])`


::: seealso
- [u()]
- [GET()]
- [ZONE()]
- [ZEMIT()]
- [ZWHO()]
- [zones]
:::
# ZONE()
`zone(<object>[, <new zone>])`

  Returns `<object>`'s zone, or #-1 if it has no zone. You must be able to examine the object; if you can't, zone() returns #-1.

  If a `<new zone>` is given, zone() attempts to change the zone of `<object>` to `<new zone>` first - see help @chzone for details.


::: seealso
- [@chzone]
- [ZFUN()]
- [ZWHO()]
- [zemit() ZONES]
:::
# UPTIME()
`UPTIME([<type>[, <precision>]])`

  This function returns the time, as a number of seconds, that something happend (or will happen). Exactly what is returned depends on the given `<type>`, which should be one of:

    upsince   - The time the MUSH was started. This is the default.<br>
    reboot    - The time the MUSH was last rebooted.<br>
    save      - The time the MUSH last saved, or -1 if it hasn't.<br>
    nextsave  - The time of the next automatic save.<br>
    dbck      - The time of the next automatic dbck.<br>
    purge     - The time of the next automatic purge.<br>
    warnings  - The time of the next automatic warnings check, or -1 if automated warnings are disabled.


::: seealso
- [@uptime]
- [SECS()]
- [CONVSECS()]
- [time()]
- [STARTTIME()]
- [STARTTIME()]
- [RESTARTS()]
- [@dbck]
- [@purge]
- [@warnings]
- [@config]
- [@dump]
- [@shutdown]
:::
# SUGGEST()
`SUGGEST(<category>, <word>[, <seperator>[, <limit>]])`

  Returns a list of suggested alternatives to `<word>` from vocabulary words known in the given `<category>`. `<seperator>` defaults to space. `<limit>` controls how many suggestions are returned, and defaults to 20.

 This is the same mechanism used to suggest help entries, function names, etc. when an unknown one is encountered.

 If the dict_file @config option is set, loads that file into the 'words' category.

 Example:
```sharp
think suggest(words, ardvark)
AARDVARK AARDVARKS AARDVARK'S etc...
```


::: seealso
- [@SUGGEST]
:::
# CONNRECORD()
`CONNRECORD(<id>[, <osep>])`

  This Wizard-only function returns information about a connection if enhanced logging is enabled.

  `<id>` is a value returned by connlog(). connrecord() returns the following fields: DBREF NAME IPADDR HOSTNAME CONNECTION-TIME DISCONNECTION-TIME DISCONNECTION-REASON SSL WEBSOCKET. If the optional `<osep>` argument is given, it is used instead of space to seperate fields.

  If a single connection had multiple logins and logouts, only the last one is used. If a connection was never logged in, the DBREF field is #-1 and the NAME field is -. If the connection is still active, the DISCONNECTION-TIME is -1 and DISCONNECTION-REASON is -.

  Times are in seconds since the epoch, as returned by secs().

  This function must be enabled (by the use_connlog @config option); if disabled, it returns #-1.


::: seealso
- [connlog()]
:::
# ADDRLOG()
`ADDRLOG([<count>, ]ip|hostname, <pattern>[, <osep>])`

  Searches the log of unique sites that have connected to the mush and returns a list of 'IPADDRESS HOSTNAME' pairs that match the given field with the given wildcard pattern, separated by `<osep>`, which defaults to |. If 'count' is given, returns the total number of matches instead.

  Restricted to See_all, Wizard, and Royalty objects.

  This function must be enabled (by the use_connlog @config option); if disabled, it returns #-1.


::: seealso
- [connlog()]
- [CONNRECORD()]
:::
# URLENCODE()
`URLENCODE(<string>)`

  This function converts its argument to a URL-encoded string where everything but a-z, A-Z, 0-9, -, ., _, and ~ are converted into %NN where NN is a hex code for their character value.


::: seealso
- [URLDECODE()]
- [@HTTP]
:::
# URLDECODE()
`URLDECODE(<string>)`

  This function takes a URL-encoded string and returns it in its decoded form. Unprintable characters are converted to question marks.


::: seealso
- [URLENCODE()]
- [@HTTP]
:::
# HMAC()
`HMAC(<digest>, <key>, <text>[, <encoding>])`

  Computes the HMAC (message authentication code) hash for `<text>` using the passphrase `<key>` and the given hash function `<digest>`, which can be any supported by digest(). `<encoding>` can be base16 (The default) or base64.

  Example:
```sharp
think hmac(sha256, secret, this is some text)
9598fd959633f2a64a7d7e985966774aa6f334bc802e5b3301772ec8ed6eed5a
think hmac(sha256, secret, this is some text, base64)
lZj9lZYz8qZKfX6YWWZ3SqbzNLyALlszAXcuyO1u7Vo=
```


::: seealso
- [DIGEST()]
:::
