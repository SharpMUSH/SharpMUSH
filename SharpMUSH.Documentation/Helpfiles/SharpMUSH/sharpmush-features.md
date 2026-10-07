<!-- help-article
{
  "corpus": "help",
  "id": "sharpmush-features",
  "lookup": "sharpmush features",
  "aliases": ["features"],
  "sections": [
    {
      "id": "features-accounts",
      "heading": "Accounts and the web portal",
      "lookup": "sharpmush features accounts"
    },
    {
      "id": "features-roles",
      "heading": "Roles and permissions",
      "lookup": "sharpmush features roles"
    },
    {
      "id": "features-wiki",
      "heading": "The wiki",
      "lookup": "sharpmush features wiki"
    },
    {
      "id": "features-softcode",
      "heading": "Softcode",
      "lookup": "sharpmush features softcode"
    },
    {
      "id": "features-clients",
      "heading": "Clients, pictures and sound",
      "lookup": "sharpmush features clients"
    },
    {
      "id": "features-administration",
      "heading": "Running the game",
      "lookup": "sharpmush features administration"
    }
  ]
}
-->
# SharpMUSH Features

SharpMUSH plays PennMUSH softcode and imports PennMUSH databases, and adds the systems below. Each
line names the help topic that explains it. For where SharpMUSH still differs from PennMUSH, see
[pennmush compatibility].

## Accounts and the web portal

- Players can hold a web-portal account with several characters linked to it. At the login
  screen, [register] and [login] reach the account, and [make] and [play] create and connect its
  characters. Wizards manage accounts with [@account].
- [@locale] sets the language the server addresses you in.

## Roles and permissions

- WIZARD, ROYALTY and the powers are [roles], held by characters, objects and accounts. Games can
  define their own roles and permissions with [@role] and [@permission], and test them with
  [ROLES()], [HASROLE()] and [PERMISSION()].
- [administrative capabilities] lists the scopes that guard snapshots, jobs, queues and the
  reality layers. [security] gives an overview of how access is decided.
- The `approved` role and [ISAPPROVED()] mark characters that have met the game's own bar.

## The wiki

- The game has a wiki that the web portal serves and softcode can read. See [wiki] and
  [Wiki functions].

## Softcode

- Every command has an output, which the next command in the same action list reads as `%>`.
  See [command output]. [piping] hands what a command printed to the next command as `%|`.
- Each command runs under a time limit, its [execution budget], and [queue budgets] limit how
  much work may wait in the queue. [@queue] inspects and controls queued work.
- [restrictedexpr()] evaluates code limited to the functions you allow. [localfun] gives each
  owner their own named functions.
- [@map] runs an attribute once for each element of a list, and [@input] passes each line a
  player types to an attribute until the session ends.
- Every new game is seeded with handler objects for [EVENTS] and [http] requests, so softcode
  can answer them without any setup.
- [JSON FUNCTIONS], [rendermarkdown()] and [~] (strict argument parsing) round out the toolset.

## Clients, pictures and sound

- [MEDIA FUNCTIONS] write a sound, picture or pane once, and each client gets it in its own form:
  MXP, Pueblo, the web portal or plain text.
- [IMAGE ATTRIBUTES] name the pictures the portal shows for characters, rooms, exits and things.
- [CMDLINK()] writes a clickable command for every client that has one.

## Running the game

- [recurring jobs] run an attribute on a schedule. [object snapshots] recover mistakes on a
  room, exit or code object without restoring the whole world.
- [@reality] sets reality layers, so objects in one room can be present to some viewers and not
  to others.
- [@backup] copies the live world, [@storage] reports its disk use, and [@package] turns objects
  into installable softcode packages.
- [@profile] records which functions and commands run, and [@ps/history] lists recent queue
  outcomes.
- An internal error is reported as an [exception] with an id the server log can be searched for.

::: seealso
- [pennmush compatibility]
- [Getting Started]
- [topics]
:::
