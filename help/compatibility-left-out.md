<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-left-out",
  "lookup": "compatibility left out",
  "aliases": [],
  "sections": [
    {
      "id": "rhostmush-functions-left-out",
      "heading": "RhostMUSH functions left out",
      "lookup": "compatibility left out rhostmush functions"
    },
    {
      "id": "rhostmush-clusters-left-out",
      "heading": "RhostMUSH clusters left out",
      "lookup": "compatibility left out rhostmush clusters"
    },
    {
      "id": "rhostmush-functions-little-used",
      "heading": "RhostMUSH functions little used",
      "lookup": "compatibility left out rhostmush little used"
    },
    {
      "id": "rhostmush-commands-left-out",
      "heading": "RhostMUSH commands left out",
      "lookup": "compatibility left out rhostmush commands"
    },
    {
      "id": "tinymux-functions-and-commands-left-out",
      "heading": "TinyMUX functions and commands left out",
      "lookup": "compatibility left out tinymux"
    }
  ]
}
-->
# Compatibility Left Out

SharpMUSH leaves out some RhostMUSH and TinyMUX functions and commands on purpose. Imported code that calls one of the functions gets `#-1 FUNCTION (<NAME>) NOT FOUND`, and one of the commands gets `Huh?`. The import report names each use and points here.

- [compatibility left out rhostmush functions]: accounts, passwords, host access and engine internals
- [compatibility left out rhostmush clusters]: `cluster_*()` and `@cluster`
- [compatibility left out rhostmush little used]: functions no shipped code needs
- [compatibility left out rhostmush commands]: database upkeep, RWHO and doors
- [compatibility left out tinymux]: SQL rowsets, `@cron` and `@lua`

```sharp dialect=rhost
> think [execscript(backup.sh)]
#-1 FUNCTION (EXECSCRIPT) NOT FOUND
```

## RhostMUSH functions left out

- `account_boot()`, `account_login()`, `account_owner()`, `account_su()`, `account_who()`: SharpMUSH has accounts of its own; see `account()` and `@account`.
- `attrpass()`, `genpassword()`, `passwd()`: a password is never an attribute or a softcode value.
- `isunicode()`, `isutf8()`: text is always Unicode.
- `execscript()`, `execscriptnr()`, `logtofile()`, `stderr()`, `nslookup()`, `ipv4math()`: they reach the host. Use `@http` or a plugin.
- `sqlite_query()`, `sqlon()`, `sqloff()`, `sqlping()`: `sql()` runs queries, and the server keeps the connection.
- `crc32obj()`, `chkgarbage()`, `safebuff()`, `limits()`, `logstatus()`, `countspecial()`: they report RhostMUSH's own buffers, logs and storage.
- `ruler()`, `runintz()`, `listtzones()`, `secstz()`, `msecstz()`: no time zone design yet.
- `guild()`, `race()`: write `get(<player>/GUILD)` and `get(<player>/RACE)`.
- `shift()`, `args()`, `passthrough()`: `%10` and up, and `uargs()`, reach every argument.

## RhostMUSH clusters left out

A cluster spreads one object's attributes over several objects to get past RhostMUSH's limits. An object here holds up to 100,000 attributes, so neither the functions nor `@cluster` are needed.

- `iscluster()`, `cluster_add()`, `cluster_attrcnt()`, `cluster_default()`, `cluster_edefault()`, `cluster_flags()`, `cluster_get()`, `cluster_get_eval()`, `cluster_grep()`, `cluster_hasattr()`, `cluster_hasflag()`, `cluster_lattr()`, `cluster_regrep()`, `cluster_regrepi()`, `cluster_set()`, `cluster_stats()`, `cluster_u()`, `cluster_u2()`, `cluster_u2default()`, `cluster_u2ldefault()`, `cluster_u2local()`, `cluster_udefault()`, `cluster_ueval()`, `cluster_uldefault()`, `cluster_ulocal()`, `cluster_vattrcnt()`, `cluster_wipe()`, `cluster_xget()`

## RhostMUSH functions little used

Little used in shipped RhostMUSH code. One is added when a game needs it.

- `array()`, `template()`, `strmath()`, `packmath()`, `sortlist()`, `garble()`, `rotl()`, `rotr()`, `trreverse()`, `ee()`, `bucket()`, `lloc()`, `sees()`, `writable()`, `sweep()`, `quietnomatch()`, `regnummatch()`, `regnummatchi()`, `regeditlit()`, `regeditilit()`, `regeditalllit()`, `regeditallilit()`, `esclist()`, `unesclist()`, `editansi()`, `ansipos()`, `xorflag()`, `listnewsgroups()`, `listprotection()`, `inprogram()`, `programmer()`, `rset()`

## RhostMUSH commands left out

- `@mark`, `@mark_all`, `@apply_marked`, `@cut`, `@fixdb`, `@dbclean`, `@bfree`, `@fnd`, `@destroyall`, `@convert`, `@timewarp`: upkeep of RhostMUSH's database. `@backup` and `@storage` look after SharpMUSH's.
- `@crc32obj`, `@atrcache`, `@limit`, `@flaglevel`, `@protect`, `@money`, `@robot`: RhostMUSH's own administration.
- `@snoop`: watching another player's session.
- `@turtle`: `@nuke` and `@chownall` do the work.
- `slay`: TinyMUD money combat.
- `@rwho`, `@mudwho`, `rwho`: the RWHO protocol is gone.
- `head`, `post`, `put`: RhostMUSH's HTTP on the game port. SharpMUSH's HTTP handler takes web requests.
- `@door`: plugins and `@http` reach other services.
- `@sqlconnect`, `@sqldisconnect`: the server keeps the SQL connection.
- `@tor`: sitelock bans addresses.
- `@remote`: `@force` an object in that room.
- `@list_file`: `@listmotd` and the connect screen show the text files.
- `newsdb`, `+players`, `wielded`, `worn`: softcode does these.

## TinyMUX functions and commands left out

- `rserror()`, `rsnext()`, `rsprev()`, `rsrec()`, `rsrecnext()`, `rsrecprev()`, `rsrelease()`, `rsrows()`: SQL row sets. `sql()` returns every row at once.
- `setname()`, `setparent()`: write `@name` and `@parent`.
- `text()`: `textfile()` reads a text file.
- `format()`: `wrap()` and `align()` lay out text.
- `mailj()`: `mail()` and `mailinfo()` read a message.
- `@cron`, `@crondel`, `@crontab`: `@wait` and STARTUP queue timed work.
- `@lua`: plugins add code in other languages.
