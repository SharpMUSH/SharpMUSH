# COMMANDS

Help is available for the following MUSH commands:
 
- ahelp
- anews
- brief
- DOING
- drop
- examine
- enter
- events
- follow
- get
- give
- go
- index
- leave
- LOGOUT
- look
- move
- news
- page
- pose
- QUIT
- read
- rules
- say
- score
- teach
- think
- unfollow
- use
- whisper
- WHO
- with
- "
- :
- ;
- +
- ]
 
In addition to these, there are several types of '@' commands. @-commands are usually commands which have permanent effects on the MUSH (such as creating a new object). Here are the help topics on @-commands:
 
- @-ATTRIBUTES
- @-BUILDING
- @-GENERAL
- @-WIZARD
 
Commands that can only be used by connected players are listed in [help SOCKET COMMANDS|SOCKET COMMANDS].

# @-ATTRIBUTES

These '@' commands set standard message/action sets on objects. Each comes in 3 versions: @\<whatever\>, @o\<whatever\>, and @a\<whatever\>. Only the @\<whatever\> version is listed below, but help is available for each:
 
- @describe
- @drop
- @efail
- @enter
- @failure
- @follow
- @give
- @idescribe
- @leave
- @lfail
- @move
- @payment
- @receive
- @success
- @tport
- @ufail
- @unfollow
- @use
- @zenter
- @zleave

These '@' command set other standard attributes on objects that don't follow the pattern above:

- @aahear
- @aclone
- @aconnect
- @adisconnect
- @amail
- @amhear
- @away
- @charges
- @conformat
- @cost
- @descformat
- @ealias
- @exitformat
- @filter
- @forwardlist
- @haven
- @idescformat
- @idle
- @infilter
- @inprefix
- @lalias
- @listen
- @nameformat
- @oxenter
- @oxleave
- @oxmove
- @oxtport
- @prefix
- @runout
- @sex
- @startup

## See Also
- [help ATTRIBUTES|ATTRIBUTES]
- [help NON-STANDARD ATTRIBUTES|NON-STANDARD ATTRIBUTES]

# @-BUILDING

These '@' commands are building-related (they create or modify objects):
 
- @atrlock
- @atrchown
- @chown
- @chzone
- @clone
- @cpattr
- @create
- @destroy
- @dig
- @elock
- @eunlock
- @firstexit
- @link
- @lock
- @moniker
- @mvattr
- @name
- @nuke
- @open
- @parent
- @recycle
- @set
- @undestroy
- @ulock
- @unlink
- @unlock
- @uunlock
- @wipe
  
# @-GENERAL

These '@' commands are general utility and programming commands:

- @@
- @alias
- @break
- @channel
- @chat
- @cemit
- @command
- @config
- @decompile
- @doing
- @dolist
- @drain
- @edit
- @emit
- @entrances
- @find
- @force
- @function
- @gedit
- @grep
- @halt
- @if
- @lemit
- @listmotd
- @mail
- @notify
- @nsemit
- @nslemit
- @nsoemit
- @nspemit
- @nsprompt
- @nsremit
- @nszemit
- @oemit
- @password
- @pemit
- @prompt
- @ps
- @remit
- @restart
- @scan
- @search
- @select
- @stats
- @sweep
- @switch
- @teleport
- @trigger
- @verb
- @version
- @wait
- @whereis
- @zemit

# @-WIZARD

These '@' commands are only usable by wizards or privileged players:
 
- @allhalt
- @allquota
- @boot
- @chownall
- @chzoneall
- @comment
- @dbck
- @disable
- @dump
- @enable
- @flag
- @hide
- @hook
- @http
- @kick
- @log
- @motd
- @newpassword
- @pcreate
- @poll
- @poor
- @power
- @purge
- @quota
- @readcache
- @respond
- @rejectmotd
- @shutdown
- @sitelock
- @sql
- @squota
- @suggest
- @uptime
- @wall
- @wizmotd
- @wizwall
- cd
- ch
- cv
 
# ]

"]" is a special prefix which can be used before any command. It instructs the MUSH that it shouldn't evaluate the arguments to the command (similar to the "/noeval" switch available on some commands). For example:
