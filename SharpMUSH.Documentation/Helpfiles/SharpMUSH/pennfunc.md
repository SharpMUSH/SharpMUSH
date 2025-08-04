# FUNCTIONS

# FUNCTION

Functions are specialized commands used to manipulate strings and other input. Functions take the general form: `[FUNCTION(<input>)]`

The brackets are used to delimit and force evaluation of the function (or nested functions). The brackets can also be used to group functions for the purposes of string concatenation. In general, more than one pair of brackets is not required, but you can nest an arbitrary number of brackets.

Examples:

```
> say first(rest(This is a nice day))
You say, "is"
```

```
> @va me=This is a
> @vb me=nice day
> say first(rest(v(va) [v(vb)]))
You say, "is"
```

See 'help functions2' for more.

# FUNCTIONS2

There are two types of functions, "built-in functions" and "global user functions", also known as "@functions". You can get a complete list of functions on this game with "@list/functions".

Built-in functions are written in the game hardcode, while @functions are written in softcode, and then made global with the "@function" command. Both are used in exactly the same manner. For more information on @functions, see 'help @function'.

See also: [MUSHCODE], [FUNCTION LIST]

# FUNCTION LIST

# FUNCTION TYPES

Several major variants of functions are available. The help topics are listed below, together with a quick summary of the function type and some examples of that type of function.

*   **Attribute functions**: attribute-related manipulations (GET, UFUN)
*   **Bitwise functions**: manipulation of individual bits of numbers (SHL, BOR)
*   **Boolean functions**: produce 0 or 1 (false or true) answers (OR, AND)
*   **Channel functions**: get information about channels (CTITLE, CWHO)
*   **Communication functions**: send messages to objects (PEMIT, OEMIT)
*   **Connection functions**: get information about a player's connection (CONN)
*   **Dbref functions**: return dbref info related to objects (LOC, LEXITS)
*   **HTML functions**: output HTML tags for Pueblo and WebSocket clients
*   **Information functions**: find out something about objects (FLAGS, MONEY)
*   **JSON functions**: create and manipulate JSON objects (JSON, JSON_MAP)
*   **List functions**: manipulate lists (REVWORDS, FIRST)
*   **Mail functions**: manipulate @mail (MAIL, FOLDERSTATS)
*   **Math functions**: number manipulation, generic or integers only (ADD, DIV)
*   **Regular expression functions**: Regular expressions (REGMATCH, REGEDIT)
*   **SQL functions**: access SQL databases (SQL, SQLESCAPE)
*   **String functions**: string manipulation (ESCAPE, FLIP)
*   **Time functions**: formatting and display of time (TIME, CONVSECS)
*   **Utility functions**: general utilities (ISINT, COMP)

The command "@list/functions" lists all functions on the game.
The command "@function" lists only the game's custom global functions defined via the @function command.

# Attribute functions

These functions can access or alter information stored in attributes on objects.

*   aposs()
*   attrib_set()
*   default()
*   edefault()
*   eval()
*   flags()
*   get()
*   grep()
*   grepi()
*   hasattr()
*   hasattrp()
*   hasattrval()
*   hasflag()
*   lattr()
*   lflags()
*   nattr()
*   obj()
*   owner()
*   pfun()
*   poss()
*   reglattr()
*   regrep()
*   regrepi()
*   regxattr()
*   set()
*   subj()
*   udefault()
*   ufun()
*   ulambda()
*   uldefault()
*   ulocal()
*   v()
*   wildgrep()
*   wildgrepi()
*   xattr()
*   xget()
*   zfun()

See also: [ATTRIBUTES], [NON-STANDARD ATTRIBUTES]

# Bitwise functions

These functions treat integers as a sequence of binary bits (either 0 or 1) and manipulate them.

For example, 2 is represented as '0010' and 4 as '0100'. If these two numbers are bitwise-or'ed together with BOR(), the result is 6, or (in binary) '0110'. These functions are useful for storing small lists of toggle (Yes/No) options efficiently.

*   baseconv()
*   band()
*   bnand()
*   bnot()
*   bor()
*   bxor()
*   shl()
*   shr()

# Boolean functions

Boolean functions all return 0 or 1 as an answer.

Your MUSH may be configured to use traditional PennMUSH booleans, in which case non-zero numbers, non-negative db#'s, and strings are all considered "true" when passed to these functions. Alternatively, your MUSH may be using TinyMUSH 2.2 booleans, in which case only non-zero numbers are "true". Check @config tiny_booleans.

*   and()
*   cand()
*   cor()
*   eq()
*   gt()
*   gte()
*   lt()
*   lte()
*   nand()
*   neq()
*   nor()
*   not()
*   or()
*   t()
*   xor()

See also: [BOOLEAN VALUES], [@config]

# Communication functions

Communication functions are side-effect functions that send a message to an object or objects.

*   cemit()
*   emit()
*   lemit()
*   message()
*   nsemit()
*   nslemit()
*   nsoemit()
*   nspemit()
*   nsprompt()
*   nsremit()
*   nszemit()
*   oemit()
*   pemit()
*   prompt()
*   remit()
*   zemit()

See also: [Channel functions], [Mail functions]

# Connection functions

Connection functions return information about the connections open on a game, or about specific connections.

*   addrlog()
*   cmds()
*   conn()
*   connlog()
*   connrecord()
*   doing()
*   height()
*   host()
*   hidden()
*   idle()
*   ipaddr()
*   lports()
*   lwho()
*   lwhoid()
*   mwho()
*   mwhoid()
*   nmwho()
*   nwho()
*   player()
*   ports()
*   pueblo()
*   recv()
*   sent()
*   ssl()
*   terminfo()
*   width()
*   xmwho()
*   xmwhoid()
*   xwho()
*   xwhoid()
*   zmwho()
*   zwho()

# Dbref functions

Dbref functions return a dbref or list of dbrefs related to some value on an object.

*   children()
*   con()
*   entrances()
*   exit()
*   followers()
*   following()
*   home()
*   lcon()
*   lexits()
*   loc()
*   locate()
*   lparent()
*   lplayers()
*   lsearch()
*   lvcon()
*   lvexits()
*   lvplayers()
*   namelist()
*   next()
*   nextdbref()
*   num()
*   owner()
*   parent()
*   pmatch()
*   rloc()
*   rnum()
*   room()
*   where()
*   zone()

See also: [DBREF], [Information functions]

# Information functions

Information functions return values related to objects or the game.

*   accname()
*   alias()
*   andflags()
*   andlflags()
*   andlpowers()
*   config()
*   controls()
*   csecs()
*   ctime()
*   elock()
*   findable()
*   flags()
*   fullalias()
*   fullname()
*   getpids()
*   hasattr()
*   hasattrp()
*   hasflag()
*   haspower()
*   hastype()
*   iname()
*   lflags()
*   lock()
*   lockflags()
*   lockowner()
*   locks()
*   lpids()
*   lstats()
*   money()
*   moniker()
*   msecs()
*   mtime()
*   mudname()
*   mudurl()
*   name()
*   nattr()
*   nearby()
*   objid()
*   objmem()
*   orflags()
*   orlflags()
*   orlpowers()
*   pidinfo()
*   playermem()
*   poll()
*   powers()
*   quota()
*   restarts()
*   type()
*   version()
*   visible()

See also: [Dbref functions]

# List functions

List functions take at least one list of elements and return transformed lists or one or more members of those lists. Most of these functions can take an arbitrary `<delimiter>` argument to specify what delimits list elements; if none is provided, a space is used by default.

*   elements()
*   extract()
*   filter()
*   filterbool()
*   first()
*   fold()
*   grab()
*   graball()
*   index()
*   itemize()
*   items()
*   iter()
*   last()
*   ldelete()
*   linsert()
*   lreplace()
*   lockfilter()
*   map()
*   match()
*   matchall()
*   member()
*   mix()
*   munge()
*   namegrab()
*   namegraball()
*   randword()
*   remove()
*   rest()
*   revwords()
*   setdiff()
*   setinter()
*   setsymdiff()
*   setunion()
*   shuffle()
*   sort()
*   sortby()
*   sortkey()
*   splice()
*   step()
*   table()
*   unique()
*   wordpos()
*   words()

See also: [LISTS]

# Math functions

Math functions take one or more floating point numbers and return a numeric value.

*   abs()
*   acos()
*   add()
*   asin()
*   atan()
*   atan2()
*   bound()
*   ceil()
*   cos()
*   ctu()
*   dist2d()
*   dist3d()
*   e()
*   exp()
*   fdiv()
*   floor()
*   fmod()
*   fraction()
*   ln()
*   lmath()
*   log()
*   max()
*   mean()
*   median()
*   min()
*   mul()
*   pi()
*   power()
*   root()
*   round()
*   sign()
*   sin()
*   sqrt()
*   stddev()
*   sub()
*   tan()
*   trunc()
*   val()

These functions operate only on integers (if passed floating point numbers, they will return an error or misbehave):

*   dec()
*   div()
*   floordiv()
*   inc()
*   mod()
*   remainder()

Math functions are affected by a number of @config options, including the TinyMUSH compatability options null_eq_zero and tiny_math.

See also: [Vector Functions]

# Vector functions

These functions operate on n-dimensional vectors. A vector is a delimiter-separated list of numbers (space-separated, by default):

*   vadd()
*   vcross()
*   vdim()
*   vdot()
*   vmag()
*   vmax()
*   vmin()
*   vmul()
*   vsub()
*   vunit()

See also: [Math functions]

# Regular expression functions

These functions take a regular expression (regexp, or re) and match it against assorted things.

*   regedit()
*   regeditall()
*   regeditalli()
*   regediti()
*   reglattr()
*   reglattrp()
*   regmatch()
*   regmatchi()
*   regnattr()
*   regnattrp()
*   regrab()
*   regraball()
*   regraballi()
*   regrabi()
*   regrep()
*   regrepi()
*   reswitch()
*   reswitchall()
*   reswitchalli()
*   reswitchi()
*   regxattr()
*   regxattrp()

See also: [string functions], [regexp]

# SQL functions

These functions perform queries or other operations on an SQL database to which the MUSH is connected, if SQL support is available and enabled.

*   sql()
*   sqlescape()
*   mapsql()

# String functions

String functions take at least one string and return a transformed string, parts of a string, or a value related to the string(s).

*   accent()
*   after()
*   align()
*   alphamax()
*   alphamin()
*   art()
*   before()
*   brackets()
*   capstr()
*   case()
*   caseall()
*   cat()
*   center()
*   chr()
*   comp()
*   cond()
*   condall()
*   decode64()
*   decompose()
*   decrypt()
*   digest()
*   edit()
*   encode64()
*   encrypt()
*   escape()
*   flip()
*   foreach()
*   formdecode()
*   hmac()
*   if()
*   ifelse()
*   lcstr()
*   left()
*   lit()
*   ljust()
*   lpos()
*   merge()
*   mid()
*   ord()
*   ordinal()
*   pos()
*   regedit()
*   regmatch()
*   repeat()
*   right()
*   rjust()
*   scramble()
*   secure()
*   space()
*   spellnum()
*   squish()
*   strallof()
*   strcat()
*   strdelete()
*   strfirstof()
*   strinsert()
*   stripaccents()
*   stripansi()
*   strlen()
*   strmatch()
*   strreplace()
*   switch()
*   tr()
*   trim()
*   ucstr()
*   urldecode()
*   urlencode()
*   wrap()

See also: [STRINGS]

# Time functions

These functions return times or format times.

*   convsecs()
*   convutcsecs()
*   convtime()
*   convutctime()
*   ctime()
*   etime()
*   etimefmt()
*   isdaylight()
*   mtime()
*   restarttime()
*   secs()
*   starttime()
*   stringsecs()
*   time()
*   timecalc()
*   timefmt()
*   timestring()
*   utctime()
*   uptime()

See also: [TIMEZONES]

# Utility functions

These functions don't quite fit into any other category.

*   allof()
*   ansi()
*   atrlock()
*   beep()
*   benchmark()
*   checkpass()
*   clone()
*   create()
*   die()
*   dig()
*   endtag()
*   firstof()
*   functions()
*   fn()
*   html()
*   ibreak()
*   ilev()
*   inum()
*   isdbref()
*   isint()
*   isnum()
*   isobjid()
*   isregexp()
*   isword()
*   itext()
*   letq()
*   localize()
*   link()
*   list()
*   listq()
*   lnum()
*   lset()
*   null()
*   numversion()
*   objeval()
*   open()
*   pcreate()
*   r()
*   rand()
*   s()
*   scan()
*   set()
*   setq()
*   setr()
*   slev()
*   soundex()
*   soundslike()
*   speak()
*   stext()
*   suggest()
*   tag()
*   tagwrap()
*   tel()
*   testlock()
*   textentries()
*   textfile()
*   unsetq()
*   valid()
*   wipe()
*   @@()
*   uptime()

# @@()

# NULL()

`@@(<expression>)`
`null(<expression>[, ... , <expression>])`

The @@() function does nothing and returns nothing. It does not evaluate its argument. It could be used for commenting, perhaps.

The null() function is similar, but does evaluate its argument(s), so side-effects can occur within a null(). Useful for eating the output of functions when you don't use that output.

See also: [@@]

# ABS()

`abs(<number>)`

Returns the absolute value of a number.

Examples:

```
> say abs(-4)
You say, "4"
```

```
> say abs(2)
You say, "2"
```

See also: [sign()]

# ACCENT()

`accent(<string>, <template>)`

The accent() function will return `<string>`, with characters in it possibly changed to accented ones according to `<template>`. Both arguments must be the same size.

Whether or not the resulting string is actually displayed correctly is client-dependent. Some OSes uses different character sets than the one assumed (Unicode and ISO 8859-1), and some clients strip these 8-bit characters.

For each character in `<string>`, the corresponding character of `<template>` is checked according to the table in 'help accents', and a replacement done. If either the current `<string>` or `<template>` characters aren't in the table, the `<string>` character is passed through unchanged.

See 'help accent2' for some examples.

See also: [stripaccents()], [NOACCENTS], [@nameaccent], [accname()], [ACCENTS]

# ACCENTS

Below is the table of possible accents which can be used with accent() and @nameformat.

*   **Accent Name**: grave
    *   **Description**: Backward slant above letter (À)
    *   **Template Character**: `
    *   **String Character**: A,E,I,O,U,a,e,i,o,u
*   **Accent Name**: acute
    *   **Description**: Forward slant above letter (Á)
    *   **Template Character**: '
    *   **String Character**: A,E,I,O,U,Y,a,e,i,o,u,y
*   **Accent Name**: tilde
    *   **Description**: Wavy line above letter (Ñ)
    *   **Template Character**: ~
    *   **String Character**: A,N,O,a,n,o
*   **Accent Name**: circumflex
    *   **Description**: carat above letter (Â)
    *   **Template Character**: ^
    *   **String Character**: A,E,I,O,U,a,e,i,o,u
*   **Accent Name**: umlaut, diaeresis
    *   **Description**: Two dots above letter (Ä)
    *   **Template Character**: :
    *   **String Character**: A,E,I,O,U,,a,e,i,o,u,y
*   **Accent Name**: ring
    *   **Description**: Small circle above letter (Å)
    *   **Template Character**: o
    *   **String Character**: A,a
*   **Accent Name**: cedilla
    *   **Description**: Small tail below letter (Ç)
    *   **Template Character**: ,
    *   **String Character**: C,c

Continued in [HELP ACCENTS2]

# ACCENTS2

These are non-accent special characters, mostly punctuation and non-roman letters.

*   **Description**: Upside-down ? (¿)
    *   **Template Character**: u
    *   **String Character**: ?
*   **Description**: Upside-down ! (¡)
    *   **Template Character**: u
    *   **String Character**: !
*   **Description**: << quote mark («)
    *   **Template Character**: "
    *   **String Character**: <
*   **Description**: >> quote mark (»)
    *   **Template Character**: "
    *   **String Character**: >
*   **Description**: German sharp s (ß)
    *   **Template Character**: B
    *   **String Character**: s
*   **Description**: Capital thorn (Þ)
    *   **Template Character**: |
    *   **String Character**: P
*   **Description**: Lower-case thorn (Þ)
    *   **Template Character**: |
    *   **String Character**: p
*   **Description**: Capital eth (Ð)
    *   **Template Character**: -
    *   **String Character**: D
*   **Description**: Lower-case eth (ð)
    *   **Template Character**: &
    *   **String Character**: o

See 'HELP ACCENTS3' for examples

# ACCENT2

# ACCENTS3

Some examples of accent() and their expected outputs:

```
> think accent(Aule, ---:)
Aul(e-with-diaeresis)
Aulë
```

```
> think accent(The Nina was a ship, The Ni~a was a ship)
The Ni(n-with-~)a was a ship
The Niña was a ship
```

```
> think accent(Khazad ai-menu!, Khaz^d ai-m^nu!)
Khaz(a-with-^)d ai-m(e-with-^)nu!
Khazâd ai-mênu
```

# ACCNAME()

`accname(<object>)`

accname() returns the name of `<object>`, applying the object's
@nameaccent, if any.

See also: [name()], [fullname()], [iname()], [ACCENTS]

# ACOS()

`acos(<cosine>[, <angle type>])`

Returns the angle that has the given `<cosine>` (arc-cosine), with the angle expressed in the given `<angle type>`, or radians by default.

See 'HELP ANGLES' for more on the `<angle type>`.

See also: [asin()], [atan()], [cos()], [ctu()], [sin()], [tan()]

# ADD()

`add(<number1>, <number2>[, ... , <numberN>])`

Returns the sum of the given numbers.

See also: [MATH FUNCTIONS], [lmath()]

# AFTER()

`after(<string1>, <string2>)`

Returns the portion of `<string1>` that occurs after `<string2>`. If `<string2>` isn't in `<string1>`, the function returns nothing. This is case-sensitive.

Examples:

```
> say after(foo bar baz,bar)
You say, " baz"
```

```
> say after(foo bar baz,ba)
You say, "r baz"
```

See also: [before()], [rest()]

# ALIGN()

# LALIGN()

`align(<widths>, <col>[, ... , <colN>[, <filler>[, <colsep>[, <rowsep>]]]])`
`lalign(<widths>, <colList>[, <delim>[, <filler>[, <colsep>[, <rowsep>]]]])`

Creates columns of text, each column designated by `<col>` arguments. Each `<col>` is individually wrapped inside its own column, allowing for easy creation of book pages, newsletters, or the like. In lalign(), `<colList>` is a `<delim>`-separated list of the columns.

`<widths>` is a space-separated list of column widths. '10 10 10' for the widths argument specifies that there are 3 columns, each 10 spaces wide. You can alter the behavior of a column in multiple ways. (Check 'help align2' for more details)

`<filler>` is a single character that, if given, is the character used to fill empty columns and remaining spaces. `<colsep>`, if given, is inserted between every column, on every row. `<rowsep>`, if given, is inserted between every line. By default, `<filler>` and `<colsep>` are a space, and `<rowsep>` is a newline.

Continued in [help align2]

# ALIGN2

You can modify column behavior within align(). The basic format is:

`[justification]Width[options][(ansi)]`

Justification: Placing one of these characters before the width alters the spacing for this column (e.g: <30). Defaults to < (left-justify).

*   `<` Left-justify
*   `-` Center-justify
*   `>` Right-justify
*   `_` Full-justify
*   `=` Paragraph-justify

Other options: Adding these after the width will alter the column's behaviour in some situtations

*   `.` Repeat for as long as there is non-repeating text in another column.
*   `` ` `` When this column runs out of text, merge with the column to the left
*   `'` When this column runs out of text, merge with the column to the right
*   `$` nofill: Don't use filler after the text. If this is combined with merge-left, the column to its left inherits the 'nofill' when merged.
*   `x` Truncate each (%r-separated) row instead of wrapping at the colwidth
*   `X` Truncate the entire column at the end of the first row instead of wrapping
*   `#` Don't add a `<colsep>` after this column. If combined with merge-left, the column to its left inherits this when merged.

Ansi: Place ansi characters (as defined in 'help ansi()') within ()s to define a column's ansi markup.

See 'help align3' for examples.

See also: [center()], [ljust()], [rjust()], [table()]

# ALIGN3

Examples:

```
> &line me=align(<3 10 20$,([ljust(get(%0/sex),1,,1)]), name(%0),name(loc(%0)))
> th iter(lwho(),u(line,##),%b,%r)
  (M) Walker     Tree
  (M) Ashen-Shug Apartment 306
      ar
  (F) Jane Doe   Nowhere
```

```
> &line me=align(<3 10X 20X$,([ljust(get(%0/sex),1,,1)]), name(%0),name(loc(%0)))
> th iter(lwho(),u(line,##),%b,%r)
  (M) Walker     Tree
  (M) Ashen-Shug Apartment 306
  (F) Jane Doe   Nowhere
```

See 'help align4' for more examples.

# ALIGN4

```
> &haiku me = Alignment function,%rIt justifies your writing,%rBut the words still suck.%rLuke
```

```
> th [align(5 -40 5,,[repeat(-,40)]%r[u(haiku)]%r[repeat(-,40)],,%b,+)]
     +----------------------------------------+
     +          Alignment function,           +
     +       It justifies your writing,       +
     +       But the words still suck.        +
     +                  Luke                  +
     +----------------------------------------+
```

See 'help align5' for more examples.

# ALIGN5

```
> &dropcap me=%b_______%r|__%b%b%b__|%r%b%b%b|%b|%r%b%b%b|_|
> &story me=%r'was the night before Christmas, when all through the house%rNot a creature was stirring, not even a mouse.%rThe stockings were hung by the chimney with care,%rIn hopes that St Nicholas soon would be there.
> th align(9'(ch) 68, u(dropcap), u(story))
 _______
|__   __| 'was the night before Christmas, when all through the house
   | |    Not a creature was stirring, not even a mouse.
   |_|    The stockings were hung by the chimney with care,
In hopes that St Nicholas soon would be there.
```

The dropcap 'T' will be in ANSI cyan-highlight, and merges with the 'story'
column.

```
> th align(>15 60,Walker,Staff & Developer,x,x)
xxxxxxxxxWalkerxStaff & Developerxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

```
> th align(>15 60$,Walker,Staff & Developer,x,x)
xxxxxxxxxWalkerxStaff & Developer
```

# ALLOF()

`allof(<expr>[, ... , <exprN>], <osep>)`

Evaluates every `<expr>` argument (including side-effects) and returns the results of those which are true, in a list separated by `<osep>`. The output separator argument is required, and can be a string of any length (including an empty string; use %b for a space).

The meaning of true or false depends on configuration options as explained in the 'BOOLEAN VALUES' help topics.

```
> &s me=Bats are similar to Rats which are afraid of Cats
> say allof(grab(v(s),rats),grab(v(s),mats),grab(v(s),bats),)
You say, "Rats Bats"
```

```
> say allof(#-1,#101,#2970,,#-3,0,#319,null(This Doesn't Count),|)
You say, "#101|#2970|#319"
```

```
> say allof(foo, 0, #-1, bar, baz,)
You say, "foobarbaz"
```

```
> say allof(foo, 0, #-1, bar, baz,%b)
You say, "foo bar baz"
```

See also: [firstof()], [BOOLEAN VALUES], [strallof()], [filter()]

# ALPHAMAX()

`alphamax(<word>[, ... , <wordN>])`

Takes any number of `<word>` arguments, and returns the one which is lexicographically biggest. That is, the `<word>` would be last in alphabetical order.

This is equivilent to `last(sort(<word> ... <wordN>,a))`.

See also: [alphamin()], [max()]

# ALPHAMIN()

`alphamin(<word>[, ... , <wordN>])`

Takes any number of `<word>` arguments, and returns the one which is lexicographically smallest. That is, the word that would be first in alphabetical order.

This is equivilent to `first(sort(<word> ... <wordN>,a))`.

See also: [alphamax()], [min()]

# AND()

# CAND()

`and(<boolean1>, <boolean2>[, ... , <booleanN>])`
`cand(<boolean1>, <boolean2>[, ... , <booleanN>])`

These functions take any number of boolean values, and return 1 if all are true, and 0 otherwise. and() will always evaluate all its arguments (including side effects), while cand() stops evaluation after the first false argument.

See also: [BOOLEAN VALUES], [nand()], [or()], [xor()], [not()], [lmath()]

# ANDFLAGS()

# ANDLFLAGS()

`andflags(<object>, <string of flag letters>)`
`andlflags(<object>, <list of flag names>)`

These functions return 1 if `<object>` has all of the given flags, and 0 if it does not. andflags() takes a string of single flag letters, while andlflags() takes a space-separated list of flag names. In both cases, a ! before the flag means "not flag".

If there is a syntax error like a ! without a following flag, '#-1 INVALID FLAG' is returned. Unknown flags are treated as being not set.

Example: Check to see if %# is set Wizard and Dark, but not Ansi.

```
> say andflags(%#, WD!A)
```

```
> say andlflags(%#, wizard dark !ansi)
```

See also: [orflags()], [flags()], [lflags()]

# ANDLPOWERS()

`andlpowers(<object>, <list of powers>)`

This function returns 1 if `<object>` has all the powers in a specified list, and 0 if it does not. The list is a space-separated list of power names. A '!' preceding a flag name means "not power".

Thus, `ANDLPOWERS(me, no_quota no_pay)` would return 1 if I was powered no_quota and no_pay. `ANDLPOWERS(me, poll !guest)` would return 1 if I had the poll power but not the guest power.

If there is a syntax error like a ! without a following flag, '#-1 INVALID POWER' is returned. Unknown powers are treated as being not set.

See also: [powers()], [orlpowers()], [POWERS LIST], [@power]

# ANSI()

`ansi(<codes>[ ... <codesN>], <string>)`

This allows you to mark up a string using ANSI terminal effects, 16-color codes, and 256 XTERM colors (specified as color names or hex values).

The old-style `<ansi-codes>` are listed in "help ansi2".
Each block of space-separated `<codes>` can be one or more old-style ANSI codes, as listed in "help ansi2", or a foreground and/or background color. Background colors are prefixed with a "/". Each color can be one of:

*   `+<colorname>` (for a list of valid names, see "help colors()")
*   a hexcode, optionally in angle brackets (#000000, `<#ff0055>`, etc)
*   a list of red, green and blue values from 0-255, in angle brackets (`<0 0 0>`, `<255 0 85>`, etc)
*   a number from 0-255; this is the same as using "+xterm<number>", for Rhost compatability.

For example, "ansi(+orange/#0000ff,Test)" would color "Test" in orange, on a blue background. In the event that your client does not support those colors, PennMUSH will downgrade the color to the closest fit that your client can understand.

Codes are parsed from left to right so, with later codes overriding earlier ones. So, for example:
`ansi(y /+green B <#ffffff>, test)`
would show white text on an ANSI-blue background.

See 'help ansi3' for more examples.

See also: [ANSI], [COLOR], [@sockset], [colorstyle], [colors()]

# ANSI2

Old-style valid color codes are:

*   **f**: flash
*   **F**: not flash
*   **h**: hilite
*   **H**: not hilite
*   **u**: underscore
*   **U**: not underscore
*   **i**: inverse
*   **I**: not inverse
*   **n**: normal
*   **d**: default foreground
*   **D**: default background
*   **x**: black foreground
*   **X**: black background
*   **r**: red foreground
*   **R**: red background
*   **g**: green foreground
*   **G**: green background
*   **y**: yellow foreground
*   **Y**: yellow background
*   **b**: blue foreground
*   **B**: blue background
*   **m**: magenta foreground
*   **M**: magenta background
*   **c**: cyan foreground
*   **C**: cyan background
*   **w**: white foreground
*   **W**: white background

For example, "ansi(fc, Test)" would hilight "Test" in flashing cyan. Default foreground and background use the client's default color for fore and back.

# ANSI3

Bright yellow text on a blue background:

```
> think ansi(yB, foo)
```

Orange text on an ANSI-green background:

```
> think ansi(G+orange, bar)
```

Underlined pink text on a purple background

```
> think ansi(u+lightsalmon/#a020f0, ugly)
```

ANSI-blue text on a bisque background

```
> think ansi(+yellow/+bisque b, the 'b' overrides the earlier '+yellow')
```

# APOSS()

# %a

`aposs(<object>)`

Returns the absolute possessive pronoun - his/hers/its/theirs - for an object. The %a substitution returns the absolute possessive pronoun of the enactor.

See also: [obj()], [poss()], [subj()]

# ART()

`art(<string>)`

This function returns the proper article, "a" or "an", based on whether or not `<string>` begins with a vowel.

# ASIN()

`asin(<sine>[, <angle type>])`

Returns the angle with the given `<sine>` (arc-sine), with the angle expressed in the given `<angle type>`, or radians by default.

See 'HELP ANGLES' for more on the angle type.

See also: [acos()], [atan()], [cos()], [ctu()], [sin()], [tan()]

# ATAN()

# ATAN2()

`atan(<tangent>[, <angle type>])`
`atan2(<number1>, <number2>[, <angle type>])`

Returns the angle with the given `<tangent>` (arc-tangent), with the angle expressed in the given `<angle type>`, or radians by default.

`atan2(x, y)` is like `atan(fdiv(x, y))`, except y can be 0, and the signs of both arguments are used in determining the sign of the result. It is useful in converting between cartesian and polar coordinates.

See 'HELP ANGLES' for more on the angle type.

See also: [acos()], [asin()], [cos()], [ctu()], [sin()], [tan()]

# ATRLOCK()

`atrlock(<object>/<attrib>[, [on|off]])`

When given a single `<object>/<attribute>` pair as an argument, returns 1 if the attribute is locked, 0 if unlocked, and #-1 if the attribute doesn't exist or can't be read by the function's caller.

When given a second argument of "on" (or "off"), attempts to lock (or unlock) the specified attribute, as per @atrlock.

A locked attribute is one which has the "locked" attribute flag, so this function is roughly equivilent to:

`hasattr(<object>/<attrib>, locked)`
`set(<object>/<attribute>, [!]locked)`

except that the attribute's owner is also changed when you lock it via atrlock().

See also: [@atrlock], [@atrchown], [hasflag()]

# ATTRIB_SET()

`attrib_set(<object>/<attrib>[, <value>])`

Sets or clears an attribute. With a `<value>`, it sets the attribute, without one, it clears the attribute. This is an easier-to-read replacement for the old `set(<object>, <attrib>:<value>)` notation, and a less destructive replacement for wipe() that won't destroy entire attribute trees in one shot.

If there is a second argument, then attrib_set() will create an attribute, even if the second argument is empty (in which case attrib_set() will create an empty attribute). If the empty_attrs configuration option is off, the attribute will be set to a single space. This means that `attrib_set(me/foo,%0)` will _always_ create an attribute.

See also: [set()], [@set]

# BAND()

`band(<integer>, <integer>[, ... , <integerN>])`

Does a bitwise AND of all its arguments, returning the result (a number with only the bits set in every argument set in it).

See also: [BITWISE FUNCTIONS], [lmath()]

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

```
> say before(foo bar baz,bar)
You say, "foo"
```

```
> say before(foo bar baz,r)
You say, "foo ba"
```

```
> say before(foo bar baz,a)
You say, "foo b"
```

See also: [after()], [first()]

# BENCHMARK()

`benchmark(<expression>, <number>[, <sendto>])`

Evaluates `<expression>` `<number>` times, and returns the average, minimum, and maximum time it took to evaluate `<expression>` in microseconds. If a `<sendto>` argument is given, benchmark() instead pemits the times to the object `<sendto>`, and returns the result of the last evaluation of `<expression>`.

Example:

```
> think benchmark(iter(lnum(1,100), ##), 200)
Average: 520.47   Min: 340   Max: 1382
```

```
> think benchmark(iter(lnum(1,100), %i0), 200)
Average: 110.27   Min: 106   Max: 281
```

# BRACKETS()

`brackets(<string>)`

Returns a count of the number of left and right square brackets, parentheses, and curly braces in the string, in that order, as a space-separated list of numbers. This is useful for finding missing or extra brackets in MUSH code. `<string>` is evaluated.

Example:

```
> @desc me=This is [ansi(h,a test)] of the { brackets() function.
> think brackets(v(desc))
1 1 2 2 1 0
```

# BNAND()

`bnand(<integer1>, <integer2>)`

Returns `<integer1>` with every bit that was set in `<integer2>` cleared.

See also: [BITWISE FUNCTIONS]

# BNOT()

`bnot(<integer>)`

Returns the bitwise complement of `<integer>`. Every bit set in it is cleared, and every clear bit is set.

See also: [BITWISE FUNCTIONS]

# BOR()

`bor(<integer>, <integer>[, ... , <integerN>])`

Does a bitwise OR of all its arguments, returning the result. (A number with a bit set if that bit appears in any of its arguments).

See also: [BITWISE FUNCTIONS], [lmath()]

# BOUND()

`bound(<number>, <lower bound>, <higher bound>)`

bound() returns `<number>` if it is between `<lower bound>` and `<higher bound>`. If it's lower than `<lower bound>`, `<lower bound>` is returned. If it's higher than `<higher bound>`, `<higher bound>` is returned.

If you just want to know whether `<number>` is within the range of `<lower>` to `<higher>`, consider using `lte(<lower>, <number>, <higher>)` instead to get a boolean result.

See also: [ceil()], [floor()], [round()], [trunc()]

# BXOR()

`bxor(<integer>, <integer>[, ... , <integerN>])`

Does a bitwise XOR of all its arguments, returning the result. (A number with a bit set if it's set in only one of its arguments).

See also: [BITWISE FUNCTIONS], [lmath()]

# CAPSTR()

`capstr(<string>)`

Returns `<string>` with the first character capitalized.

Example:

```
> think capstr(foo bar baz)
Foo bar baz
```

See also: [lcstr()], [ucstr()]

# CAT()

# STRCAT()

`cat(<string>[, ... , <stringN>])`
`strcat(<string1>[, ... , <stringN>])`

These functions concatenate multiple strings together. cat() adds a space between each string; strcat() does not.

Example:

```
> say cat(foo bar, baz blech)
You say, "foo bar baz blech"
```

```
> say strcat(foo bar, baz blech)
You say, "foo barbaz blech"
```

# CENTER()

`center(<string>, <width>[, <fill>[, <rightfill>]])`

This function will center `<string>` within a field `<width>` characters wide, using the `<fill>` string for padding on the left side of the string, and `<rightfill>` for padding on the right side. `<rightfill>` defaults to the mirror-image of `<fill>` if not specified. `<fill>` defaults to a space if neither `<fill>` nor `<rightfill>` are specified.

If `<string>` divides `<width>` into uneven portions, the left side will be one character shorter than the right side.

Examples:

```
> say center(X,5,-)
You say, "--X--"
```

```
> say center(X,5,-=)
You say, "-=X=-"
```

```
> say center(.NEAT.,15,-,=)
You say, "----.NEAT.====="
```

```
> say center(hello,16,12345)
You say, "12345hello543215"
```

See also: [align()], [ljust()], [rjust()]

# CHECKPASS()

`checkpass(<player>, <string>)`

Returns 1 if `<string>` matches `<player>`'s password, and 0 otherwise. If `<player>` has no password, this function will always return 1.

This function can only be used by wizards.

See also: [@password], [@newpassword]

# CHR()

# ORD()

`chr(<number>)`
`ord(<character>)`

ord() returns the numerical value of the given character. chr() returns the character with the given numerical value.

Examples:

```
> say ord(A)
You say, "65"
```

```
> say chr(65)
You say, "A"
```

# CLONE()

`clone(<object>[, <new name>[, <dbref>[, preserve]]])`

This function clones `<object>`, as per @clone, and returns the dbref number of the clone, or #-1 if the object could not be cloned.

The clone will have the same name as the original object unless you give a `<new name>` for it. Normally, the clone will be created with the first available dbref, but wizards and objects with the pick_dbref power may give the `<dbref>` of a garbage object to use instead.

If the optional fourth argument is the string preserve, acts as @clone/preserve.
Note: If @create or @clone is restricted or disabled, clone() will also be restricted/disabled.

See also: [@clone], [create()], [dig()], [open()]

# CMDS()

`cmds(<player|descriptor>)`

Returns the number of commands issued by a player during this connection as indicated by WHO.

You must be a Wizard, Royalty or See_All to use this function on anyone but yourself.

See also: [CONNECTION FUNCTIONS]

# SENT()

`sent(<player|descriptor>)`

Returns the number of characters sent by a player during this connection as indicated by SESSION.

You must be a Wizard, Royalty or See_All to use this function on anyone but yourself.

See also: [Connection Functions]

# RECV()

`recv(<player|descriptor>)`

Returns the number of characters received by a player during this connection as indicated by SESSION.

You must be a Wizard, Royalty or See_All to use this function on anyone but yourself.

See also: [Connection Functions]

# COLORS()

`colors()`
`colors(<wildcard>)`
`colors(<colors>, <format>)`

With no arguments, colors() returns an unsorted, space-separated list of colors that PennMUSH knows the name of. You can use these colors in `ansi(+<colorname>,text)`. The colors "xterm0" to "xterm255" are not included in the list, but can also be used in ansi().

With one argument, returns an unsorted, space-separated list of colors that match the wildcard pattern `<wildcard>`.

With two arguments, colors() returns information about specific colors. `<colors>` can be any string accepted by the ansi() function's first argument. `<format>` must be one of:

*   **hex, x**: return a hexcode in the format #rrggbb.
*   **rgb, r**: return the RGB components as a list (0 0 0 - 255 255 255)
*   **xterm256, d**: return the number of the xterm color closest to the given `<color>`.
*   **xterm256x,h**: return the number of the xterm color in base 16.
*   **16color, c**: return the letter of the closest ANSI color code (possibly including 'h' for highlight fg colors).
*   **name**: return a list of names of all the colors exactly matching the given colors, or '#-1 NO MATCHING COLOR NAME' if there is no exact match with a named color.
*   **auto**: returns the colors in the same format(s) they were given in.

It can be used for working out how certain colors will downgrade to people using clients which aren't fully color-capable.

`<format>` can also include the word "styles", in which case all ANSI styling options (f, u, i and h) present in `<colors>` are included in the output.

See 'help colors2' for examples.

See also: [ansi()], [valid()], [colorstyle]

# colors2

Examples:

```
> think colors(*yellow*)
greenyellow yellowgreen lightgoldenrodyellow lightyellow yellow lightyellow1 lightyellow2 lightyellow3 lightyellow4 yellow1 yellow2 yellow3 yellow4
```

```
> think colors(+yellow, hex)
#ffff00
```

```
> think colors(+yellow, xterm256)
226
```

```
> think colors(+yellow, 16color)
yh
```

```
> think colors(/+yellow, 16color)
Y
```

```
> think colors(#ffff00, name)
yellow yellow1
```

```
> think colors(iuB+red, hex styles)
ui#ff0000/#0000ee
```

```
> think colors(+blue huyG/+black, auto)
hy/+black
```

# COMP()

`comp(<value1>, <value2>[, <type>])`

comp() compares two values. It returns 0 if they are the same, -1 if `<value1>` is less than/precedes alphabetically `<value2>`, and 1 otherwise.

By default the comparison is a case-sensitive lexicographic (string) comparison. By giving the optional `<type>`, the comparison can be specified:

*   **A**: Maybe case-sensitive lexicographic (default)
*   **I**: Always case-insensitive lexicographic
*   **D**: Dbrefs of valid objects
*   **N**: Integers
*   **F**: Floating point numbers

Whether or not the a sort type is case-sensitive or not depends on the particular MUSH and its environment.

See also: [strmatch()], [eq()]

# CON()

`con(<object>)`

Returns the dbref of the first object in the `<object>`'s inventory.

You can get the complete contents of any container you may examine, regardless of whether or not objects are dark. You can get the partial contents (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the contents of anything else, regardless of whether or not you have objects in it.

See also: [lcon()], [next()]

# COND()

# CONDALL()

# NCOND()

# NCONDALL()

`cond(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`
`condall(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`
`ncond(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`
`ncondall(<cond>, <expr>[, ... , <condN>, <exprN>][, <default>])`

cond() evaluates `<cond>`s until one returns a true value. Should none return true, `<default>` is returned.

condall() returns all `<expr>`s for those `<cond>`s that evaluate to true, or `<default>` if none are true.

ncond() and ncondall() are identical to cond(), except it returns `<expr>`s for which `<cond>`s evaluate to false.

Examples:

```
> say cond(0,This is false,#-1,This is also false,#123,This is true)
You say, "This is true"
```

```
> say ncond(0,This is false,#-1,This is also false,#123,This is true)
You say, "This is false"
```

```
> say ncondall(0,This is false,#-1,This is also false,#123,This is true)
You say, "This is falseThis is also false"
```

See also: [firstof()], [allof()]

# CONFIG()

`config([<option>])`

With no arguments, config() returns a list of config option names. If `<option>` is given, config() returns the value of the given option Boolean configuration options will return values of "Yes" or "No".

Example:

```
> think config(money_singular)
Penny
```

# CONN()

`conn(<player|descriptor>)`

This function returns the number of seconds a player has been connected. `<player>` should be the full name of a player or a dbref. You can also use a `<descriptor>` to get connection information for a specific connection when a player is connected more than once. Wizards can also specify the descriptor of a connection which is still at the login screen.

This function returns -1 for invalid `<player|descriptor>`s, offline players and players who are dark, if the caller is not able to see them.

See also: [CONNECTION FUNCTIONS]

# CONTROLS()

`controls(<object>, <victim>[/<attribute>])`

With no `<attribute>`, this function returns 1 if `<object>` controls `<victim>`, or 0, if it does not. With an `<attribute>`, it will return 1 if `<object>` could successfully set `<attribute>` on `<victim>` (or alter `<attribute>`, if it already exists). If one of the objects does not exist, it will return #-1 ARGN NOT FOUND (where N is the argument which is the invalid object). If `<attribute>` is not a valid attribute name, it will return #-1 BAD ATTR NAME. You must control `<object>` or `<victim>`, or have the See_All power, to use this function.

See also: [visible()], [CONTROL]

# CONVSECS()

# CONVUTCSECS()

`convsecs(<seconds>[, <timezone>])`
`convutcsecs(<seconds>)`

This function converts `<seconds>` (the number of seconds which have elapsed since midnight on January 1, 1970 UTC) to a time string. Because it's based on UTC, but returns local time, convsecs(0) is not going to be "Thu Jan 1 00:00:00 1970" unless you're in the UTC (GMT) timezone.

If a `<timezone>` argument is given, the return value is based on that timezone instead of the MUSH server's local time. See 'help timezones' for more information on valid timezones.

If Extended convtime() is supported (see @config compile), negative values for `<seconds>` representing dates prior to 1970 are allowed.

`convutcsecs(<seconds>)` is an alias for `convsecs(<seconds>, utc)`.

Examples:

```
> say secs()
You say, "709395750"
```

```
> say convsecs(709395750)
You say, "Wed Jun 24 10:22:54 1992"
```

```
> say convutcsecs(709395750)
You say, "Wed Jun 24 14:22:30 1992"
```

See also: [convtime()], [time()], [timefmt()]

# CONVTIME()

# CONVUTCTIME()

`convtime(<time string>,[<timezone>])`
`convutctime(<time string>)`

This functions converts a time string to the number of seconds since Jan 1, 1970 GMT. A time string is of the format:
`Ddd MMM DD HH:MM:SS YYYY`
where Ddd is the day of the week, MMM is the month, DD is the day of the month, HH is the hour in 24-hour time, MM is the minutes, SS is the seconds, and YYYY is the year. If you supply an incorrectly formatted string, it will return #-1.

convutctime() and convtime() with a second argument of 'utc' assume the timestring is based on UTC time. Other time zones can be specified too. If no `<timezone>` is given, the server's timezone is used.

If the extended convtime() is supported (See @config compile), more formats for the date are enabled, including ones missing the day of week and year, and a 'Month Day Year' format. In this case, convtime() can also handle dates prior to 1970 (in which case a negative number will be returned).

Example:

```
> say time()
You say, "Wed Jun 24 10:22:54 1992"
```

```
> say convtime(Wed Jun 24 10:22:54 1992)
You say, "709395774"
```

See also: [convsecs()], [time()], [timezones]

# COS()

`cos(<angle>[, <angle type>])`

Returns the cosine of `<angle>`. Angle must be in the given angle type, or radians by default.

Examples:

```
> say cos(90, d)
You say, "0"
```

```
> say cos(1.570796)
You say, "0"
```

See 'HELP ANGLES' for more on the angle type.

See also: [acos()], [asin()], [atan()], [ctu()], [sin()], [tan()]

# PCREATE()

`pcreate(<name>, <password>[, <dbref>])`

Creates a player with a given `<name>` and `<password>`. This function can only be used by wizards.

The optional third argument can be used to specify a garbage object to use for the new player.

See also: [@pcreate], [create()], [dig()], [open()]

# CREATE()

`create(<object>[, <cost>[, <dbref>]])`

This function creates an object with name `<object>` for `<cost>` pennies, and returns the dbref number of the created object. It returns #-1 on error.

Wizards may also specify a `<dbref>`; if this refers to a garbage object, the new object is created with this dbref.

See also: [@create], [pcreate()], [dig()], [open()]

# CTIME()

# CSECS()

`ctime(<object>[, <utc>])`
`csecs(<object>)`

ctime() returns the date and time that `<object>` was created. The time returned is in the server's local timezone, unless `<utc>` is true, in which case the time is in the UTC timezone.

csecs() returns the time as the number of seconds since the epoch. Anyone can get the creation time of any object in the game.

See also: [mtime()], [time()], [secs()], [objid()]

# ANGLES

In any function which accepts an angle type, the argument can be one of 'd' for degrees, 'r' for radians, or 'g' for gradians. Gradians are not used often, but it's included for completeness.

As a refresher, there are 180 degrees in pi radians in 200 gradians.

See also: [acos()], [asin()], [atan()], [cos()], [ctu()], [sin()], [tan()]

# CTU()

`ctu(<angle>, <from>, <to>)`

Converts between the different ways to measure angles. `<from>` controls what the angle is treated as, and `<to>` what form it is turned into. See HELP ANGLES for more information.

Example:

```
> say 90 degrees is [ctu(90, d, r)] radians
You say, "90 degrees is 1.570796 radians"
```

See also: [acos()], [asin()], [atan()], [cos()], [sin()], [tan()]

# DEC()

`dec(<integer>)`
`dec(<string-ending-in-integer>)`

dec() returns the given `<integer>` minus 1. If given a string that ends in an integer, it decrements only the final integer portion. That is:

```
> think dec(3)
2
```

```
> think dec(hi3)
hi2
```

```
> think dec(1.3.3)
1.3.2
```

```
> think dec(1.3)
1.2
```

Note especially the last example, which will trip you up if you use floating point numbers with dec() and expect it to work like sub().

If the null_eq_zero @config option is on, using dec() on a string which does not end in an integer will return `<string>-1`. When null_eq_zero is turned off, it will return an error.

See also: [inc()], [sub()]

# DECOMPOSE()

`decompose(<string>)`

decompose() works like escape() with the additional caveat that it inserts parse-able characters to recreate `<string>` exactly after one parsing. It takes care of multiple spaces, '%r's, and '%t's.

Example:

```
> think decompose(This is \[a [ansi(y,test)]\][space(3)])
This is \[a%b[ansi(y,test)]\] %b%b
```

See also: [@decompile2], [escape()], [secure()], [\]]

# DEFAULT()

`default([<obj>/]<attr>[, ... ,[<objN>]/<attrN>], <default>)`

This function returns the value of the first possible `<obj>/<attr>`, as if retrieved via the get() function, if the attribute exists and is readable by you. Otherwise, it evaluates `<default>`, and returns that. Note that `<default>` is only evaluated if none of the given attributes exist or can be read. Note further than an empty attribute counts as an existing attribute.

This is useful for code that needs to return the value of an attribute, or an error message or default case, if that attribute does not exist.

Examples:

```
> &TEST me=apple orange banana
> say default(me/Test, No fruits!)
You say "apple orange banana"
```

```
> &TEST ME
> say default(me/Test, No fruits!)
You say "No fruits!"
```

See also: [get()], [hasattr()], [ufun()], [edefault()], [udefault()], [uldefault()], [strfirstof()]

# STRDELETE()

# DELETE()

`strdelete(<string>, <first>, <len>)`

Return a modified `<string>`, with `<len>` characters starting after the character at position `<first>` deleted. In other words, it copies `<first>` characters, skips `<len>` characters, and then copies the remainder of the string. If `<len>` is negative, deletes characters leftwards from `<first>`. Characters are numbered starting at 0.

Examples:

```
> say strdelete(abcdefgh, 3, 2)
You say, "abcfgh"
```

```
> say strdelete(abcdefgh, 3, -2)
You say, "abefgh"
```

delete() is an alias for strdelete(), for backwards compatability.

See also: [strreplace()], [strinsert()], [mid()], [ldelete()]

# DIE()

`die(<number of times to roll die>, <number of sides on die>[, <show>])`

This function simulates rolling dice. It "rolls" a die with a given number of sides, a certain number of times, and adds the results. For example, `DIE(2, 6)` would roll "2d6" - two six-sided dice, generating a result in the range 2-12. The maximum number of dice this function will roll in a single call is 700. If `<show>` is true, the result will be a space-seperated list of the individual rolls rather than their sum.

Examples:

```
> think die(3, 6)
6
```

```
> think die(3, 6, 1)
5 2 1
```

See also: [rand()]

# DIG()

`dig(<name>[, <exit to>[, <exit from>[, <room dbref>, <to dbref>, <from dbref>]]])`

This function digs a room called `<name>`, and optionally opens and links `<exit to>` and `<exit from>`, like the normal @dig command. It returns the dbref number of the new room.

Wizards and objects with the pick_dbref power can supply optional fourth through sixth arguments to specify garbage objects to use for the new room and exits.

See also: [@dig], [open()], [@open], [create()], [pcreate()]

# DIGEST()

# MD5

# SHA1

# CHECKSUM

# HASH

`digest(list)`
`digest(<algorithm>, <string>)`

Returns a checksum (hash, digest, etc.) of `<string>` using the given `<algorithm>`. The result is a unique large number represented in base 16.

Typically at least the following algorithms are supported:

md4 md5 ripemd160 sha1 sha224 sha256 sha384 sha512 whirlpool

Depending on the host's OpenSSL version and how it was configured, there might be more (or less) available. digest(list) returns the methods a particular server understands if the OpenSSL library version being used is recent enough (1.0.0 and higher), or '#-1 LISTING NOT SUPPORTED' on older versions. For portable code, stick with MD5, SHA1 and the SHA2 family.

Example:

```
> think iter(digest(list), %i0(foo) => [digest(%i0, foo)], %b, %r)
...
MD4(foo) => 0ac6700c491d70fb8650940b1ca1e4b2
MD5(foo) => acbd18db4cc2f85cedef654fccc4a4d8
MDC2(foo) => 5da2a8f36bf237c84fddf81b67bd0afc
RIPEMD160(foo) => 42cfa211018ea492fdee45ac637b7972a0ad6873
SHA1(foo) => 0beec7b5ea3f0fdbc95d0dd47f3c5bc275da8a33
SHA224(foo) => 0808f64e60d58979fcb676c96ec938270dea42445aeefcd3a4e6f8db
...
```

See also: [encode64()], [encrypt()], [hmac()]

# DIST2D()

`dist2d(<x1>, <y1>, <x2>, <y2>)`

Returns the distance between two points in the Cartesian plane that have coordinates (`<x1>`, `<y1>`) and (`<x2>`, `<y2>`).

See also: [dist3d()], [lmath()]

# DIST3D()

`dist3d(<x1>, <y1>, <z1>, <x2>, <y2>, <z2>)`

Returns the distance between two points in space, with coordinates (`<x1>`, `<y1>`, `<z1>`) and (`<x2>`, `<y2>`, `<z2>`).

See also: [dist2d()], [lmath()]

# DIV()

# FLOORDIV()

# FDIV()

`div(<number1>, <number2>[, ... , <numberN>])`
`fdiv(<number1>, <number2>[, ... , <numberN>])`
`floordiv(<number1>, <number2>[, ... , <numberN>])`

These functions divide `<number1>` by `<number2>` (and, for each subsequent argument, divide the previous result by `<numberN>`) and return the final result.

div() returns the integer part of the quotient. floordiv() returns the largest integer less than or equal to the quotient; for positive numbers, they are identical, but for negative numbers they may differ. fdiv() returns the floating-point quotient.

Examples:

`div(13,4)` ==> 3 and `floordiv(13,4)` ==> 3
`div(-13,4)` ==> -3 but `floordiv(-13,4)` ==> -4
`div(13,-4)` ==> -3 but `floordiv(13,-4)` ==> -4
`div(-13,-4)` ==> 3 and `floordiv(-13,-4)` ==> 3

`fdiv(13,4)` ==> 3.25 `fdiv(-13,4)` ==> -3.25
`fdiv(13,-4)` ==> -3.25 `fdiv(-13,-4)` ==> 3.25

Note that `add(mul(div(%0,%1),%1),remainder(%0,%1))` always yields %0, and `add(mul(floordiv(%0,%1),%1),modulo(%0,%1))` also always yields %0.

See also: [modulo()], [lmath()]

# DOING()

`doing(<player|descriptor>)`

When given the name of a player or descriptor, doing() returns the player's @doing. If no matching player or descriptor is found, or the descriptor is not yet connected to a player, an empty string is returned.

See also: [@poll], [@doing], [poll()]

# E()

# EXP()

`e([<number>])`

With no argument, returns the value of "e" (2.71828182845904523536, rounded to the game's float_precision setting).

If a `<number>` is given, it returns e to the power of `<number>`.

exp() is an alias for e().

See also: [power()], [log()]

# EDEFAULT()

`edefault([<obj>/]<attr>, <default case>)`

This function returns the evaluated value of `<obj>/<attr>`, as if retrieved via the get_eval() function, if the attribute exists and is readable by you. Otherwise, it evaluates `<default case>`, and returns that. `<default case>` is only evaluated if the attribute does not exist or cannot be read.

Example:

```
> &TEST me=You have lost [rand(10)] marbles.
> say edefault(me/Test,You have no marbles.)
You say "You have lost 6 marbles."
```

```
> &TEST me
> say edefault(me/Test,You have no marbles.)
You say "You have no marbles."
```

See also: [get()], [eval()], [ufun()], [default()], [udefault()], [hasattr()]

# EDIT()

`edit(<string>, <search>, <replace>[, ... , <searchN>, <replaceN>])`

For each given `<search>` and `<replace>` pair, edit() replaces all occurrences of `<search>` in `<string>` with the corresponding `<replace>`.

If `<search>` is a caret (^), `<replace>` is prepended.
If `<search>` is a dollar sign ($), `<replace>` is appended.
If `<search>` is an empty string, `<replace>` is inserted between every character, and before the first and after the last.
If `<replace>` is an empty string, `<search>` is deleted from the string.

Example:

```
> say edit(this is a test,^,I think%b,$,.,a test,an exam)
You say "I think this is an exam."
```

edit() can not replace a literal single ^ or $. Use regedit() for that.

See also: [@edit], [regedit()]

# ELEMENTS()

`elements(<list of words>, <list of numbers>[, <delim>[, <osep>]])`

This function returns the words in `<list of words>` that are in the positions specified by `<list of numbers>`. The `<list of words>` are assumed to be space-separated, unless a `<delim>` is given. If `<osep>` is given, the matching words are separated by `<osep>`, otherwise by `<delim>`.

If any of the `<list of numbers>` is negative, it counts backwards from the end of the list of words, with -1 being the last word, -2 the word before last, and so on.

Examples:

```
> say elements(Foo Ack Beep Moo Zot,2 4)
You say "Ack Moo"
```

```
> say elements(Foof|Ack|Beep|Moo,3 1,|)
You say "Beep|Foof"
```

```
> say elements(The last word is foo, -1)
You say "foo"
```

See also: [extract()], [index()], [grab()]

# ELOCK()

`elock(<object>[/<locktype>], <victim>)`

elock() returns 1 if the `<victim>` would pass the @lock/<locktype> on `<object>`, and 0 if it would fail. Any locktype can be given, including user-defined "user:" @locks. If no `<locktype>` is given, it defaults to the Basic lock.

You must be able to examine the lock, which means either that you must control `<object>`, it must be @set VISUAL, or the `<locktype>` lock must be @lset VISUAL.

Examples:

```
> @lock/drop Dancing Slippers=#0
> think elock(Dancing Slippers/drop, Princess)
0
```

```
> @lock/user:test map==*Fred|=*George
> think elock(map/test,*Snape)
0
```

See also: [@lock], [locktypes], [testlock()], [lockfilter()], [@lset]

# EMIT()

# NSEMIT()

`emit(<message>)`
`nsemit(<message>)`

Sends a message to the room, as per @emit.

nsemit() works like @nsemit.

See also: [pemit()], [remit()], [lemit()], [oemit()], [zemit()]

# ENCODE64()

# DECODE64()

# base64

`encode64(<string>)`
`decode64(<string>)`

encode64() returns `<string>` encoded using base-64 format.

decode64() converts a base-64 encoded `<string>` back to its original form.

See also: [encrypt()], [digest()]

# ENCRYPT()

# DECRYPT()

`encrypt(<string>, <password>[, <encode>])`
`decrypt(<string>, <password>[, <encoded>])`

encrypt() returns an encrypted string produced by a simple password-based encrypted algorithm. Good passwords are long passwords. This is not high-security encryption.

If the optional `<encode>` argument is true, the resulting string is further encoded in base-64 so that it only contains alphanumeric characters.

decrypt() decrypts a string encrypted with encrypt(). The `<encoded>` argument indicates that the encrypted string was base-64 encoded.

See also: [encode64()], [digest()]

# ENTRANCES()

`entrances([<object>[, <type>[, <begin>[, <end>]]]])`

With no arguments, the entrances() function returns a list of all exits, things, players, and rooms linked to your location, like @entrances. You can specify an object other than your current location with `<object>`. You can limit the type of objects found by specifying one or more of the following for `<type>`:

*   **a**: all (default)
*   **e**: exits
*   **t**: things
*   **p**: players
*   **r**: rooms

You can also limit the range of the dbrefs searched by giving `<begin>` and `<end>`. If you control `<object>`, or have the Search or See_All powers, all objects linked to `<object>` are returned. Otherwise, only objects you can examine will be included.

See also: [lsearch()], [@entrances]

# EQ()

`eq(<number1>, <number2>[, ... , <numberN>])`

Takes two or more `<number>`s, and returns 1 if they are all equal, and 0 otherwise.

See also: [neq()], [lmath()]

# ESCAPE()

`escape(<string>)`

The escape() function "escapes out" potentially "dangerous" characters, preventing function evaluation in the next pass of the parser. It returns `<string>` after adding the escape character ('\') at the beginning of the string, and before the following characters:

`%` `;` `[` `]` `{` `}` `\` `(` `)` `,` `^` `$`

This function prevents code injection in strings entered by players. It is only needed when `<string>` will be passed through a command or function which will evaluate it again, which can usually be avoided. Since the function preserves the original string, it is, in most cases, a better choice than secure(), but decompose() is often better still.

See also: [decompose()], [secure()], [\]]

# EVAL()

# GET_EVAL()

`eval(<object>, <attribute>)`
`get_eval(<object>/<attribute>)`

eval() and get_eval() are similar to ufun(), in that they evaluate the given `<attribute>` on `<object>`. However, they change the enactor (%#) to the object executing the eval (%!). It does not modify the stack (%0-%9), so the attribute being evaled sees the same values for them that the calling code does. Unless you need this behavior, it is better to use u() instead, which hides the caller's stack.

Example:

```
> &TEST Foo=%b%b%b-[name(me)] (%n)
> &CMD Foo=$test: @emit ufun(me/test) ; @emit eval(me, test)
> test
   -Foo (Mike)
   -Foo (Foo)
```

See also: [get()], [u()], [xget()], [edefault()]

# EXIT()

`exit(<object>)`

Returns the dbref of the first exit in room `<object>`.

You can get the complete exit list of any room you may examine, regardless of whether or not exits are dark. You can get the partial exit list (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the exit list of anything else, regardless of whether or not you have objects in it.

See also: [lexits()], [next()]

# EXTRACT()

`extract(<list>[, <first>[, <length>[, <delimiter>]]])`

This function returns `<length>` elements of `<list>`, counting from the `<first>`th element. If `<length>` is not specified, the default is 1, so `extract(<list>,3)` acts like `elements(<list>,3)`. If `<first>` is not specified, the default is the 1, so `extract(<list>)` acts like `first(<list>)`.

If `<first>` is negative, extract() will begin counting backwards from the end of `<list>`, so -1 starts at the last element, -2 the element before last, and so on.

If `<length>` is negative, extract() will return up to and including the `<length>`th element from the right, so -1 will extract up to the last element, -2 up to the element before last, and so on.

Examples:

```
> think extract(This is a test string,3,2)
a test
```

```
> think extract(Skip the first and last elements, 2, -2)
the first and last
```

```
> think extract(Get just the last three elements,-3, 3)
last three elements
```

See also: [index()], [elements()], [grab()]

# FILTER()

# FILTERBOOL()

`filter([<obj>/]<attr>, <list>[, <delimiter>[, <osep>[, ..., <argN>]]])`
`filterbool([<obj>]/<attr>, <list>[, <delimiter>[, <osep>[, ..., <argN>]]])`

These functions returns the elements of `<list>` for which a user-defined function evaluates to "1" (for filter()), or to a boolean true value (for filterbool()). That function is specified by the first argument (just as with the ufun() function), and the element of the list being tested is passed to that user-defined function as %0. Up to 29 further `<arg>`s can be specified, and will be available in the function as v(1) to v(30).

`<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`.

`filter(<obj>/<attr>, <list>)` is roughly equivalent to `squish(iter(<list>, switch(ufun(<obj>/<attr>, %i0),1,%i0,)))` though the filter() version is much more efficient.

Example:

```
> &IS_ODD test=mod(%0,2)
> say filter(test/is_odd, 1 2 3 4 5 6)
You say, "1 3 5"
```

See also: [anonymous attributes], [firstof()], [allof()], [lockfilter()], [boolean values]

# FINDABLE()

`findable(<object>, <victim>)`

This function returns 1 if `<object>` can locate `<victim>`, or 0 if it cannot. If one of the objects does not exist, it will return #-1 ARGN NOT FOUND (where N is the argument which is the invalid object).

The object executing the function needs to be see_all or control both `<object>` and `<victim>`.

See also: [locate()], [loc()]

# FIRST()

`first(<list>[, <delimiter>])`

Returns the first element of a list.

See also: [before()], [rest()], [last()], [firstof()], [strfirstof()]

# FIRSTOF()

`firstof(<expr>[, ... , <exprN>], <default>)`

Returns the first evaluated `<expr>` that is true. If no `<expr>` arguments are true, `<default>` is returned.

The meaning of true or false is dependent on configuration options as explained in the 'BOOLEAN VALUES' help topics.

This function evaluates arguments one at a time, stopping as soon as one is true.

Examples:

```
> say firstof(0,2)
You say, "2"
```

```
> say firstof(10,11,0)
You say, "10"
```

```
> say firstof(grab(the cat,mommy),grab(in the hat,daddy),#-1 Error)
You say, "#-1 Error"
```

```
> say firstof(get(%#/royal cheese),#-1 This Has No Meaning,0,)
You say, ""
```

See also: [allof()], [BOOLEAN VALUES], [strfirstof()], [filter()]

# FLAGS()

`flags()`
`flags([<object>[/<attribute>]])`

With no arguments, flags() returns a string consisting of the flag letters for each flag on the MUSH. Note that some flags have no letter, and mutlple flags may have the same letter (and so will appear multiple times).

If an `<object>` is given, flags() returns 'P', 'T', 'R' or 'E', depending on whether `<object>` is a player, thing, room, or exit, followed by the flag letter for each flag set on `<object>`.

With an `<object>/<attribute>`, the flag letters for each flag set on the given `<attribute>` are returned.

Examples:

```
> @create Test
> @set Test=no_command puppet
> think flags(Test)
Tnp
```

```
> think flags(me/describe)
$vp
```

See also: [lflags()], [list()]

# LFLAGS()

`lflags()`
`lflags(<object>[/<attribute>])`

With an argument, lflags() returns a space-separated list consisting of the names of all the flags attached to `<object>`, or `<object>`'s `<attribute>`.

Given no arguments, this function returns a space-separated list of all flag names known to the server, as per @list/flags.

Examples:

```
> @create Test
> @set Test=no_command puppet
> think flags(Test)
NO_COMMAND PUPPET
```

```
> think flags(me/describe)
NO_COMMAND VISUAL
```

See also: [flags()], [list()]

# FLIP()

# REVERSE()

`flip(<string>)`

flip() reverses a string. reverse() is an alias for flip().

Example:

```
> say flip(foo bar baz)
You say, "zab rab oof"
```

See also: [revwords()]

# FMOD()

`fmod(<number>, <divisor>)`

Similar to remainder() but may take floating point arguments. The return value is `<number>` - n * `<divisor>`, where n is the quotient of `<number>` / `<divisor>`, rounded towards zero. The result has the same sign as `<number>` and a magnitude less than the magnitude of `<divisor>`.

Example:

```
> think fmod(6.1,2.5)
1.1
```

See also: [fdiv()], [div()], [mod()], [lmath()]

# FOLD()

`fold([<obj>/]<attr>, <list>[, <base case>[, <delimiter>]])`

This function "folds" a list through the user-defined function, set in the specified `<obj>/<attribute>`.

If no `<base case>` is provided, fold() passes the first element of `<list>` as %0, and the second element of `<list>` as %1, to the user-defined function. The user-defined function is then called again, with the result of the first evaluation being %0, and the next (third) element of the list as %1. This is repeated until all the elements of the list have been used. The result of the last call of `<obj>/<attr>` is returned.

If a base case is provided, it is passed as %0, and the first element of list is passed as %1, to the user-defined function. The process for the no-base-case fold() is then used.

The number of times `<attr>` has been called is passed as %2, starting from 0.

Note that it's not possible to pass a `<delimiter>` to fold without also giving a `<base case>`; see the examples for a way around this.

See 'help fold2' for examples.

# FOLD2

Examples:

```
> &REP_NUM test=%0[repeat(%1,%1)]
> say fold(test/rep_num,1 2 3 4 5)
You say, "122333444455555"
```

```
> say fold(test/rep_num,1 2 3 4 5,List:)
You say, "List:122333444455555"
```

```
> &ADD_NUMS test=add(%0,%1)
> say fold(test/add_nums,1 2 3 4 5)
You say, "15"
```

If your list uses a delimiter, you need to give a `<base case>`. This can be a problem for dynamically generated lists. One solution is to use a register and pop the first element off the list. For example:

```
> &GEN_LIST test=lnum(1,rand(5,10),|)
> &ADD_NUMS test=add(%0,%1)
> say letq(fl, u(gen_list), fold(test/add_nums, rest(%q<fl>,|), first(%q<fl>,|), |))
You say, "36"
```

See also: [anonymous attributes]

# FOLLOWERS()

`followers(<object>)`

Returns the list of things and players following object. You must control `<object>`.

See also: [following()], [follow], [unfollow]

# FOLLOWING()

`following(<object>)`

Returns the list of things and players that the object is following. You must control `<object>`.

See also: [followers()], [follow], [unfollow]

# FOREACH()

`foreach([<object>/]<attribute>, <string>[, <start>[, <end>]])`

This function is similar to map(), but instead of calling the given `<object>/<attribute>` for each word in a list, it is called for each character in `<string>`.

For each character in `<string>`, `<object>/<attribute>` is called, with the character passed as %0, and its position in the string as %1 (the first character has position 0). The results are concatenated.

If `<start>` is given, everything before the first occurrence of `<start>` is copied as-is, without being passed to the `<object>/<attribute>`. If `<end>` is given, everything after the first occurrence of `<end>` is copied as-is. The `<start>` and `<end>` characters themselves are not copied.

See 'help foreach2' for examples.

# FOREACH2

Examples:

```
> &add_one me=add(%0,1)
> say foreach(add_one, 54321)
You say, "65432"
```

```
> say [foreach(add_one, This is #0# number, #, #)]
You say, "This is 1 number"
```

```
> &upper me=ucstr(%0)
> say foreach(upper, quiet quiet >shout< quiet, >, <)
You say, "quiet quiet SHOUT quiet"
```

```
> &is_alphanum me=regmatch(%0, \[\[:alnum:\]\])%b
> say foreach(is_alphanum,jt1o+)
You say, "1 1 1 1 0 "
```

See also: [map()], [anonymous attributes]

# FRACTION()

`fraction(<number>[, <whole>])`

This function returns a fraction representing the floating-point `<number>`. Since not all numbers can be expressed as a fraction, dividing the numerator by the denominator of the results will not always return the original `<number>`, but something close to it.

If `<whole>` is true, and `<number>` is greater than 1.0 (or less than -1.0), the return value will be a whole number followed by the fraction representation of the decimal.

Examples:

```
> think fraction(.75)
3/4
```

```
> think fraction(pi())
348987/111086
```

```
> think fraction(2)
2
```

```
> think fraction(2.75)
11/4
```

```
> think fraction(2.75, 1)
2 3/4
```

# FULLNAME()

`fullname(<object>)`

fullname() returns the full name of object `<object>`. It is identical to name() except that for exits, fullname() returns the complete exit name, including all aliases.

Example:

```
> say fullname(south)
You say, "South;sout;sou;so;s"
```

See also: [name()], [accname()], [iname()], [alias()], [fullalias()]

# FUNCTIONS()

`functions([<type>])`

Returns a space-separated list of the names of functions. If `<type>` is "local", only @functions are listed. If "builtin", only builtin functions. If "all" or omitted, both are returned.

See also: [list()], [config()]

# GET()

# XGET()

`get(<object>/<attribute>)`
`xget(<object>, <attribute>)`

These functions return the string stored in an `<object>`'s `<attribute>` attribute, without evaluating it. You must be able to examine the attribute. get() and xget() are identical, apart from the argument separator.

Example:

```
> &test me=This is [a test].
> think get(me/test)
This is [a test].
```

See also: [hasattr()], [visible()], [ufun()], [default()], [udefault()]

# GETPIDS()

`getpids(<object>[/<attribute>])`

Returns a space-separated list of semaphore queue process ids waiting on the given `<object>` and semaphore `<attribute>`. If `<attribute>` is not given, pids for all semaphores on the object are returned.

See also: [@ps], [@wait], [lpids()], [pidinfo()], [SEMAPHORES]

# GRAB()

# REGRAB()

# REGRABI()

`grab(<list>, <pattern>[, <delimiter>])`
`regrab(<list>, <regexp>[, <delimiter>])`
`regrabi(<list>, <regexp>[, <delimiter>])`

These functions return the first word in `<list>` which matches the pattern. For grab(), `<pattern>` is a wildcard pattern ('help wildcards'). For regrab() and regrabi(), the pattern is a regular expression. regrabi() is case-insensitive. `<delimiter>` defaults to a space.

Basically, this is a much more efficient way to do:
`elements(<list>, match(<list>, <pattern>[, <delimiter>])[, <delimiter>])`
or the regular expression variation thereof.

See also: [graball()], [match()], [extract()], [elements()], [regmatch()]

# GRABALL()

# REGRABALL()

# REGRABALLI()

`graball(<list>, <pattern>[, <delim>[, <osep>]])`
`regraball(<list>, <regexp>[, <delim>[, <osep>]])`
`regraballi(<list>, <regexp>[, <delim>[, <osep>]])`

These functions work identically to the grab() and regrab()/regrabi() functions, except they return all matches, not just the first: They return all words in the `<list>` which match `<pattern>`. If none match, an empty string is returned. `<delim>` defaults to a space, and `<osep>` defaults to `<delim>`.

Examples:

```
> say graball(This is a test of a test,test)
You say "test test"
```

```
> say graball(This|is|testing|a|test,tes*,|)
You say "testing|test"
```

```
> say regraball(This is testing a test,s$)
You say "This is"
```

See also: [match()], [matchall()], [grab()], [regmatch()]

# GREP()

# REGREP()

# WILDGREP()

# GREPI()

# REGREPI()

# WILDGREPI()

# PGREP()

`grep(<object>, <attrs>, <substring>)`
`wildgrep(<object>, <attrs>, <pattern>)`
`regrep(<object>, <attrs>, <regexp>)`
`grepi(<object>, <attrs>, <substring>)`
`regrepi(<object>, <attrs>, <regexp>)`
`wildgrepi(<object>, <attrs>, <pattern>)`
`pgrep(<object>, <attrs>, <substring>)`

These functions return a list of attributes on `<object>` containing `<substring>`, matching the wildcard `<pattern>`, or matching the regular expression `<regexp>`. `<attrs>` is a wildcard pattern for attribute names to search.

Parsing _does_ occur before this function is invoked. Therefore, "special" characters will need to be escaped out.

grep()/wildgrep()/regrep() are case-sensitive.
grepi()/wildgrepi()/regrepi() are case-insensitive.

pgrep() works like grep(), but also checks attributes inherited from parents.

See also: [@grep], [lattr()], [WILDCARDS]

# GT()

`gt(<number1>, <number2>[, ... , <numberN>])`

Takes two or more numbers, and returns 1 if and only if each number is greater than the number after it, and 0 otherwise.

See also: [gte()], [lt()], [lte()], [eq()], [neq()], [lmath()]

# GTE()

`gte(<number1>, <number2>[, ... , <numberN>])`

Takes two or more numbers, and returns 1 if and only if each number is greater than or equal to the number after it, and 0 otherwise.

See also: [gt()], [lt()], [lte()], [eq()], [neq()], [lmath()]

# HASATTR()

# HASATTRP()

# HASATTRVAL()

# HASATTRPVAL()

`hasattr(<object>, <attribute>)`
`hasattrp(<object>, <attribute>)`
`hasattrval(<object>, <attribute>)`
`hasattrpval(<object>, <attribute>)`

The hasattr*() functions check to see if `<object>` has a given attribute. They return #-1 if the object does not exist or the attribute can't be examined by the player. Otherwise, they return 1 if the attribute is present and 0 if it is not.

hasattr() checks to see if `<attribute>` exists on `<object>` only.

hasattrp() also checks for `<attribute>` on `<object>`'s parent/ancestor.

hasattrval() only returns 1 if `<attribute>` exists and is non-empty. An "empty" attr is one containing a null value (if the empty_attrs config option is on), or one containing a single space (if the option is off).

hasattrpval() is like hasattrval() but also checks parents.

All four functions will also work with one argument in the form of `<object>/<attribute>`.

See also: [visible()], [lattr()]

# HASFLAG()

`hasflag(<object>[/<attrib>], <flag>)`

With no `<attrib>`, hasflag() returns 1 if `<object>` has the `<flag>` flag set. If `<attrib>` is specified, the attribute is checked for the `<flag>` attribute flag instead. If the flag is not present, 0 is returned.

hasflag() will accept a full flag name ("Wizard") or a flag letter ("W"). You can check the flags of any object, whether you control them or not.

Example:

```
> think hasflag(me, wizard)
1
```

See also: [orlflags()], [andlflags()], [orflags()], [andflags()], [flags()], [lflags()], [attribute flags], [@flag], [haspower()], [hastype()]

# HASPOWER()

`haspower(<object>, <power>)`

Returns 1 if `<object>` has the named power, and 0 if it does not.

You can check the powers of any object, whether you control it or not.

See also: [@power], [powers list], [hasflag()]

# HASTYPE()

`hastype(<object>, <type list>)`

Returns 1 if `<object>` belongs to one of the types given in `<type list>`, and 0 otherwise. Valid types are PLAYER, THING, ROOM, EXIT and GARBAGE.

Example:

```
> @create Test Object
> think hastype(test object, PLAYER EXIT)
0
```

```
> think hastype(test object, PLAYER THING)
1
```

See also: [TYPES], [type()]

# HIDDEN()

`hidden(<player|descriptor>)`

If you can see hidden players, this function returns 1 if `<player>` (or the player connected to `<descriptor>`) is hidden, and 0 otherwise. If you cannot see hidden players, hidden() returns #-1.

See also: [@hide]

# HOME()

`home(<object>)`

Returns the object's 'home', where it is @link'd to. This is the home for a player or thing, the drop-to of a room, or source of an exit.

See also: [@link]

# HOST()

# HOSTNAME()

`host(<player|descriptor>)`

Returns the hostname a player is connected from, as shown on the wizard WHO. This may be more reliable that `get(<player>/lastsite)` if the player has multple connections from different locations, and the function is called with a descriptor argument.

The caller can use the function on himself, but using on any other player requires privileged power such as Wizard, Royalty or SEE_ALL.

hostname() is an alias for host().

See also: [Connection Functions], [ipaddr()], [ports()], [lports()]

# IDLE()

# IDLESECS()

`idle(<player|descriptor>)`

This function returns the number of seconds a player has been idle, much as WHO does. `<player name>` must be the full name of a player, or a player's dbref. You can also specify a `<descriptor>`, useful if a player is connected multiple times, or for connections which are still at the login screen. Players who are not connected have an idle time of "-1", as do dark wizards, when idle() is used on them by a non-priv'ed player.

idlesecs() is an alias for idle().

See also: [Connection Functions], [conn()]

# IF()

# IFELSE()

`if(<condition>, <true expression>[, <false expression>])`
`ifelse(<condition>, <true expression>, <false expression>)`

These functions evaluate the `<condition>` and return `<true expression>` if the `<condition>` is true, or `<false expression>` (if provided) if the `<condition>` is false. Only the returned `<expression>` is evaluated.

See also: [BOOLEAN VALUES], [switch()], [@if], [@break], [cond()]

# INAME()

`iname(<object>)`

iname() returns the name of `<object>`, as it would appear if you were inside it. It is identical to name() except that if the object has a NAMEFORMAT or NAMEACCENT attribute, it is used.

You must be see_all, control `<object>`, or be inside it to use this function.

See also: [@nameformat], [@nameaccent], [name()], [fullname()], [accname()]

# INC()

`inc(<integer>)`
`inc(<string-ending-in-integer>)`

inc() returns the integer plus 1. If given a string that ends in an integer, it increments only the final integer portion. That is:

Examples:

```
> think inc(3)
4
```

```
> think inc(hi3)
hi4
```

```
> think inc(1.3.3)
1.3.4
```

Note especially the last example, which will trip you up if you use floating point numbers with inc() and expect it to work like add().

If the null_eq_zero @config option is on, using inc() on a string which does not end in an integer will return `<string>1`. When null_eq_zero is turned off, it will return an error.

See also: [dec()], [add()], [sub()]

# INDEX()

`index(<list>, <character>, <first>, <length>)`

This function is similar to extract(), except that it requires four arguments, while extract() uses defaults for its arguments if they aren't given. The function returns `<length>` items starting from the `<first>` position. Trailing spaces are trimmed.

Examples:

```
> say index(Cup of Tea | Mug of Beer | Glass of Wine, |, 2, 1)
You say, "Mug of Beer"
```

```
> say index(%rtoy boat^%rblue tribble^%rcute doll^%rred ball,^,2,2)
You say, "
blue tribble^
cute doll"
```

See also: [extract()], [elements()], [grab()]

# INSERT()

# LINSERT()

`linsert(<list>, <position>, <new item>[, <delim>])`

If `<position>` is a positive integer, this inserts `<new item>` BEFORE the item at `<position>` from the left in `<list>`. That means that `<new item>` then becomes the `<position>`th element of `<list>`.

If `<position>` is a negative integer, this inserts `<new item>` AFTER the item at the absolute value of `<position>` from the RIGHT in `<list>`. This is the same as reversing the list before inserting `<new item>`, and then reversing it again into correct order. For example, when `<position>` is -1, `<new item>` will be the last in the list; when `<position>` is -2, `<new item>` will be the second item from the right, and so on.

If a `<delim>` is not given, a space is assumed. Null items are counted when determining position, as in 'items()'.

Examples:

```
> say linsert(This is a string,4,test)
You say, "This is a test string"
```

```
> say linsert(one|three|four,2,two,|)
You say, "one|two|three|four"
```

```
> say linsert(meep bleep gleep,-3,GOOP)
You say, "meep GOOP bleep gleep"
```

insert() is an alias for linsert(), for backwards compatability.

See also: [lreplace()], [ldelete()], [strinsert()]

# ISDAYLIGHT()

`isdaylight([<secs>[, <timezone>]])`

Returns 1 if it's daylight savings in the specified timezone at the given time. Defaults to the host server's time zone and current time if not specified.

See also: [timezones], [secs()]

# ISDBREF()

# ISOBJID()

`isdbref(<string>)`
`isobjid(<string>)`

isobjid() returns 1 if `<string>` is the object id of an existing object. If `<string>` is not a full objid, or is the objid of a garbage object, it returns 0.

isdbref() functions the same, but will also return 1 if `<string>` is the dbref of an existing (or garbage) object.

Examples:

```
> @stats
100 objects = 20 rooms, 20 exits, 20 things, 20 players, 20 garbage.
The next object to be created will be #33.
```

```
> think isdbref(#33)
1
```

```
> think isobjid(#33:1234567890)
0
```

```
> think csecs(#1)
1324654503
```

```
> think isdbref(#1)
1
```

```
> think isobjid(#)
0
```

```
> think isdbref(#1:1324654503)
1
```

```
> think isobjid(#1:1324654503)
1
```

```
> think isobjid(#1:9876543210)
0
```

See also: [DBREFS], [OBJECT IDS], [num()], [objid()]

# ISINT()

`isint(<string>)`

Returns 1 if its argument is an integer, and 0 otherwise. Integers can begin with a '+' or '-' sign, but the rest of the string must be digits.

See also: [isnum()]

# ISNUM()

`isnum(<string>)`

This function returns 1 if `<string>` is a number, and 0 if it is not. Numbers can begin with a '-' sign (for negatives), but the rest of the characters in the string must be digits, and an optional decimal point.

See also: [isint()]

# ISREGEXP()

`isregexp(<string>)`

This function returns 1 if `<string>` is a valid regular expression, and 0 if it is not.

See also: [REGEXP]

# ISWORD()

`isword(<string>)`

This function returns 1 if every character in `<string>` is a letter, or 0, if any character isn't a letter. Case does not matter.

# ITEMS()

`items(<list>, <delim>)`

items() counts the number of items in a list using an arbitrary `<delim>`. Null items are counted, so:

`items(X|X,|)` => 2 (2 X items)
`items(X||X,|)` => 3 (2 X items and 1 null item)
`items(X X,%b)` => 2 (2 X items)
`items(X%b%bX,%b)` => 3 (2 X items and 1 null item)
`items(,|)` => 1 (a single null item)

Another way to think about this is that items() counts the number of times `<delim>` appears in `<list>`, and adds 1.

See also: [words()]

# ITEMIZE()

# ELIST()

`itemize(<list>[, <delim>[, <conjunction>[, <punctuation>]]])`
`elist(<list>[, <conjunction>[, <delim>[, <osep>[, <punctuation>]]]])`

These functions take the elements of `<list>` (separated by `<delim>` or a space by default), and:

*   If there's just one, return it.
*   If there's two, return `<e1> <conjunction> <e2>`
*   If there's more than two, return `<e1><punc> <e2><punc> ... <conj> <en>`

elist() uses `<osep>` after `<punc>`/`<conj>` instead of a space.
The default `<conjunction>` is "and", default punctuation is ",".

Examples:

```
> say itemize(eggs) * [itemize(eggs bacon)]
You say, "eggs * eggs and bacon"
```

```
> say itemize(eggs bacon spam)
You say, "eggs, bacon, and spam"
```

```
> say itemize(eggs bacon spam, ,&,;)
You say, "eggs; bacon; & spam"
```

# ITER()

# PARSE()

`iter(<list>, <pattern>[, <delimiter>[, <output separator>]])`

For each word in `<list>`, iter() evaluates `<pattern>` once, and returns a list of the results of those evaluations. Words in `<list>` are separated by `<delimiter>`, if given, and spaces if not. Words in the resulting list are separated by the given `<ouput separator>`, or a space if no output separator is given.

Prior to each evaluation, every occurrence of the string "##" in `<pattern>` is replaced with the current word from `<list>`. However, because this replacement occurs before evaluation, it cannot be used well in nested iter()s, and should not be used on user input or untrusted `<list>`s, as the word will be evaluated. Instead, you can use the %iX substitution, or the itext() function. The substitution '%iL' refers to the outermost iter of the current expression, and is intended to replace ##.

The string "#@" will be replaced with the position of the current word in `<list>`. Like "##", the replacement occurs before substitution. Use the inum() function for nested iter()s.

If you nest iter()s, ## and #@ refer to the first/outermost iter(). The ilev() function can be used to get the current iter() nesting level.

parse() is an alias for iter().

See 'help iter2' for examples.

See also: [itext()], [inum()], [ilev()], [ibreak()], [map()], [@dolist]

# ITER2

Examples:

```
> say iter(This is a test string., strlen(%i0))
You say, "4 2 1 4 7"
```

```
> say iter(lnum(5), mul(add(%i0,#@),2))
You say, "2 6 10 14 18"
```

```
> say iter(lexits(here), name(%i0) (owned by [name(owner(%i0))]))
You say, "South (owned by Claudia) North (owned by Roy)"
```

```
> &STRLEN_FN me=strlen(%0)
> say iter(This is a test string., u(STRLEN_FN, %i0))
You say, "4 2 1 4 7"
```

Since this example just evaluates another attribute for each element of the list, it can be done more efficiently using map():

```
> say map(strlen_fun, This is a test string.)
```

```
> say iter(lnum(3), %i0, ,%r)
You say, "0
1
2"
```

An example of why using ## instead of %i0 can be insecure, and lead to unintended evaluation:

```
> say iter((1\,1),add##)
You say, "2"
```

```
> say iter((1\,1),add%i0)
You say, "add(1,1)"
```

# IBREAK()

`ibreak([<level>])`

The ibreak() function stops an iter() from running at the end of the current loop. When used in nested iter()s, you can give a `<level>` to specify how many iter()s to break from. iter() will stop evaluating at the end of the current loop, and NOT immediately after ibreak() is called.

Examples:

```
> say iter(1 2 3 4 5,switch(%i0,3,ibreak())Test %i0!)
You say, "Test 1! Test 2! Test 3!"
```

```
> say iter(1 2 3 4 5,switch(%i0,3,ibreak(),Test %i0!))
You say, "Test 1! Test 2! "
```

```
> say iter(a b c, iter(1 2 3, switch(%i0%i1, 2c, ibreak(2), %$0)))
You say, "1a 2a 3a 1b 2b 3b 1c "
```

See also: [iter()], [itext()], [inum()], [ilev()]

# ILEV()

# ITEXT()

# INUM()

# %i

# %i0

`ilev()`
`itext(<n>)`
`%i<n>`
`inum(<n>)`

These functions return the equivilent of ## (itext) or #@ (inum) for iter() and @dolist, where an `<n>`=0 returns to the current iter or @dolist, `<n>`=1 refers to the iter()/@dolist which the current iter() or @dolist is nested in, etc. An `<n>` of "L" can be used to refer to the outermost iter()/@dolist. %i`<n>` is an alias for `itext(<n>)`, where `<n>` can be from 0 to 9 (or "L").

ilev() returns the current nesting depth, or -1 when used outside an iter() or @dolist. Thus, `itext(ilev())` will return the outermost ##, equivilent to %iL.

See 'help itext2' for examples.

See also: [iter()], [ibreak()], [@dolist]

# ITEXT2

Examples:

```
> say iter(red blue green, iter(fish shoe, #@:##))
You say, "1:red 1:red 2:blue 2:blue 3:green 3:green"
```

```
> say iter(red blue green, iter(fish shoe, inum(ilev()):[itext(1)]))
You say, "1:red 1:red 2:blue 2:blue 3:green 3:green"
```

```
> say iter(red blue green,iter(fish shoe, inum(0):[itext(0)]))
You say, "1:fish 2:shoe 1:fish 2:shoe 1:fish 2:shoe"
```

```
> say iter(red blue green,iter(fish shoe, %i1:%i0))
You say, "red:fish red:shoe blue:fish blue:shoe green:fish green:shoe"
```

```
> @dolist red blue green=say iter(fish shoe, %i1:%i0)
You say, "red:fish red:shoe"
You say, "blue:fish blue:shoe"
You say, "green:fish green:shoe"
```

See also: [iter()], [@dolist]

# IPADDR()

`ipaddr(<player|descriptor>)`

Returns the IP address of the connected player or descriptor. This may be more reliable that `get(<player>/lastip)` if the player has multple connections from different locations, and the function is called with a descriptor argument.

The caller can use the function on himself, but using on any other player requires privileged power such as Wizard, Royalty or SEE_ALL.

See also: [Connection Functions], [hostname()], [ports()], [lports()]

# LAST()

`last(<list>[, <delimiter>])`

Returns the last element of a list. Elements in `<list>` are separated by `<delimiter>`, if given, or by a space if not.

See also: [first()], [rest()], [before()], [after()]

# LATTR()

# LATTRP()

# REGLATTR()

# REGLATTRP()

`lattr(<object>[/<attribute pattern>][, <output separator>])`
`lattrp(<object>[/<attribute pattern>][, <output separator>])`
`reglattr(<object>[/<regexp>][, <output separator>])`
`reglattrp(<object>[/<regexp>][, <output separator>])`

lattr() returns a list of all the attributes on `<object>` which you can see, and which match the wildcard `<attribute pattern>`. If no `<attribute pattern>` is given, it defaults to "*". Note that this does not include branches in attribute trees; you must use the "**" wildcard to include those.

The resulting list will be separated by `<output separator>`, or a space if no separator is given.

reglattr() returns attributes whose names match the regexp `<regexp>`. The match is not case-sensitive (as attribute names are always upper-case), and the "`" branch separator has no special meaning in the pattern.

lattrp() and reglattrp() also include attributes inherited from parents.

When returning large numbers of attributes, the results may be truncated due to buffer limits. In these cases, you can use nattr() and xattr() to retrieve the results in smaller pieces.

See also: [nattr()], [xattr()], [hasattr()], [examine], [grep()], [WILDCARDS]

# NATTR()

# NATTRP()

# ATTRCNT()

# ATTRPCNT()

# REGNATTR()

# REGNATTRP()

`nattr(<object>[/<attribute pattern>])`
`nattrp(<object>[/<attribute pattern>])`
`regnattr(<object>[/<regexp>])`
`regnattrp(<object>[/<regexp>])`

nattr() returns the number of attributes on `<object>` that you can see which match the given `<attribute pattern>`. It is considerably faster than words(lattr()) and doesn't suffer from buffer length constraints. It's designed primarily for statistical purposes. `<attribute pattern>` defaults to "*", which does not include branches in attribute trees; use "**" if you need to count attribute trees.

regnattr() matches attribute names against the regular expression `<regexp>`.

nattrp() and regnattrp() also count matching attributes on the parent.

attrcnt() and attrpcnt() are aliases for nattr() and nattrp() respectively.

See also: [lattr()], [hasattr()], [xattr()], [WILDCARDS]

# LCON()

`lcon(<object>[, <type>])`

Returns a list of the dbrefs of objects which are located in `<object>`.

You can get the complete contents of any object you may examine, regardless of whether or not objects are dark. You can get the partial contents (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the contents of anything else, regardless of whether or not you have objects in it.

When used on exits, this function returns #-1.

For compatability with other codebases, a `<type>` can be given to limit the results. Valid `<type>`s are:

*   **player**: equivalent to `lplayers(<object>)`
*   **connect**: equivalent to `lvplayers(<object>)`
*   **thing (or object)**: equivalent to `lthings(<object>)`
*   **listen**: return only listening objects
*   **puppet**: return only THINGs set PUPPET

See also: [lexits()], [lplayers()], [lthings()], [con()], [next()], [lvcon()]

# LCSTR()

# LCSTR2()

`lcstr(<string>)`
`lcstr2(<string>)`

Returns `<string>` with all letters converted to lowercase.

If the MUSH is compiled with ICU Unicode support, lcstr2() does the same thing except the returned string might be a different length, and ansi colors and other markup are stripped.

Example:

```
> say lcstr(Foo BAR bAz)
You say, "foo bar baz"
```

See also: [capstr()], [ucstr()]

# LDELETE()

`ldelete(<list>, <position(s)>[, <delimiter>[, <osep>]])`

This function deletes the element(s) of `<list>` at the given `<position(s)>`. Elements of `<list>` are separated by `<delimiter>`, which defaults to a space. Null items are counted, as in 'items()'. Elements of `<position(s)>` must be numeric, and are always separated by a space, not by `<delimiter>`. Elements of the returned list are separated by `<osep>`, which defaults to the `<delimiter>`.

If a `<position>` is negative, ldelete() counts backwards from the end of the list; a position of -1 deletes the last element, -2 the element before last, and so on.

All position calculations are performed on the original list. That is, `ldelete(a b c, -1 -1)` will return "a b", not "a", and `ldelete(a b c, -1 -2)` returns "a", not "b".

Examples:

```
> say ldelete(This is a long test string,4)
You say, "This is a test string"
```

```
> say ldelete(lemon|orange|pear|apple,2 3,|)
You say, "lemon|apple"
```

```
> say ldelete(foo bar baz boing,3,,%b~%b)
You say, "foo ~ bar ~ boing"
```

See also: [strdelete()], [remove()], [linsert()]

# LEFT()

`left(<string>, <length>)`

Returns the first `<length>` characters from `<string>`.

See also: [right()], [mid()], [ljust()]

# NSLEMIT()

# LEMIT()

`lemit(<message>)`
`nslemit(<message>)`

lemit() emits a message in the caller's outermost room, as per @lemit.

nslemit() like @nslemit.

See also: [@lemit], [remit()]

# LETQ()

`letq([<reg1>, <value1>[, ... , <regN>, <valueN>], <expr>)`

letq() saves the current values of the given q-`<reg>`isters, sets them to new `<value>`s, evaluates `<expr>` and then restores the saved registers. It does not restore registers that are not listed. None of the values can see the updated contents of the registers -- they are only visible to `<expr>`.

It returns the result of `<expr>`.

Examples:

```
> think setr(A, 1):[letq(A, 2, %qA)]:%qA
1:2:1
```

```
> think setr(A, 1)[setr(B,1)]:[letq(A, 2, %qA[setr(B,2)])]:%qA%qB
11:22:12
```

See also: [setq()], [setr()], [unsetq()], [listq()], [localize()], [ulocal()], [r()]

# LEXITS()

`lexits(<room>)`

Returns a list of the dbrefs of exits in a room.

You can get the complete exit list of any room you may examine, regardless of whether or not exits are dark. You can get the partial exit list (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the exit list of anything else, regardless of whether or not you have objects in it.

See also: [lcon()], [exit()], [next()], [lvexits()]

# LJUST()

`ljust(<string>, <length>[, <fill>[, <truncate?>]])`

This function returns `<string>`, padded with the string `<fill>` until it's `<length>` characters long. `<fill>` can be more than one character in length, and defaults to a single space.

If `<string>` is longer than `<length>`, it will be returned unaltered, unless `<truncate?>` is true, in which case only the first `<length>` characters of `<string>` are returned.

Examples:

```
> say ljust(foo,6)
You say, "foo   "
```

```
> say %r0[ljust(foo,6,-)]7%r01234567
You say, "
0foo---7
01234567"
```

```
> say ljust(foo,12,=+)
You say, "foo=+=+=+=+="
```

```
> say ljust(This is too long,9,,1)
You say, "This is t"
```

See also: [align()], [center()], [rjust()], [left()]

# LINK()

`link(<object>, <destination>[, <preserve>])`

This function links `<object>` to `<destination>`. While normally used on exits, it has all of the other capabilities of @link as well. It returns #-1 or 0 on failure, 1 on success. If the optional third argument is true, acts like @link/preserve.

See also: [@link], [open()]

# LIST()

`list(<option>[, <type>])`

This is the function-equivilent of the @list command, and lists some useful information about the MUSH. `<option>` can be one of:

*   **motd**: Returns the current @motd
*   **wizmotd**: Returns the current @motd/wizard. Wiz/Roy only.
*   **downmotd**: Returns the current @motd/down. Wiz/Roy only.
*   **fullmotd**: Returns the current @motd/full. Wiz/Roy only.
*   **functions**: Returns a list of all built-in functions and @functions.
*   **commands**: Returns a list of all built-in commands and @commands.
*   **attribs**: Returns all standard attributes.
*   **locks**: Returns the built-in lock types. Similar to llocks().
*   **flags**: Returns all flags. Similar to lflags().
*   **powers**: Returns all @powers.

"commands"/"functions" return both built-in and local commands/functions by default. You can specify a `<type>` of either "builtin", "local" or "all" to limit this if you wish.

See also: [@list], [flags()], [lflags()], [config()], [functions()], [@listmotd], [@motd], [llocks()]

# LIT()

`lit(<string>)`

This function returns `<string>` literally - without even squishing spaces, and without evaluating *anything*. This can be useful for writing ASCII maps with spaces or whatever.

It can be a bit tricky to get a literal string with spaces into an attrib, however, since spaces are usually squished in setting an attribute. This example illustrates how to make it work:

```
> @va me=$test: think {[lit(near       far)]}
Set.
```

```
> ex me/va
VA [#1]: $test: think {[lit(near       far)]}
```

```
> test
near       far
```

Leaving out the {}'s will not work in the above.

See also: [decompose()]

# LMATH()

`lmath(<op>, <list>[, <delim>])`

This function performs generic math operations on `<list>`, returning the result. Each element of the list is treated as one argument to an operation, so that `lmath(<op>, 1 2 3)` is equivalent to `<op>(1, 2, 3)`. Using @function, one can easily write ladd, lsub, etc as per TinyMUSH.

Supported `<op>`'s are:
add and band bor bxor dist2d dist3d div eq fdiv gt gte lt lte max mean median min modulo mul nand neq nor or remainder stddev sub xor

Examples:

```
> think lmath(add, 1|2|3, |)
6
```

```
> think lmath(max, 1 2 3)
3
```

```
> &FUN_FACTORIAL me=lmath(mul,lnum(1,%0))
> think u(fun_factorial,5)
120
```

# LN()

`ln(<number>)`

Returns the natural log of `<number>`. This is equivilent to `log(<number>, e)`.

See also: [log()]

# LNUM()

`lnum(<number>)`
`lnum(<start number>, <end number>[, <output separator>[, <step>]])`

With one argument, lnum returns a list of numbers, from 0 to `<number - 1>`. For example, `lnum(4)` returns the list "0 1 2 3". This is useful for creating loops.

With two arguments, the numbers range from the first to the second argument. For example, `lnum(1,4)` => 1 2 3 4

With three arguments, the output is separated by the separator given in the third argument. `lnum(1,4,|)` => 1|2|3|4

A fourth argument dictates the step. By default, the step is 1.
`lnum(1,10,%b,2)` -> 1 3 5 7 9
`lnum(0,10,%b,2)` -> 0 2 4 6 8 10

# LOC()

`loc(<object>)`

For things and players, loc() returns the dbref of the object which contains `<object>`. For rooms, it returns the drop-to, if one is set, or #-1 otherwise. For exits, it returns the destination (the source is an exits home()). This will be #-1 for unlinked exits, #-2 for variable exits, and #-3 for exits @linked to "home".

You must be able to examine `<object>`, or be near it, for this function to work. A special case exists when `<object>` is a player: As long as `<object>` is not set UNFINDABLE, and you are allowed to use the @whereis command, you can get `<object>`'s location.

You can also get the location of the enactor using the %L substitution, whether you are near to/can examine it or not.

See also: [locate()], [rloc()], [home()], [where()], [rnum()], [room()], [@link], [UNFINDABLE], [@whereis]

# LOCALIZE()

`localize(<code>)`

localize() saves the q-registers, evaluates its argument, and restores the registers afterwards. It has the same effect as ulocal(), but doesn't require setting the code into an attribute.

Examples:

```
> say setr(0, Outside)-[setr(0, Inside)]-%q0
You say, "Outside-Inside-Inside"
```

```
> &INSIDE me=setr(0,Inside)
> say setr(0, Outside)-[ulocal(INSIDE)]-%q0
You say, "Outside-Inside-Outside"
```

```
> say setr(0, Outside)-[localize(setr(0, Inside))]-%q0
You say, "Outside-Inside-Outside"
```

See also: [letq()], [setq()], [setr()], [r()], [ulocal()], [uldefault()]

# LOCATE()

`locate(<looker>, <name>, <parameters>)`

This function attempts to find an object called `<name>`, relative to the object `<looker>`. It's similar to the num() function, but you can be more specific about which type of object to find, and where to look for it. When attempting to match objects near to `<looker>` (anything but absolute, player name or "me" matches), you must control `<looker>`, have the See_All power or be nearby.

`<parameters>` is a string of characters which control the type of the object to find, and where (relative to `<looker>`) to look for it.

You can control the preferred types of the match with:

*   **N**: No type (this is the default)
*   **E**: Exits
*   **L**: Prefer an object whose Basic @lock `<looker>` passes
*   **P**: Players
*   **R**: Rooms
*   **T**: Things
*   **F**: Return #-1 if what's found is of a different type than the preferred one.
*   **X**: Never return #-2. Use the last dbref found if the match is ambiguous.

If type(s) are given, locate() will attempt to find an object with one of the given types first. If none are found, it will attempt to find any type of object, unless 'F' is specified, in which case it will return #-1.

Continued in [help locate2].

# LOCATE2

You can control where to look with:

*   **a**: Absolute match (match `<name>` against any dbref)
*   **c**: Exits in the room `<looker>`
*   **e**: Exits in `<looker>`'s location
*   **h**: If `<name>` is "here", return `<looker>`'s location
*   **i**: Match `<name>` against the names of objects in `<looker>`'s inventory
*   **l**: Match `<name>` against the name of `<looker>`'s location
*   **m**: If `<name>` is "me", return `<looker>`'s dbref
*   **n**: Match `<name>` against the names of objects in `<looker>`'s location
*   **p**: If `<name>` begins with a *, match the rest against player names
*   **z**: English-style matching (my 2nd book) of `<name>` (see 'help matching')
*   *****: All of the above (try a complete match). Default when no match parameters are given.
*   **y**: Match `<name>` against player names whether it begins with a * or not
*   **x**: Only match objects with the exact name `<name>`, no partial matches
*   **s**: Only match objects which `<looker>` controls. You must control `<looker>` or have the See_All power.

Just string all the parameters together. Spaces are ignored, so you can use spaces between paramaters for clarity if you wish.

See 'help locate3' for examples.

See also: [num()], [rnum()], [pmatch()], [room()], [where()], [rloc()], [findable()]

# LOCATE3

Examples:
Find the dbref of the player whose name matches %0, or %#'s dbref if %0 is "me".

```
> think locate(%#, %0, PFym)
```

'PF' matches objects of type 'player' and nothing else, 'm' checks for the string "me", and 'y' matches the names of players.

Find the dbref of an object near %# called %0, including %# himself and his location. Prefer players or things, but accept rooms or exits if no players or things are found.

```
> think locate(%#, %0, PThmlni)
```

This prefers 'P'layers or 'T'hings, and compares %0 against the strings "here" and "me", and the names of %#'s location, his neighbours, and his inventory.

# LOCK()

`lock(<object>[/<locktype>][, <new value>])`

lock() returns the text string equivalent of the @lock on `<object>`. `<locktype>` can be any valid switch for @lock ("Enter", "user:foo", etc) and defaults to "Basic". You must be able to examine the lock.

If a `<new value>` is given, lock() attempts to change the lock as @lock would first. You must control the object.

See also: [@lock], [locktypes], [elock()], [lockflags()], [llockflags()], [lset()], [llocks()], [lockowner()], [lockfilter()]

# LLOCKS()

# LOCKS()

`llocks([<object>])`
`locks(<object>)`

llocks() and locks() both list @locks set on `<object>`, including user-defined locks (prefixed with USER:)

If no object is given, llocks() returns all the predefined lock types available.

Example:

```
> @lock me==me
> @lock/use me==me
> @lock/user:itsme me==me
> th llocks(me)
Basic USER:ITSME Use
```

See also: [lock()], [lset()], [lockflags()], [llockflags()], [lockowner()]

# LOCKFILTER()

`lockfilter(<key>, <dbrefs>[, <delim>])`

lockfilter() goes through `<dbrefs>` and tests them all against the lock `<key>`, returning a list of all dbrefs that pass the `<key>`.

`<key>` is evaluated from the caller's perspective.

This is equivilent to `filter(#lambda/testlock(<key>, %%0), <dbrefs>)` but much more efficient, as the lock `<key>` is only parsed/compiled once.

`<delim>` defaults to a space, and is the delimiter of `<dbrefs>` and the list returned by lockfilter().

Examples:
Get all male players with a name starting with 'W'.

```
> think iter(lockfilter(NAME^W*&SEX:M*,lwho()),name(%i0))
Walker WalkerBot Wilco
```

List all wizroys online:

```
> think iter(lockfilter(FLAG^WIZARD|FLAG^ROYALTY,lwho()),name(%i0))
Sketch Viila Tanaku Raevnos Zebranky Cheetah Walker
```

List all players with an IC age > 20.

```
> think lockfilter(age:>20,lwho())
#123 #456 #789
```

Note: You can escape the first character of `<key>` using double back slashes, for example, if you are checking for an attribute named +FOO to have the value of BAR on all connected players:

```
> think map(#apply/name,lockfilter(\\+FOO:BAR,lwho()))
Mike Walker Qon
```

See also: [@lock], [lock()], [elock()], [lockkeys], [filter()], [testlock()]

# LOCKFLAGS()

`lockflags(<object>[/<locktype>])`
`lockflags()`

If an `<object>` is given, lockflags() returns a string consisting of the one-character abbreviations for all the lock flags on `<object>`'s `<locktype>` lock, or Basic lock if no locktype is given. You must be able to examine the lock.

Given no arguments, this function returns a string consisting of all the flag letters the server knows.

See also: [llockflags()], [lset()], [lock()], [llocks()], [lockowner()]

# LLOCKFLAGS()

`llockflags(<object>[/<locktype>])`
`llockflags()`

If an `<object>` is given, llockflags() returns a space-separated list of the lock flags on `<object>`'s `<locktype>` lock, or Basic lock if no locktype is given. You must be able to examine the lock.

Given no arguments, this function returns a space-separated list of all the names of all lock flags known to the server.

See also: [lockflags()], [lset()], [lock()], [llocks()], [lockowner()]

# LOCKOWNER()

`lockowner(<object>[/<locktype>])`

This function returns the dbref of the player who owns the `<locktype>` lock on `<object>`, or the Basic lock if no `<locktype>` is given. You must be able to examine the lock to use this function.

See also: [lockflags()], [llockflags()], [lset()], [lock()], [llocks()]

# LSET()

`lset(<object>/<locktype>,[!]<flag>)`

This functions sets or clears flags on locks.

See 'help @lset' for more information on what flags are available.

See also: [lockflags()], [llockflags()], [lock()], [lockowner()]

# LOG()

`log(<number>[, <base>])`

Returns the logarithm (base 10, or the given base) of `<number>`. `<base>` can be a floating-point number, or 'e' for the natural logarithm.

See also: [ln()]

# LPARENT()

`lparent(<object>)`

This function returns a list consisting of `<object>`'s dbref (as per num()), the dbref of its parent, grandparent, greatgrandparent, etc. The list will not, however, show parents of objects which the player is not privileged to examine. Ancestor objects are not included.

See also: [parent()], [children()], [PARENTS], [ANCESTORS]

# LPLAYERS()

`lplayers(<object>)`

This function returns the dbrefs of all players, connected or not, in `<object>`. DARK wizards aren't listed to mortals or those without the see_all power. You must be in `<object>` or control it to use this function.

See also: [lvplayers()], [lcon()], [lthings()]

# LTHINGS()

`lthings(<object>)`

This function returns the dbrefs of all things, dark or not, in `<object>`. You must be in `<object>` or control it to use this function.

See also: [lvthings()], [lcon()]

# LPOS()

`lpos(<string>, <character>)`

This function returns a list of the positions where `<character>` appears in `<string>`, with the first character of the string being 0. Note that this differs from the pos() function, but is consistent with other string functions like mid() and strdelete().

If `<character>` is a null argument, space is used. If `<character>` is not found anywhere in `<string>`, an empty list is returned.

Example:

```
> say lpos(a-bc-def-g, -)
You say, "1 4 8"
```

See also: [pos()], [member()], [match()], [wordpos()]

# LSEARCH()

# NLSEARCH()

# SEARCH()

# NSEARCH()

# LSEARCHR()

# CHILDREN()

# NCHILDREN()

`lsearch(<player>[, ... , <classN>, <restrictionN>])`
`nlsearch(<player>[, ... , <classN>, <restrictionN>])`
`lsearchr(<player>[, ... , <classN>, <restrictionN>])`
`children(<object>)`
`nchildren(<object>)`

This function is similar to the @search command, except it returns just a list of dbref numbers. The function must have at least three arguments. You can specify "all" or `<player>` for the `<player>` field; for mortals, only objects they can examine are included. If you do not want to restrict something, use "none" for `<class>` and `<restriction>`.

The possible `<class>`es and `<restriction>`s are the same as those accepted by @search. lsearch() can accept multiple class/restriction pairs, and applies them in a boolean "AND" fashion, returning only dbrefs that fulfill all restrictions. See 'help @search' for information about them.

children() is exactly the same as `lsearch([me|all], parent, <object>)`, using "all" for See_All/Search players and "me" for others.

`nlsearch(...)` and `nchildren(...)` return the count of results that would be returned by lsearch() or children() with the same args.

Continued in [help lsearch2].

# LSEARCH2

# SEARCH2

If `<class>` is one of the eval classes (EVAL, EEXITS, EROOMS, ETHINGS or EPLAYERS), note that any brackets, percent signs, or other special characters should be escaped, as the code in `<restriction>` will be evaluated twice - once as an argument to lsearch(), and then again for each object looked at in the search. Before the per-object evaluation, the string "##" is replaced with the object dbref.

lsearch() is free unless it includes either an eval-class search or an elock search that contains an eval or indirect lock. Otherwise, it costs find_cost pennies to perform the lsearch.

lsearchr() is like an lsearch() run through revwords(). Results are returned from highest dbref to lowest. search() is an alias for lsearch().

See 'help lsearch3' for examples.

See also: [@search], [@find], [lparent()], [stats()]

# LSEARCH3

# SEARCH3

lsearch() Examples:

`lsearch(all, flags, Wc)` <-- lists all connected wizards.
`lsearch(me, type, room)` <-- lists all rooms owned by me.
`lsearch(me, type, room, flag, W)` <-- lists all Wizard rooms owned by me.
`lsearch(me, type, room, 100, 200)` <-- same, but only w/db# 100-200
`lsearch(all, eplayer, \[eq(money(##),100)\])` <-- lists all players with 100 coins.
`lsearch(all, type, player, elock, (FLAG^WIZARD|FLAG^ROYALTY)&!FLAG^IC)` ^-- list all wiz and roy players that are not IC.
`lsearch(all, type, player, elock, sex:m*)` <- lists all players with an @sex beginning with 'm'
`lsearch(me, elock, !desc:*)` <-- lists all objects you own that don't have an @desc set

# LSTATS()

# STATS()

`lstats([<player>])`

This function returns the breakdown of objects in the database, in a format similar to "@stats". If `<player>` is "all" (the default), a breakdown is done for the entire database. Otherwise, the breakdown is returned for that particular player.

Only wizards and those with the Search power can LSTATS() other players. The list returned is in the format:
`<Total objects> <Rooms> <Exits> <Things> <Players> <Garbage>`

stats() is an alias for lstats().

See also: [nsearch()]

# LT()

`lt(<number1>, <number2>[, ... , <numberN>])`

Takes two or more numbers, and returns 1 if and only if each number is less than the number after it, and 0 otherwise.

Examples:

```
> th lt(1,2)
1
```

```
> th lt(1,2,3)
1
```

```
> th lt(1,3,2)
0
```

See also: [lte()], [gt()], [gte()], [lnum()], [lmath()]

# LTE()

`lte(<number1>, <number2>[, ... , <numberN>])`

Takes two or more numbers, and returns 1 if and only if each number is less than or equal to the number after it, and 0 otherwise.

See also: [lt()], [gt()], [gte()], [lnum()], [lmath()]

# LVCON()

`lvcon(<object>)`

This function returns the dbrefs of all objects that are inside `<object>` and visible (non-dark). You must be in `<object>` or control it to use this function.

See also: [lcon()], [lvplayers()], [lvthings()], [lvexits()]

# LVEXITS()

`lvexits(<room>)`

This function returns the dbrefs of all visible (non-dark) exits from `<room>`. You must be in the room or control it to use this function.

See also: [lexits()], [lvcon()], [lvplayers()], [lvthings()]

# LVPLAYERS()

`lvplayers(<object>)`

This function returns the dbrefs of all connected and non-dark players in an object. You must be in the object or control it to use this function.

See also: [lplayers()], [lvcon()], [lvthings()], [lvexits()]

# LVTHINGS()

`lvthings(<object>)`

This function returns the dbrefs of all non-dark things inside an object. You must be in the object or control it to use this function.

See also: [lthings()], [lvplayers()], [lvcon()], [lvexits()]

# LWHO()

# LWHOID()

`lwho([<viewer>[, <status>]])`
`lwhoid([<viewer>[, <status>]])`

lwho() returns a list of the dbref numbers for all currently-connected players. When mortals use this function, the dbref numbers of hidden wizards or royalty do NOT appear on the dbref list.

If a `<viewer>` is given, and used by a See_All object, lwho() returns the output of lwho() from `<viewer>`'s point of view.

`<status>` can be used to include "#-1" dbrefs for unconnected ports, and must be one of "all", "online" (the default) or "offline". It is primarily useful when using a `<status>` with lports(), to make the dbrefs and ports match up. Only See_All players can see offline dbrefs.

lwhoid() returns a list of objid's instead.

See also: [mwho()], [nwho()], [xwho()], [lports()]

# MAP()

`map([<object>/]<attribute>, <list>[, <delim>[, <osep>]])`

This function works much like ITER(). The given `<attribute>` is evaluated once for each element of `<list>`, and the results of the evaluations are returned. For each evaluation, the current list element is passed to the attribute as %0, and its position in the list as %1. Elements of `<list>` are separated by `<delim>`, or a space if none is given, and the results are returned separated by `<osep>`, if given, or the delimiter otherwise.

This is roughly equivilent to, though slightly more efficient than:
`iter(<list>, ulambda(<object>/<attribute>, %i0, inum(0)), <delim>, <osep>)`

Examples:

```
> &times_two me=mul(%0,2)
```

```
> say map(times_two, 5 4 3 2 1)
You say, "10 8 6 4 2"
```

```
> say map(times_two,1;2;3;4;5,;)
You say, "2;4;6;8;10"
```

See also: [anonymous attributes], [iter()], [@dolist]

# ELEMENT()

# MATCH()

# MATCHALL()

`match(<list>, <pattern>[, <delimiter>])`
`matchall(<list>, <pattern>[, <delimiter>[, <output separator>]])`

match() returns the index of the first element of `<list>` which matches the wildcard pattern `<pattern>`. The first word has an index of 1. If no matches are found, 0 is returned. element() is an alias for match().

matchall() is similar, but returns the indexes of all matching elements. If no elements match, an empty string is returned.

In both cases, elements of `<list>` are separated by `<delimiter>`, if it's given, or a space otherwise. The results of matchall() are separated by `<ouput separator>`, if given, and `<delimiter>` if not.

To get the matching elements, instead of the indexes of where they appear in the list, use grab()/graball(). To see if a single string matches a wildcard pattern, use strmatch().

See 'help match2' for examples.

See also: [grab()], [strmatch()], [member()], [reglmatch()], [WILDCARDS]

# MATCH2

Examples:

```
> say match(I am testing a test, test*)
You say, "3"
```

```
> say matchall(I am testing a test, test*)
You say, "3 5"
```

```
> say match(foo bar baz boing, sprocket)
You say, "0"
```

```
>say matchall(foo bar baz boing, sprocket)
You say, ""
```

# REGLMATCH()

# REGLMATCHI()

# REGLMATCHALL()

# REGLMATCHALLI()

`reglmatch(<list>, <regexp>[, <delimiter>])`
`reglmatchi(<list>, <regexp>[, <delimiter>])`
`reglmatchall(<list>, <regexp>[, <delimiter>[, <output separator>]])`
`reglmatchalli(<list>, <regexp>[, <delimiter>[, <output separator>]])`

These functions are the regexp versions of match() and matchall(). reglmatch() returns the position of the first element in `<list>` which matches the regular expression `<regexp>`. reglmatchi() does the same thing, but case-insensitively.

reglmatchall() returns the positions of all elements in `<list>` which match `<regexp>`. reglmatchalli() is case-insensitive.

In all cases, the elements of `<list>` are separated by `<delimiter>`, which defaults to a space. The elements outputted by reglmatchall() are separated by `<output separator>`, if one is given, or by `<delimiter>` if not.

See 'help reglmatch2' for examples.

See also: [regmatch()], [regrab()], [match()], [REGEXP SYNTAX]

# REGLMATCH2

Examples:

```
> say reglmatch(I am testing a test, test)
You say, "3"
```

```
> say reglmatch(I am testing a test, test$)
You say, "5"
```

```
> say reglmatchall(I am testing a test, test, , |)
You say, "3|5"
```

# MAX()

`max(<number1>, <number2>[, ... , <numberN>])`

This function returns the largest number in its list of arguments. It can take any number of arguments.

See also: [min()], [lmath()], [bound()], [alphamax()]

# AVG()

# MEAN()

`mean(<number1>, <number2>[, ... , <numberN>])`

Returns the mean (arithmetic average) of its arguments.

avg() is an alias for mean(), for Rhost compatibility.

See also: [median()], [stddev()], [lmath()]

# MEDIAN()

`median(<number>, <number>[, ... , <numberN>)`

Returns the median (the middlemost numerically) of its arguments.

See also: [mean()], [stddev()], [lmath()]

# MEMBER()

`member(<list>, <word>[, <delimiter>])`

member() returns the position where `<word>` first occurs in `<list>`. If `<word>` is not present in `<list>`, it returns 0. Elements of `<list>` are `<delimiter>`-separated, or space-separated if no `<delimiter>` is given.

member() is case-sensitive, and does not perform wildcard matching. If you need to do a wildcard match, use match(). To compare two strings (instead of a word and list elements), consider comp().

See also: [match()], [grab()], [comp()], [strmatch()]

# MERGE()

`merge(<string1>, <string2>, <characters>)`

This function merges `<string1>` and `<string2>`, depending on `<characters>`. If a character in `<string1>` is the same as one in `<characters>`, it is replaced by the character in the corresponding position in `<string2>`. The two strings must be of the same length.

Example:

```
> say merge(AB--EF,abcdef,-)
You say, "ABcdEF"
```

Spaces need to be treated specially. An empty argument is considered to equal a space, for `<characters>`.

Example:

```
> say merge(AB[space(2)]EF,abcdef,)
You say, "ABcdEF"
```

See also: [splice()], [tr()]

# MESSAGE()

`message(<recipients>, <message>, [<object>/]<attribute>[, <arg0>[, ... , <arg9>][, <switches>]])`

message() is the function form of @message/silent, and sends a message, formatted through an attribute, to a list of objects. See 'help @message' for more information.

`<switches>` is a space-separated list of one or more of "nospoof", "spoof", "oemit" and "remit", and makes message() behaviour as per @message/<switches>. For backwards-compatability reasons, all ten `<arg>` arguments must be given (even if empty) to use `<switches>`.

Examples:

```
> &formatter #123
> think message(me, Default> foo bar baz, #123/formatter, foo bar baz)
Foo Bar Baz
```

```
> &formatter #123=Formatted> [iter(%0,capstr(%i0))]
> think message(me, Default> foo bar baz, #123/formatter, foo bar baz)
Formatted> Foo Bar Baz
```

```
> think message(here, default, #123/formatter, backwards compatability is annoying sometimes,,,,,,,,,,remit)
Formatted> Backwards Compatability Is Annoying Sometimes
```

See also: [@message], [oemit()], [remit()], [speak()]

# MID()

`mid(<string>, <first>, <length>)`

mid() returns `<length>` characters from `<string>`, starting from the `<first>` character. If `<length>` is positive, it counts forwards from the `<first>` character; for negative `<length>`s, it counts backwards. Note that the first character in `<string>` is numbered 0, not 1.

Examples:

```
> say mid(testing, 2, 2)
You say, "st"
```

```
> say mid(testing, 2, -2)
You say, "es"
```

See also: [left()], [right()], [strdelete()]

# MIN()

`min(<number1>, <number2>[, ... , <numberN>])`

This function returns the smallest number in its list of arguments. It can take any number of arguments.

See also: [max()], [lmath()], [bound()], [alphamin()]

# MIX()

`mix([<object>/]<attribute>, <list1>, <list2>[, ... , <list30>, <delim>])`

This function is similar to MAP(), except that it takes the elements of up to 30 lists, one by one, and passes them to the user-defined function as %0, %1, up to %9, respectively, for elements of `<list1>` to `<list30>`. Use v() to access elements 10 or higher. If the lists are of different sizes, the shorter ones are padded with empty elements. `<delim>` is used to separate elements; if it is not specified, it defaults to a space. If using more than 2 lists, the last argument must be a delimiter.

See 'help mix2' for examples.

# MIX2

Examples of mix():

```
> &add_nums me=add(%0, %1)
> say mix(add_nums,1 2 3 4 5, 2 4 6 8 10)
You say, "3 6 9 12 15"
```

```
> &lengths me=strlen(%0) and [strlen(%1)].
> say mix(lengths, some random, words)
You say, "4 and 5. 6 and 0."
```

```
> &add_nums me=lmath(add, %0 %1 %2)
> say mix(add_nums, 1:2:3, 4:5:6, 7:8:9, :)
You say, "12:15:18"
```

See also: [anonymous attributes], [map()], [step()]

# MOD()

# MODULO()

# MODULUS()

# REMAINDER()

`modulo(<number>, <number>[, ..., <numberN>])`
`remainder(<number>, <number>[, ..., <numberN>])`

remainder() returns the remainder of the integer division of the first number by the second (and subsequent) number(s) (ie, the remainder from calling div() with the same arguments).

modulo() returns the modulo of the given numbers (from calling floordiv() with the same arguments).

For positive numbers, these are the same, but they may be different for negative numbers:

`modulo(13,4)` ==> 1 and `remainder(13,4)` ==> 1
`modulo(-13,4)` ==> 3 but `remainder(-13,4)` ==> -1
`modulo(13,-4)` ==> -3 but `remainder(13,-4)` ==> 1
`modulo(-13,-4)` ==> -1 and `remainder(-13,-4)` ==> -1

remainder()s result always has the same sign as the first argument. modulo()s result always has the same sign as the second argument.

mod() and modulus() are aliases for modulo().

See also: [div()], [lmath()]

# MONEY()

`money(<integer>)`
`money(<object>)`

If given an integer, money() returns the appropriate name (either singular or plural) for that amount of money, as set in the money_singular and money_plural @config options.

Otherwise, it returns the amount of money `<object>` has. If `<object>` has the no_pay power, the value of the 'max_pennies' @config option is returned. `<object>` must have the power itself, rather than inheriting it from its owner, in this case.

Examples:

```
> say money(Javelin)
You say, "150"
```

```
> say money(1)
You say, "Penny"
```

```
> say money(2)
You say, "Pennies"
```

```
> &counter CvC=$count *: @say %0 [money(%0)]. Ah.. ah.. ah.
> count 2
Count von Count says, "2 Pennies. Ah.. ah.. ah."
```

See also: [score]

# MTIME()

# MSECS()

`mtime(<object>[, <utc?>])`
`msecs(<object>)`

mtime() returns the date and time that one of `<object>`'s attributes or locks was last added, deleted, or modified. The time returned is in the server's local timezone, unless `<utc?>` is true, in which case the time is in the UTC timezone.

msecs() returns the time as the number of seconds since the epoch.

Only things, rooms, and exits have modification times. You must be able to examine an object to see its modification time.

See also: [ctime()], [time()], [secs()], [convtime()], [convsecs()]

# MUDNAME()

# MUDURL()

`mudname()`
`mudurl()`

These functions return the name of the MUSH and the MUSH's website address, as set in the 'mud_name' and 'mud_url' @config options.

Example:

```
> say mudname()
You say, "TestMUSH"
```

```
> say mudurl()
You say, "http://www.testmush.com"
```

see also: [config()]

# MUL()

`mul(<number1>, <number2>[, ... , <numberN>])`

Returns the product of some numbers.

See also: [lmath()], [div()], [fdiv()]

# MUNGE()

`munge([<object>/]<attribute>, <list1>, <list2>[, <delimiter>[, <osep>]])`

This function takes two lists of equal length. It passes the entirety of `<list1>` to the user-defined function as %0, and the delimiter as %1. Then, this resulting list is matched with elements in `<list 2>`, and the rearranged `<list2>` is returned.

This is useful for doing things like sorting a list, and then returning the corresponding elements in the other list. If a resulting element from the user-defined function doesn't match an element in the original `<list1>`, a corresponding element from `<list2>` does not appear in the final result. The elements are matched using an exact, case-sensitive comparision.

`<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`.

See 'help munge2' for examples.

# MUNGE2

For example: Consider attribute PLACES, which contains "Fort Benden Ista", and another attribute DBREFS contains the dbrefs of the main JUMP_OK location of these areas, "#20 #9000 #5000". We want to return a list of dbrefs, corresponding to the names of the places sorted alphabetically. The places sorted this way would be "Benden Fort Ista", so we want the final list to be "#9000 #20 #5000". The functions, using munge(), are simple:

```
> &sort me=sort(%0)
> say munge(sort, v(places), v(dbrefs))
You say, "#9000 #20 #5000"
```

See 'help munge3' for another example.

# MUNGE3

Another common task that munge() is well suited for is sorting a list of dbrefs of players by order of connection. This example uses #apply to avoid the need for the sort attribute, and also unlike the other example, it builds the list to sort on out of the list to return.

```
> &faction_members me=#3 #12 #234
> say munge(#apply/sort, map(#apply/conn, v(faction_members)), v(faction_members))
You say, "#12 #234 #3"
```

See also: [anonymous attributes]

# MWHO()

# MWHOID()

`mwho()`
`mwhoid()`

mwho() returns a list of the dbref numbers for all current-connected, non-hidden players. It's exactly the same as lwho() used by a mortal, and is suitable for use on privileged global objects who need an unprivileged who-list. In some cases, `lwho(<viewer>)` may be preferable to mwho(), as it includes hidden players for `<viewer>`s who can see them.

mwhoid() returns a list of objids instead.

See also: [lwho()], [nwho()]

# ALIAS()

# FULLALIAS()

`alias(<object>[, <new alias>])`
`fullalias(<object>)`

alias() returns the first of `<object>`'s aliases. fullalias() returns all the aliases set for `<object>`. Note that, while any object can have an alias set, they are only meaningful for players and exits.

With two arguments, alias() attempts to change the alias for `<object>` to `<new alias>`, as per @alias.

Examples:

```
> ex *Noltar/ALIAS
ALIAS [#7$v]: $;No;Nol;Noli;Nolt
```

```
> say alias(*Noltar)
You say, "$"
```

```
> say fullalias(*Noltar)
You say, "$;No;Nol;Noli;Nolt"
```

See also: [fullname()]

# NAME()

`name(<object>[, <new name>])`

name() returns the name of object `<object>`. For exits, name() returns only the displayed name of the exit.

With two arguments, name() attempts to rename `<object>` to `<new name>`, as per @name.

See also: [fullname()], [accname()], [iname()], [alias()], [moniker()]

# MONIKER()

# CNAME()

`moniker(<object>)`

Returns `<object>`'s accented name, with the color template from its @moniker applied. moniker() always returns the colored name, even if monikers are disabled via @config.

See also: [MONIKERS], [@moniker], [name()], [MONIKER], [iname()], [accname()]

# NAMELIST()

`namelist(<player-list>[, [<object>/]<attribute>])`

namelist() takes a list of players of the form used by the page command and returns a corresponding list of dbrefs. Invalid and ambiguous names return the dbrefs #-1 and #-2, respectively.

If an `<object>/<attribute>` is given, the specified attribute will be called once for each invalid name, with the name as %0 and the dbref returned (#-1 for an unmatched name, #-2 for an ambiguous one) as %1.

Example:

```
> &test me=pemit(%#,Bad name "%0")
> say namelist(#1 Javelin "ringo spar" bogus, test)
Bad name "bogus"
You say, "#1 #7 #56 #-1"
```

See also: [namegrab()], [name()], [locate()], [num()], [pmatch()]

# NAMEGRAB()

# NAMEGRABALL()

`namegrab(<dbref list>, <name>)`
`namegraball(<dbref list>, <name>)`

The namegrab() function returns the first dbref in the list that would match `<name>` as if you were checking num() or locate(). An exact match has priority over partial matches.

namegraball() returns all dbrefs whose names would be matched by `<name>`.

Examples: #0 = Room Zero, #1 = One, #2 = Master Room

```
> say namegrab(#0 #1 #2,room)
You say, "#0"
```

```
> say namegrab(#0 #1 #2,master room)
You say, "#2"
```

```
> say namegraball(#0 #1 #2,room)
You say, "#0 #2"
```

See also: [namelist()], [locate()]

# NAND()

# NCAND()

`nand(<boolean1>[, ... , <booleanN>])`
`ncand(<boolean1>[, ... , <booleanN>])`

These functions return 1 if at least one of their arguments are false, and 0 if all are true. nand() always evaluates all of its arguments, while ncand() stops evaluating after the first false value.

Equivalent to `not(and())` and `not(cand())`, but more efficient.

See also: [lmath()], [and()], [cand()], [or()], [nor()]

# NEARBY()

`nearby(<object 1>, <object 2>)`

Returns 1 if `<object 1>` is "nearby" `<object 2>`, and 0 otherwise. "Nearby" means the objects are in the same location, or that one is located inside the other. You must control at least one of the objects; if you don't, or if one of the objects can't be found, nearby() returns #-1.

See also: [locate()], [findable()]

# NEQ()

`neq(<number1>, <number2>[, ... , <numberN>])`

Returns 0 if all the given `<number>`s are the same, and 1 otherwise. Basically the same as `[not(eq(<number1>, <number2>[, ... , <numberN>]))]` but more efficient.

See also: [eq()], [not()], [lmath()]

# NEXT()

`next(<object>)`

If `<object>` is an exit, then next() will return the next exit in `<object>`'s source room. If `<object>` is a thing or a player, then next() will return the next object in the contents list of `<object>`'s location. Otherwise, it returns a #-1. #-1 is also used to denote that there are no more exits or objects after `<object>`.

You can get the complete contents of any container you may examine, regardless of whether or not objects are dark. You can get the partial contents (obeying DARK/LIGHT/etc.) of your current location or the enactor (%#). You CANNOT get the contents of anything else, regardless of whether or not you have objects in it. These rules apply to exits, as well.

See also: [lcon()], [lexits()], [con()], [exit()]

# NEXTDBREF()

`nextdbref()`

This function returns the next dbref on the free list; when the next object is @created (or @dug, or @opened, or @pcreated, etc.), it will have this dbref.

See also: [@stats], [stats()]

# NOR()

# NCOR()

`nor(<boolean1>[, ... , <booleanN>])`
`ncor(<boolean1>[, ... , <booleanN>])`

These functions return 1 if all their arguments are false, and 0 if any are true. nor() always evaluates all arguments, while ncor() stops evaluating after the first true value.

Equivalent to `not(or())` and `not(cor())`, but more efficient.

See also: [and()], [or()], [xor()], [not()], [nand()], [lmath()]

# NOT()

`not(<boolean>)`

not() returns 1 if `<boolean>` is false, and 0 if it's true.

The definition of truth and falsehood depends on configuration settings; see 'help boolean values' for details.

See also: [Boolean Functions], [t()], [and()], [or()], [nor()], [xor()]

# NUM()

`num(<object>)`

Returns the dbref number of `<object>`. `<object>` must reference a valid object, as per 'help matching'.

See also: [locate()], [rnum()], [pmatch()]

# NVCON()

# NCON()

`ncon(<object>)`
`nvcon(<object>)`

These functions return a the number of objects inside `<object>`. They are identical to `words(lcon(<object>))` and `words(lvcon(<object>))`, respectively, but are more efficient and do not suffer from buffer constraints.

See also: [nexits()], [nplayers()], [xcon()], [lcon()], [lvcon()]

# NVEXITS()

# NEXITS()

`nexits(<room>)`
`nvexits(<room>)`

These functions return a count of the exits in a room. They are equivilent to `words(lexits(<room>))` and `words(lvexits(<room>))` respectively, though are more efficient, and don't suffer from buffer constraints.

See also: [ncon()], [nplayers()], [xexits()], [lexits()], [lvexits()]

# NVPLAYERS()

# NPLAYERS()

`nplayers(<object>)`
`nvplayers(<object>)`

These functions return a count of the players in `<object>`. They are equivilent to `words(lplayers(<object>))` and `words(lvplayers(<object>))` respectively, though are more efficient and do not suffer from buffer constraints.

See also: [ncon()], [nexits()], [xplayers()], [lplayers()], [lvplayers()]

# NVTHINGS()

# NTHINGS()

`nthings(<object>)`
`nvthings(<object>)`

These functions return a count of the things in a container. They are equivilent to `words(lthings(<object>))` and `words(lvthings(<object>))` respectively, though are more efficient and do not suffer from buffer constraints.

See also: [ncon()], [nexits()], [xthings()], [lthings()], [lvthings()]

# NMWHO()

# NWHO()

`nwho([<viewer>])`
`nmwho()`

nwho() returns a count of all currently-connected players. When mortals use this function, hidden players are NOT counted. See_All players can specify a `<viewer>` to get a count of the number of players that `<viewer>` can see is online.

nmwho() returns a count of all currently connected, non-hidden players. It's exactly the same as nwho() used by a mortal, and is suitable for use on privileged global objects that always need an unprivileged count of who is online.

These functions are equivilent to `words(lwho([<viewer>]))` and `words(mwho())`, but are more efficient, and don't suffer from buffer constraints.

See also: [lwho()], [mwho()], [xwho()], [xmwho()]

# OBJ()

# %o

`obj(<object>)`

Returns the objective pronoun - him/her/it - for an object. The %o substitution will return the objective pronoun of the enactor.

See also: [subj()], [poss()], [aposs()]

# OBJEVAL()

`objeval(<object>, <expression>)`

Allows you to evaluate `<expression>` from the viewpoint of `<object>`. If side-effect functions are enabled, you must control `<object>`; if not, you must either control `<object>` or have the see_all power. If `<object>` does not exist or you don't meet one of the criterion, the expression evaluates with your privileges.

See also: [s()]

# OBJID()

`objid(<object>)`

This function returns the object id of `<object>`, a value which uniquely identifies it for the life of the MUSH. The object id is the object's dbref, a colon character, and the object's creation time, in seconds since the epoch, equivilent to `[num(<object>)]:[csecs(<object>)]`

The object id can be used nearly anywhere the dbref can, and ensures that if an object's dbref is recycled, the new object won't be mistaken for the old object.

The substitution %: returns the object id of the enactor.

See also: [num()], [csecs()], [ctime()], [ENACTOR]

# OBJMEM()

`objmem(<object>)`

This function returns the amount of memory, in bytes, being used by the object. It can only be used by players with Search powers.

See also: [playermem()]

# OEMIT()

# NSOEMIT()

`oemit([<room>/]<object> [... <object>], <message>)`
`nsoemit([<room>/]<object> [... <object>], <message>)`

Sends `<message>` to all objects in `<room>` (default is the location of `<object>`(s)) except `<object>`(s), as per @oemit.

nsoemit() works like @nsoemit.

# OPEN()

`open(<exit name>[, <destination>[, <source>[, <dbref>]]])`

This function attempts to open an exit named `<exit name>`. The exit will be opened in the room `<source>`, if given, or the caller's current location if no `<source>` is specified.

If a `<destination>` is given, it will attempt to link the exit to `<destination>` after opening it.

Wizards and objects with the pick_dbref power can specify a garbage dbref to use for the new exit.

It returns the dbref of the newly created exit, or #-1 on error.

See also: [@open], [@link], [dig()], [link()], [create()], [pcreate()]

# OR()

# COR()

`or(<boolean1>, <boolean2>[, ... , <booleanN>])`
`cor(<boolean1>, <boolean2>[, ... , <booleanN>])`

These functions take a number of boolean values, and return 1 if any of them are true, and 0 if all are false. or() always evaluates all of its arguments, while cor() stops evaluating as soon as one is true.

See also: [BOOLEAN VALUES], [and()], [nor()], [firstof()], [allof()], [lmath()]

# ORFLAGS()

# ORLFLAGS()

`orflags(<object>, <string of flag characters>)`
`orlflags(<object>, <list of flag names>)`

These functions return 1 if `<object>` has any of the given flags, and 0 if it does not. orflags() takes a string of single flag letters, while orlflags() takes a space-separated list of flag names. In both cases, a ! before the flag means "not flag".

If there is a syntax error like a ! without a following flag, '#-1 INVALID FLAG' is returned. Unknown flags are treated as being not set.

Examples: Check to see if %# is set Wizard, Dark, or not set Ansi.

```
> say orflags(%#, WD!A)
```

```
> say orlflags(%#, wizard dark !ansi)
```

See also: [andflags()], [flags()], [lflags()], [orlpowers()]

# ORLPOWERS()

`orlpowers(<object>, <list of powers>)`

This function returns 1 if `<object>` has at least one of the powers in a specified list, and 0 if it does not. The list is a space-separated list of power names. A '!' preceding a flag name means "not power".

Thus, `ORLPOWERS(me, poll login)` would return 1 if I have the poll and login powers. `ORLFLAGS(me, functions !guest)` would return 1 if I have the functions power or are not a guest.

If there is a syntax error like a ! without a following power, '#-1 INVALID POWER' is returned. Unknown powers are treated as being not set.

See also: [powers()], [andlpowers()], [POWERS LIST], [@power], [orlflags()]

# OWNER()

`owner(<object>[/<attribute>])`
`owner(<object>[/<attribute>], <new owner>[, preserve])`

Given just an object, it returns the owner of the object. Given an object/attribute pair, it returns the owner of that attribute.

If `<new owner>` is specified, the ownership is changed, as in @chown or @atrchown. If the optional third argument is "preserve", privileged flags and powers will be preserved ala @chown/preserve.
If changing ownership, #-1 or 0 is returned on failure, 1 on success.

See also: [lockowner()], [@chown], [@atrchown]

# PARENT()

`parent(<object>[, <new parent>])`

This function returns the dbref number of an object's parent. You must be able to examine the object to do this. If you specify a second argument, parent() attempts to change the parent first. You must control `<object>`, and be allowed to @parent to `<new parent>`.

See also: [@parent], [ancestors], [pfun()], [lparent()]

# PEMIT()

# NSPEMIT()

# PROMPT()

# NSPROMPT()

`pemit(<object list|port numbers>, <message>)`
`nspemit(<object list|port numbers>, <message>)`
`prompt(<object list>, <message>)`
`nsprompt(<object list>, <message>)`

With an `<object list>`, pemit() will send each object on the list a message, as per the @pemit/list command. It returns nothing. It respects page-locks and HAVEN flags on players. With `<port numbers>`, pemit() sends the message to the specified ports only, like @pemit/port/list.

nspemit() works like @nspemit/list.

prompt() adds a telnet GOAHEAD to the end of the message, as per the @prompt command. nsprompt() that works like @nsprompt.

See also: [@prompt], [@nsprompt], [PROMPT_NEWLINES]

# PI()

`pi()`

Returns the value of "pi" (3.14159265358979323846264338327, rounded to the game's float_precision setting).

# PIDINFO()

`pidinfo(<pid>[, <list of fields>[, <output separator>]])`

This function returns information about a process id if the player has permission to see the process. The `<list of fields>` is a space-separated list that may contain the following elements:

*   **queue**: the queue ("wait" or "semaphore") for the process
*   **executor**: the queueing object
*   **time**: remaining time for timed queued entries (or -1)
*   **object**: the semaphore object for semaphores (or #-1)
*   **attribute**: the semaphore attribute for semaphores (or #-1)
*   **command**: the queued command

If `<list of fields>` is not provided, all fields are returned. The fields are separated by `<output separator>`, which defaults to a space.

See also: [@ps], [lpids()], [getpids()]

# PLAYERMEM()

`playermem(<player>)`

This function returns the amount of memory, in bytes, being used by everything owned by the player. It can only be used by players with Search powers.

See also: [objmem()]

# PLAYER()

`player(<port>)`

Returns the dbref of the player connected to a given port. Mortals can only use this function on their own ports, while See_All players can use it on any port.

See also: [lports()], [ports()]

# PMATCH()

`pmatch(<name>)`

pmatch() attempts to find a player called `<name>`, which should be the full or partial name of a player (possibly prefixed with a "*") or a dbref. First, it checks to see if `<name>` is the dbref, full name, or alias of a player; if so, their dbref is returned. Otherwise, it checks for partial matches against the names of online players. If there are no matches, #-1 is returned. If there are multiple matches, pmatch() returns #-2. Otherwise, the matching player's dbref is returned.

pmatch() does not check for the string "me". If you wish to do that, you should use locate (for example, `locate(<player>, <name>, PFym)`).

See also: [num()], [namelist()], [locate()]

# POLL()

`poll()`

This function returns the current @poll.

See also: [@poll], [doing()], [@doing]

# LPIDS()

`lpids([<object>[, <queue types>]])`

This function returns a list of queue process ids (pids). Only commands queued by objects with the same owner as `<object>` are listed. If you have the see_queue @power, you can specify "all" for `<object>` to get pids for everyone's queue entries. `<object>` defaults to the caller, or "all" for priviledged callers.

`<queue types>` should be a list of one or more of the following words, to filter the pids returned:

*   **wait**: -- Only return wait queues
*   **semaphore**: -- Only return semaphore queues
*   **independent**: -- Only return commands queued by `<object>` specifically, instead of all objects with the same owner as `<object>`.

If not specified, it defaults to "wait semaphore".

See also: [@ps], [getpids()], [pidinfo()]

# LPORTS()

# PORTS()

`lports([<viewer>[, <status>]])`
`ports(<player name>)`

These functions return the list of descriptors ("ports") that are used by connected players. lports() returns all ports, in the same order as lwho() returns dbrefs, and ports() returns those a specific player is connected to, from most recent to least recent. Mortals can use ports() on themselves, but only See_All players can use ports() on others, or use lports().

If lports() is given a `<viewer>`, only the ports of connections which `<viewer>` can see are returned, in the same way as `lwho(<viewer>)` works.

The `<status>` argument for lports() controls whether or not ports which are not connected to (ie, at the login screen) are included, and must be one of "all", "online" or "offline".

These port numbers also appear in the wizard WHO, and can be used with @boot/port, page/port, and the functions that return information about a connection to make them use a specific connection rather than the least-idle one when a player has multiple connections open. Players can get information about their own connections. See_all is needed to use them to get information about other people's ports.

See also: [lwho()], [player()], [Connection Functions]

# POS()

`pos(<needle>, <haystack>)`

This function returns the position that `<needle>` begins in `<haystack>`. Unlike most other string functions, the first character of `<haystack>` is numbered 1, not 0. If `<needle>` is not present in `<haystack>`, pos() returns #-1.

See also: [member()], [match()], [lpos()], [wordpos()]

# POSS()

# %p

`poss(<object>)`

Returns the possessive pronoun - his/her/its - for an object. The %p substitution also returns the possessive pronoun of the enactor.

See also: [subj()], [obj()], [aposs()]

# POWER()

`power(<number>, <exponent>)`

Returns `<number>` to the power of `<exponent>`.

(For the functional version of @power, see 'help powers()'.)

See also: [root()]

# POWERS()

`powers()`
`powers(<object>)`
`powers(<object>, <power>)`

With no arguments, powers() returns a space-separated list of all defined @powers on the MUSH. With one argument, it returns a list of the powers possessed by `<object>`.

With two arguments, it attempts to set `<power>` on `<object>`, as per `@power <object>=<power>`.

See also: [andlpowers()], [orlpowers()], [@power], [POWERS LIST]

# QUOTA()

`quota(<player>)`

Returns the player's quota, the maximum number of objects they can create if quotas are in effect. Returns 99999 for players with the No_Quota @power, so it's safe to use in numerical comparisons.

You must control `<player>` or have the See_All or Quotas @powers to use this function.

See also: [@quota], [@squota], [@allquota], [QUOTAS], [Quotas Power], [No_Quota Power]

# R()

# %q

# R-FUNCTION

`r(<register>[, <type>])`

The r() function can be used to access registers. It can retrieve the value of q-registers set with setq() and related functions, as well as the 30 stack values (the first ten of which are also available via %0-%9), and also iter() and switch() context (also available through itext() and stext(), respectively). The registers() function can be used to obtain a list of available registers.

`<type>` defaults to "qregisters", and must be one of:

*   **qregisters**: registers set with setq(), setr() and similar functions
*   **args**: the stack, usually accessed via %0-%9. There are up to 30 stack registers, plus named stack registers from regexp $-commands
*   **iter**: itext() context from iter() or @dolist. Must be an int, or "L" for the outermost itext().
*   **switch**: stext() context from switch() or @switch. Must be an int, or "L" for the outermost stext()
*   **regexp**: regexp capture names from re*() regexp functions

qregisters can also be accessed via the %qX (for one-char register names) or `%q<X>` (for registers with longer names) substitutions.

See also: [setq()], [letq()], [listq()], [unsetq()], [registers()], [v()], [itext()], [stext()], [ilev()], [slev()]

# RAND()

`rand()`
`rand(<num>)`
`rand(<min>, <max>)`

Return a random number.

The first form returns a floating-point number in the range 0 <= n < 1.

The second form returns an integer between 0 and `<num>`-1, inclusive (or between 0 and `<num>`+1, for negative `<num>`s).

The third returns an integer between `<min>` and `<max>`, inclusive.

If called with an invalid argument, rand() returns an error message
beginning with #-1.

See also: [randword()]

# RANDWORD()

# PICKRAND()

`randword(<list>[, <delimiter>])`

Returns a randomly selected element from `<list>`. Elements of the list are separated by `<delimiter>`, which defaults to a space.

pickrand() is an alias for randword().

See also: [rand()], [randextract()]

# RANDEXTRACT()

`randextract(<list>[, <count>[, <delim>[, <type>[, <osep>]]]])`

Returns up to `<count>` random elements from the `<delim>`-separated `<list>`. The following `<type>`s are available:

*   **R**: Grab `<count>` elements from `<list>` at random, but don't duplicate any elements
*   **L**: Grab `<count>` elements from `<list>`, in order, starting at a random element
*   **D**: Grab `<count>` elements from `<list>` at random, with duplicates allowed

randextract() may return less than `<count>` elements for `<type>`s L and R, depending on the random element chosen and the length of `<list>`. Elements of the returned list are separated by `<osep>`, which defaults to `<delim>`. `<delim>` defaults to a single space, `<count>` defaults to 1, and `<type>` defaults to R.

Examples:

```
> say randextract(this is a test,3)
You say "this test a"
```

```
> say randextract(this@is@a@test,3,@)
You say "this@a@test"
```

```
> say randextract(this is a test,3,,L,*)
You say "this*is*a"
```

```
> say randextract(this is a test,6,,D)
You say, "this test is this is is"
```

See also: [rand()], [randword()]

# REGEDIT()

# REGEDITALL()

# REGEDITI()

# REGEDITALLI()

`regedit(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`
`regediti(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`
`regeditall(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`
`regeditalli(<string>, <regexp>, <replace>[, ... , <regexpN>, <replaceN>])`

These functions edit `<string>`, replacing the part of the string which matches the regular expression `<regexp>` with the accompanying `<replace>`. In `<replace>`, the string "$`<number>`" is expanded during evaluation to the `<number>`th sub-expression, with $0 being the entire matched section. If you use named sub-expressions (?P`<name>`subexpr), they are referred to with "$`<name>`". Note that, with named sub-expressions, the "<>" are literal.

regedit() only replaces the first match, while regeditall() replaces all matches. The versions ending in i are case insensitive. The `<replace>` argument is evaluated once for each match, allowing for more complex transformations than is possible with straight replacement.

Examples:

```
> say regedit(this test is the best string, (?P<char>.)est, $<char>rash)
You say "this trash is the best string"
```

```
> say regeditall(this test is the best string, (.)est, capstr($1)rash)
You say "this Trash is the Brash string"
```

See also: [edit()], [@edit], [regmatch()], [regrab()]

# REGMATCH()

# REGMATCHI()

(Help text from TinyMUSH 2.2.4, with permission)
`regmatch(<string>, <regexp>[, <register list>])`
`regmatchi(<string>, <regexp>[, <register list>])`

regmatch() checks to see if the entirety of `<string>` matches the regular expression `<regexp>`, and returns 1 if so and 0 if not. regmatchi() does the same thing, but case-insensitively. They are the regexp-equivilent of strmatch(); if you're looking for a regexp version of match(), see 'help reglmatch()'.

If `<register list>` is specified, there is a side-effect: any parenthesized substrings within the regular expression will be set into the specified local registers. The syntax for this is X:Y, where X is the number (0 is the entire matched text) or name of the substring, and Y is the q-register to save it in. If X: isn't given, the nth substring based on the register's position in the list minus one is used. The first element will have the complete matched text, the second the first substring, and so on. This is to maintain compatibility with old code; it's recommended for new uses that the X:Y syntax be used.

If `<regexp>` is not a valid regular expression, an error in the form "#-1 REGEXP ERROR: `<description>`" will be returned.

See 'help regmatch2' for an example.

See also: [regrab()], [regedit()], [valid()], [reswitch()], [strmatch()], [regexp syntax]

# REGMATCH2

For example, in

```
> think regmatch(cookies=30, (.+)=(\[0-9\]*) )
```

(note use of escaping for MUSH parser), then the 0th substring matched is 'cookies=30', the 1st substring is 'cookies', and the 2nd substring is '30'. If `<register list>` is '0:0 1:3 2:5', then %q0 will become "cookies=30", %q3 will become "cookies", and %q5 will become "30".

If `<register list>` was '0:0 2:5', then the "cookies" substring would simply be discarded. '1:food 2:amount' would store "cookies" in `%q<food>` and "30" in `%q<amount>`.

See 'help regexp syntax' for an explanation of regular expressions.

# REMIT()

# NSREMIT()

`remit(<object list>, <message>)`
`nsremit(<object list>, <message>)`

Sends a message to the contents of all the objects specified in `<object list>`, as per @remit/list.

nsremit() works like @nsremit/list.

See also: [@remit], [pemit()], [lemit()]

# REMOVE()

`remove(<list>, <words>[, <delimiter>])`

This function removes the first occurrence of every word in the list `<words>` from `<list>`, and returns the resulting `<list>`. It is case sensitive.

Elements of `<list>` and `<words>` are both separated by `<delimiter>`, which defaults to a space.

See also: [linsert()], [ldelete()], [setdiff()]

# RENDER()

`render(<string>, <formats>)`

This function renders the given `<string>` into a given format. Most useful when coding bots, or inserting text into an SQL database to display on a website. `<formats>` is a space-separated list of one or more of the following:

*   **ansi**: -- Convert colors to raw ANSI tags (requires Can_Spoof power)
*   **html**: -- Escape HTML entities (< to &lt;, etc) and convert Pueblo to HTML tags
*   **noaccents**: -- Downgrade accented characters, as per stripaccents()
*   **markup**: -- Leave any markup not already converted by ansi/html as internal markup tags. Without this, unhandled markup will be stripped, as per stripansi()

Examples:

```
> say render(<Test 1> & [tagwrap(u,Test 2)], html)
You say, "&lt;Test 1&gt; &amp; <u>Test 2</u>"
```

See also: [stripaccents()], [stripansi()], [Pueblo], [@sql], [tagwrap()], [json()]

# REPEAT()

`repeat(<string>, <number>)`

This function simply repeats `<string>`, `<number>` times. No spaces are inserted between each repetition.

Example:

```
> say repeat(Test, 5)
You say, "TestTestTestTestTest"
```

See also: [space()]

# LREPLACE()

# REPLACE()

`lreplace(<list>, <position(s)>, <new item>[, <delimiter>[, <osep>]])`

This replaces the item(s) at the given `<position(s)>` in `<list>` with `<new item>`. `<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`. Null items are counted when determining position.

If `<position>` is negative, it counts backwards from the end of the list. A `<position>` of -1 will replace the last element, -2 the element before last, and so on.

Examples:

```
> say lreplace(Turn north at the junction,2,south)
You say, "Turn south at the junction"
```

```
> say lreplace(Turn north at the junction,-1,crossroads)
You say, "Turn north at the crossroads"
```

```
> say lreplace(blue|red|green|yellow,3,white,|)
You say, "blue|red|white|yellow"
```

```
> say lreplace(this starts and ends the same, 1 -1, foo)
You say, "foo starts and ends the foo"
```

replace() is an alias for lreplace(), for backwards compatability.

See also: [ldelete()], [linsert()], [setdiff()], [splice()], [strreplace()]

# REST()

`rest(<list>[, <delimiter>])`

Returns a list minus its first element.

See also: [after()], [first()], [last()]

# REVWORDS()

`revwords(<list>[, <delimiter>[, <output separator>]])`

This function reverses the order of words in a list. List elements are separated by `<delimiter>`, which defaults to a space. Elements in the reversed list are separated by `<ouput separator>`, which defaults to the delimiter.

Example:

```
> say revwords(foo bar baz eep)
You say, "eep baz bar foo"
```

See also: [flip()]

# RIGHT()

`right(<string>, <length>)`

Returns the `<length>` rightmost characters from `<string>`.

See also: [left()], [mid()]

# RJUST()

`rjust(<string>, <length>[, <fill>[, <truncate?>]])`

This function returns `<string>`, padded on the left with the string `<fill>` until it's `<length>` characters long. `<fill>` can be more than one character in length, and defaults to a single space.

If `<string>` is longer than `<length>`, it will be returned unaltered, unless `<truncate?>` is true, in which case only the last `<length>` characters of `<string>` are returned.

Examples:

```
> say -[rjust(foo,6)]-
You say, "-   foo-"
```

```
> say %r0[rjust(foo,6,-)]%r01234567
You say, "
0---foo7
01234567"
```

```
> say rjust(foo,12,=-)
You say, "=-=-=-=-=foo"
```

```
> say rjust(This is too long,9,,1)
You say, " too long"
```

See also: [align()], [center()], [ljust()], [right()]

# RLOC()

`rloc(<object>, <levels>)`

This function may be used to the get the location of `<object>`'s location (and on through the levels of locations), substituting for repeated nested loc() calls. `<levels>` indicates the number of loc()-equivalent calls to make; i.e., `loc(loc(<object>))` is equivalent to `rloc(<object>,2)`. `rloc(<object>,0)` is equivalent to `num(<object>)`, and `rloc(<object>,1)` is equivalent to `loc(<object>)`.

If rloc() encounters a room, the dbref of that room is returned. If rloc() encounters an exit, the dbref of that exit's destination is returned. You must control `<object>`, be near it, or it must be a findable player.

See also: [loc()], [where()], [room()], [rnum()], [locate()]

# RNUM()

`rnum(<container>, <object>)`

This function looks for an object called `<object>` located inside `<container>`. If a single matching object is found, its dbref is returned. If several matching objects are found, #-2 is returned, and if nothing matches, or you lack permission, #-1 is returned.

You must be in `<container>`, or be able to examine it, to use this function.

This function has been deprecated and may be removed in a future patchlevel; `locate(<container>, <object>, i)` should be used instead.

See also: [locate()], [num()], [rloc()], [room()]

# ROOM()

`room(<object>)`

Returns the "absolute" location of an object. This is always a room; it is the container of all other containers of the object. The "absolute" location of an object is the place @lemit messages are sent to and NO_TEL status determined. You must control the object, be See_All, or be near the object in order for this function to work. The exception to this are players; if `<object>` is a player, the ROOM() function may be used to find the player's absolute location if the player is not set UNFINDABLE.

See also: [loc()], [rloc()], [rnum()], [where()]

# ROOT()

`root(<number>, <n>)`

Returns the n-th root of `<number>`. The 2nd root is the square root, the 3rd the cube root, and so on.

Examples:

```
> think root(27, 3)
3
```

```
> think power(3, 3)
27
```

See also: [sqrt()], [power()]

# ROUND()

# CEIL()

# FLOOR()

`round(<number>, <places>[, <pad>])`
`floor(<number>)`
`ceil(<number>)`

round() rounds `<number>` to `<places>` decimal places. `<places>` must be between 0 and config(float_precision). If the optional `<pad>` argument is true, the result will be padded with 0s if it would otherwise have fewer than `<places>` digits after the decimal point.

floor() rounds `<number>` down, and ceil() rounds `<number>` up, to 0 decimal places.

Examples:

```
> think round(3.14159, 2)
3.14
```

```
> think round(3.5, 3, 1)
3.500
```

```
> think ceil(3.14159)
4
```

```
> think floor(3.14159)
3
```

See also: [bound()], [trunc()]

# FN()

`fn([<obj>/]<function name>[, <arg0>[, ... , <argN>]])`

fn() executes the built-in/hardcoded function `<function name>`, even if the function has been deleted or overridden with @function. It is primarily useful within @functions that override built-ins in order to be able to call the built-in.

Example:

```
> &BRIGHT_PEMIT #10=fn(pemit,%0,-->[ansi(h,%1)])
> @function/delete PEMIT
> @function PEMIT=#10,BRIGHT_PEMIT
> think pemit(me,test)
-->test   (in highlighted letters)
```

To restrict the use of fn() to @functions only (to prevent players from skirting softcoded replacements), use @function/restrict fn=userfn.

To prevent deleted functions from being used with fn(), @function/disable them prior to deleting.

Continued in [help fn2].

# FN2

If `<obj>` is specified, the built-in function will be executed as `<obj>`, rather than as the object which called fn(). This is useful when using fn() to replace a side-effect function, to ensure priviledge checks, etc, are done correctly. You must control `<obj>`, or (if function side effects are disabled) must be see_all.

When an `<obj>` is given, debug information is automatically suppressed when evaluating the built-in function.

Example:

```
> &BRIGHT_PEMIT #10=fn(%@/pemit, %0, -->[ansi(h,%1)]))
> @function/delete PEMIT
> @function PEMIT=#10, BRIGHT_PEMIT
> @lock/page *Mike=!=*Padraic
```

(As Padraic)

```
> think pemit(me,test)
-->test  (in highlighted letters)
```

```
> think pemit(*Mike,test)
(nothing happens)
```

See also: [@function], [RESTRICT], [attribute flags]

# S()

# S-FUNCTION

`s(<string>)`

This function performs a second round of evaluation on `<string>`, and returns the result. It should be considered extremely dangerous to use on user input, or any other string which you don't have complete control over. There are very few genuine uses for this function; things can normally be achieved another, safer way.

Example:

```
> &test me=$eval *: say When we eval %0, we get [s(%0)]
> eval \[ucstr(test)]
You say, "When we eval [ucstr(test)], we get TEST"
```

See also: [objeval()], [decompose()]

# SCAN()

`scan(<looker>, <command>[, <switches>])`
`scan(<command>)`

This function works like @scan, and returns a space-separated list of dbref/attribute pairs containing $-commands that would be triggered if `<command>` were run by `<looker>`. You must control `<looker>` or be See_All to use this function. Only objects you can examine are included in the output.

If no `<looker>` is specified, it defaults to the executor.

`<switches>` is a space-separated list of strings to limit which objects are checked for $-commands. Valid switches are:

*   **room**: -- check `<looker>`'s location and its contents
*   **me**: -- check `<looker>`
*   **inventory**: -- check objects in `<looker>`'s inventory
*   **self**: -- check `<looker>` and objects in `<looker>`'s inventory
*   **zone**: -- check `<looker>`'s location's zone, and `<looker>`'s own zone
*   **globals**: -- check objects in the Master Room
*   **all**: -- all of the above (the default)
*   **break**: -- once a match is found, don't check in other locations

The order of searching for the "break" switch is the same as the order for normal $-command matching, as described in 'help evaluation order'.

See also: [@scan], [@sweep], [MASTER ROOM], [EVALUATION ORDER], [$-COMMANDS]

# SCRAMBLE()

`scramble(<string>)`

This function scrambles a string, returning a random permutation of its characters. Note that this function does not pay any attention to spaces or other special characters; it will scramble these characters just like normal characters.

Example:

```
> say scramble(abcdef)
You say, "cfaedb"
```

See also: [shuffle()]

# SECS()

`secs()`

This function takes no arguments, and returns the number of elapsed seconds since midnight, January 1, 1970 UTC. UTC is the base time zone, formerly GMT. This is a good way of synchronizing things that must run at a certain time.

See also: [convsecs()], [time()]

# SECURE()

`secure(<string>)`

This function returns `<string>` with all "dangerous" characters replaced by spaces. Dangerous characters are
`( ) [ ] { } $ % , ^ ;`
Note that the use of this function is very rarely needed.

See also: [decompose()], [escape()]

# SET()

`set(<object>[/<attribute>], <flag>)`
`set(<object>, <attribute>:<value>)`

This function is equivalent to @set, and can be used to toggle flags and set attributes. The two arguments to the function are the same as the arguments that would appear on either side of the '=' in @set. This function returns nothing.

The attribute-setting ability of set() is deprecated. You should use attrib_set() instead; it's easier to read, and allows you to clear attributes, too.

See also: [attrib_set()], [@set], [wipe()]

# SETDIFF()

`setdiff(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

This function returns the difference of two sets -- i.e., the elements in `<list1>` that aren't in `<list2>`. The list that is returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in 'help sorting'. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

Example:

```
> say setdiff(foo baz gleep bar, bar moof gleep)
You say, "baz foo"
```

See also: [setinter()], [setsymdiff()], [setunion()]

# SETSYMDIFF()

`setsymdiff(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

This function returns the symmetric difference of two sets -- i.e., the elements that only appear in one or the other of the lists, but not in both. The list that is returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in 'help sorting'. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

Example:

```
> say setsymdiff(foo baz gleep bar, bar moof gleep)
You say, "baz foo moof"
```

See also: [setdiff()], [setinter()], [setunion()]

# SETINTER()

`setinter(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

This function returns the intersection of two sets -- i.e., the elements that are in both `<list1>` and `<list2>`. The list that is returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in 'help sorting'. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

Example:

```
> say setinter(foo baz gleep bar, bar moof gleep)
You say, "bar gleep"
```

See also: [setdiff()], [setsymdiff()], [setunion()]

# SETQ()

# SETR()

`setq(<register1>, <string1>[, ... , <registerN>, <stringN>])`
`setr(<register1>, <string1>[, ... , <registerN>, <stringN>])`

The setq() and setr() functions are used to copy strings into local registers assigned arbitrary names (Much like variables in other programming languages.) setq() returns a null string; it is a purely "side effect" function. setr() returns the value stored. Multiple registers can be assigned with a single setq() or setr(), with additional pairs of registers and values in the function's arguments. In this case, setr() returns the value stored in the first register listed. All arguments are evaluated before any registers are set; if you want to use the result of setting one register in setting another, use multiple setq()s.

Registers set via setq() or setr() can be accessed via the r() function. Single-character registers can also be accessed via the %qN substitution, and ones with longer names via `%q<NAME>` (Note that the <>'s are required.) Attempting to access a register that hasn't been set results in an empty string.

Register names are case insensitive: `setq(A, foo)` and `setq(a, foo)` both set the same register, and %qA and %qa both fetch its value.

See 'help setq2' for more on limits, or 'help setq3' for examples.

See also: [r()], [listq()], [unsetq()], [letq()], [localize()], [ulocal()], [registers()]

# SETQ2

Register names follow the same rules for attribute names, but they must be shorter than 64 characters in length.

Register names other than a-z or 0-9 have a per-localize limit, defined with @config max_named_qregs. If setq or setr tries to set a named q-register and it exceeds the limit, it will return the string "#-1 TOO MANY REGISTERS". This is the only time setq will return a string. setq() and setr() with registers a-z or 0-9 have nothing to worry about.

The maximum number of q-registers you can have set is configured via @config max_attrs_per_obj. That number is for the total number of q-registers set in a queue entry: Including across localize()d calls. Beyond that count, you can only use single character registers (a-z 0-9). Attempts to create a new register will simply fail silently, with the exception of setq().

See 'help setq3' for examples.

# SETQ3

The setq() function is probably best used at the start of the string being manipulated, such as in the following example:

```
> &TEST object=strlen(%0)
> &CMD object=$test *: say setq(0,u(TEST,%0))Test. %0 has length %q0.
> test Foo
Object says, "Test. Foo has length 3."
```

In this case, it is a waste to use setq(), since we only use the function result once, but if TEST was a complex function being used multiple times within the same command, it would be much more efficient to use the local register, since TEST would then only be evaluated once. setq() can thus be used to improve the readability of MUSH code, as well as to cut down the amount of time needed to do complex evaluations.

Swapping the contents of registers can be done without writing to temporary registers by setting both registers at once, so the code:

```
> think setq(0,foo,one,bar)%q0%q<one> - [setq(0,r(one),one,%q0)]%q0%q<one>
foobar - barfoo
```

See 'help setq4' for scoping rules of setq().

# SETQ4

The registers set by setq() can be used in later commands in the same thread. That is, the registers are set to null on all $-commands, ^-commands, A-attribute triggers, etc., but are then retained from that point forward through the execution of all your code. Code branches like @wait and @switch retain the register values from the time of the branch.

Example:

```
> say setr(what,foo); @wait 0=say %q<what>; say setr(what,bar)
Object says "foo"
Object says "bar"
Object says "foo"
```

# LISTQ()

# UNSETQ()

`listq([<pattern>])`
`unsetq([<pattern1> [<pattern2> [...]]])`

listq() returns a space-separated list of set q-registers with values available in the current q-register scope. If `<pattern>` is provided, then only those that match the wildcard pattern `<pattern>` will be returned.

unsetq() without arguments clears all registers. Otherwise, unsetq() treats its argument as a list of register name patterns, and will unset all those registers within the local scope.

If unsetq() is inside of a letq(), and does not have an argument, it will clear the registers that letq() has protected. unsetq() with arguments clears the specified registers.

`unsetq(<arg>)` will clear all registers returned by `listq(<arg>)`.

Example:

```
> think setq(name,Walker,num,#6061,loc,Bahamas)[listq()]
LOC NAME NUM
```

```
> think setq(name,Walker,num,#6061,loc,Bahamas)[listq(n*)]
NAME NUM
```

```
> think setq(name,Walker,num,#6061,loc,Bahamas)[unsetq(name)][listq()]
LOC NUM
```

```
> think setq(name,Walker,num,#6061,loc,Bahamas)[unsetq(n*)][listq()]
LOC
```

See also: [setq()], [letq()], [r()], [localize()], [registers()], [WILDCARDS]

# REGISTERS()

`registers([<pattern>[, <types>[, <osep>]]])`

The registers() function returns a list of the names of all existing registers of the specified `<types>`. `<types>` is a space-separated list containing zero or more of:

*   **qregisters**: registers set with setq(), setr() and similar functions
*   **args**: %0-%9 arguments
*   **iter**: itext() context from iter() or @dolist
*   **switch**: stext() context from switch() or @switch
*   **regexp**: regexp capture names

If `<types>` is empty, all types of registers are included. If `<pattern>` is specified, only registers whose name matches `<pattern>` will be included. The results are separated by `<osep>`, which defaults to a single space.

The list returned may contain duplicates (for instance, if %0 and %q0 both have a value, the list will include "0" twice), and is not sorted in any particular order.

See also: [listq()], [setq()], [setr()], [letq()], [r()], [v()], [stext()], [itext()]

# SETUNION()

`setunion(<list1>, <list2>[, <delimiter>[, <sort type>[, <osep>]]])`

This function returns the union of two sets -- i.e., all the elements of both `<list1>` and `<list2>`, minus any duplicate elements. The list returned is sorted. Normally, alphabetic sorting is done. You can change this with the fourth argument, which is a sort type as defined in 'help sorting'. If used with exactly four arguments where the fourth is not a sort type, it's treated instead as the output separator.

Examples:

```
> say setunion(foo baz gleep bar, bar moof gleep)
You say, "bar baz foo gleep moof"
```

```
> say setunion(1.1 1.0, 1.000)
You say, "1.0 1.000 1.1"
```

```
> say setunion(1.1 1.0, 1.000, %b, f)
You say, "1.0 1.1"
```

See also: [setdiff()], [setinter()], [setsymdiff()]

# SHA0()

`sha0(<string>)`

Returns the SHA-0 cryptographic hash of the string. See RFC 3174 for more information. Deprecated; use digest() and higher strength algorithms instead. On servers with newer versions of OpenSSL that no longer provide the algorithm, returns #-1 NOT SUPPORTED.

See also: [digest()].

# SHL()

`shl(<number>, <count>)`

Performs a leftwards bit-shift on `<number>`, shifting it `<count>` times. This is equivalent to `mul(<number>, power(2, <count>)`, but much faster.

See also: [shr()]

# SHR()

`shr(<number>, <count>)`

Performs a rightwards bit-shift on `<number>`, shifting it `<count>` times. This is equivalent to `div(<number>, power(2, <count>)`, but much faster.

See also: [shl()]

# SHUFFLE()

`shuffle(<list>[, <delimiter>[, <osep>]])`

This function shuffles the order of the items of a list, returning a random permutation of its elements.

`<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`.

Example:

```
> say shuffle(foo bar baz gleep)
You say, "baz foo gleep bar"
```

See also: [scramble()], [pickrand()]

# SIGN()

`sign(<number>)`

Essentially returns the sign of a number -- 0 if the number is 0, 1 if the number is positive, and -1 if the number is negative. This is equivalent to `bound(<number>, -1, 1)`.

Example:

```
> say sign(-4)
You say, "-1"
```

```
> say sign(2)
You say, "1"
```

```
> say sign(0)
You say, "0"
```

See also: [abs()], [bound()]

# SIN()

`sin(<angle>[, <angle type>])`

Returns the sine of `<angle>`, which should be expressed in the given angle type, or radians by default.

See 'HELP ANGLES' for more on the angle type.

See also: [acos()], [asin()], [atan()], [cos()], [ctu()], [tan()]

# SORT()

`sort(<list>[, <sort type>[, <delimiter>[, <osep>]]])`

This sorts a list of words. If no second argument is given, it will try to detect the type of sort it should do. If all the words are numbers, it will sort them in order of smallest to largest. If all the words are dbrefs, it will sort them in order of smallest to largest. Otherwise, it will perform a lexicographic sort.

The second argument is a sort type. See 'help sorting'.

The optional third argument gives the list's delimiter character. If not present, `<delimiter>` defaults to a space. The optional fourth argument gives a string that will delimit the resulting list; it defaults to `<delimiter>`.

See also: [sortby()], [sortkey()]

# SORTBY()

`sortby([<obj>/]<attrib>, <list>[, <delimiter>[, <output separator>]])`

This sorts an arbitrary list according to the ufun `<obj>/<attrib>`. This ufun should compare two arbitrary elements, %0 and %1, and return zero (equal), a negative integer (element 1 is less than element 2) or a positive integer (element 1 is greater than element 2), similar to the comp() function.

A simple example, which imitates a normal alphabetic sort:

```
> &ALPHASORT test=comp(%0,%1)
> say sortby(test/ALPHASORT,foo bar baz)
You say, "bar baz foo"
```

A slightly more complicated sort. #1 is "God", #2 is "Amby", "#3" is "Bob":

```
> &NAMESORT me=comp(name(%0),name(%1))
> say sortby(NAMESORT,#1 #2 #3)
You say, "#2 #3 #1"
```

Warning: the function invocation limit applies to this function. If this limit is exceeded, the function will fail _silently_. List and function sizes should be kept reasonable.

See also: [anonymous attributes], [sorting], [sort()], [sortkey()]

# SORTKEY()

`sortkey([<obj>/]<attrib>, <list>[, <sort type>[, <delimiter>[, <osep>]]])`

This function creates a list of keys by passing every element of `<list>` into the ufun given in `<attrib>`. The list is then sorted according to the sorting method in `<sort type>`, or is automatically guessed (as per 'help sorting').

This is equivalent to:

```
> &munge_sort me=sort(%0[, <sort type>])
> say munge(munge_sort, map(<attrib>, <list>), <list>)
```

Only there is no risk with delimiters occurring within the list.

A simple example, which sorts players by their names:

```
> @@ #1 is "God", #2 is "Amby", "#3" is "Bob"
> &KEY_NAME me=name(%0)
> say sortkey(key_name, #1 #2 #3)
You say, "#2 #3 #1"
```

See also: [anonymous attributes], [sorting], [sortby()]

# SORTING

In functions where you can specify a sorting method, you can provide one of these sort types:

*   **a**: Sorts lexicographically (Maybe case-sensitive).
*   **i**: Sorts lexicographically (Always case-insensitive).
*   **d**: Sorts dbrefs.
*   **n**: Sorts integer numbers.
*   **f**: Sorts decimal numbers.
*   **m**: Sorts strings with embedded numbers and dbrefs (as names).
*   **name**: Sorts dbrefs by their names. (Maybe case-sensitive)
*   **namei**: Sorts dbrefs by their names. (Always case-insensitive)
*   **conn**: Sorts dbrefs by their connection time.
*   **idle**: Sorts dbrefs by their idle time.
*   **owner**: Sorts dbrefs by their owner dbrefs.
*   **loc**: Sorts dbrefs by their location dbref.
*   **ctime**: Sorts dbrefs by their creation time.
*   **mtime**: Sorts dbrefs by their modification time.
*   **lattr**: Sorts attribute names.

The special sort key `attr:<aname>` or `attri:<aname>` will sort dbrefs according to their `<aname>` attributes. For example: Separating by &factions or &species attrs. attr is probably case-sensitive, and attri is case-insensitive.

Prefixing the sort type with a minus sign, -, reverses the order of the sort.

Whether or not the 'a' sort type is case-sensitive or not depends on the particular mush and its environment.

See also: [sort()], [sortby()], [sortkey()], [setunion()], [setinter()], [setdiff()]

# SOUNDEX()

`soundex(<word>[, <hash type>])`

The soundex function returns the soundex pattern for a word. A soundex pattern represents the sound of the word, and similar sounding words should have the same soundex pattern. Soundex patterns consist of an uppercase letter and 3 digits.

```
> think soundex(foobar)
F160
```

For details of how the algorithm works, see 'help soundex2'.

See also: [soundslike()]

# SOUNDEX2

Here's how the soundex algorithm works:

1.  The first letter of the soundex code is the first letter of the word (exception: words starting with PH get a soundex starting with F)
2.  Each remaining letter is converted to a number:
    *   vowels, h, w, y -> 0
    *   b, p, f, v -> 1
    *   c, g, j, k, q, s, x, z -> 2
    *   d, t -> 3
    *   l -> 4
    *   m, n -> 5
    *   r -> 6
    At this stage, "foobar" is "F00106"
3.  Strings of the same number are condensed. "F0106"
4.  All 0's are removed, because vowels are much less important than consonants in distinguishing words. "F16"
5.  The string is padded with 0's or truncated to 4 characters. "F160"
    That's it. It's not foolproof (enough = "E520", enuf = "E510") but it works pretty well. :)

The optional second argument can be 'soundex' (The default), for the transformation described above, or 'phone', for a different phonetic hash algorithm.

# SOUNDLIKE()

# SOUNDSLIKE()

`soundslike(<word>, <word>[, <hash type>])`
`soundlike(<word>, <word>[, <hash type>])`

The soundslike function returns 1 if the two words have the same hash code (see 'help soundex()' for information), which means, in general, if they sound alike. The hash type can be 'soundex' (Default) or 'phone' for a different algorithm that might give better results with some words.

Examples:

```
> think soundslike(robin,robbyn)
1
```

```
> think soundslike(robin,roebuck, phone)
0
```

See also: [soundex()]

# SPACE()

`space(<number>)`

Prints `<number>` spaces. Useful for times when you want to be able to use lots of spaces to separate things. Same as `[repeat(%b, <number>)]`.

Example:

```
> say a[space(5)]b
Amberyl says, "a     b"
```

See also: [repeat()]

# SPEAK()

# SPEAKPENN()

`speak(<speaker>, <string>[, <say string>[, [<transform obj>/]<transform attr>[, [<isnull obj>/]<isnull attr>[, <open>[, <close>]]]]])`

This function is used to format speech-like constructs, and is capable of transforming text within a speech string; it is useful for implementing "language code" and the like.

If `<speaker>` begins with &, the rest of the `<speaker>` string is treated as the speaker's name, so you can use it for NPCs or tacking on titles (such as with @chatformat). Otherwise, the name of the object `<speaker>` is used.

When only `<speaker>` and `<string>` are given, this function formats `<string>` as if it were speech from `<speaker>`, as follows.

If `<string>` is... the resulting string is...

*   **:<pose>**: `<speaker's name> <pose>`
*   **;<pose>**: `<speaker's name><pose>`
*   **|<emit>**: `<emit>`
*   **<speech>**: `<speaker's name> says, "<speech>"`

The chat_strip_quote config option affects this function, so if `<speech>` starts with a leading double quote ("), it may be stripped.

If `<say string>` is specified, it is used instead of "says,".

Continued in [help speak2].

