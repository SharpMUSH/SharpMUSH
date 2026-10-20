<!-- help-article
{
  "corpus": "help",
  "id": "config-limits-command",
  "lookup": "@config limits",
  "aliases": [],
  "sections": [
    {
      "id": "communication-and-economy-limits",
      "heading": "Communication and economy limits",
      "lookup": "@config limits communication and economy limits"
    },
    {
      "id": "evaluation-and-queue-limits",
      "heading": "Evaluation and queue limits",
      "lookup": "@config limits evaluation and queue limits"
    },
    {
      "id": "connection-and-alias-limits",
      "heading": "Connection and alias limits",
      "lookup": "@config limits connection and alias limits"
    }
  ],
  "redirects": {
    "@config limits2": "@config limits communication and economy limits",
    "@config limits3": "@config limits evaluation and queue limits",
    "@config limits4": "@config limits connection and alias limits"
  }
}
-->
# @config limits

Limits and other constants.

- `max_dbref=<dbref>`: The highest dbref an object can have. If 0, there is no limit on database size.
- `max_attrs_per_obj=<number>`: The maximum attributes an object can have.
- `max_logins=<number>`: The maximum number of connected players.
- `max_guests=<number>`: The maximum number of connected guests. If 0, no limit. If -1, limited by the number of guest players in the db.
- `max_named_qregs=<number>`: The maximum number of qregs except for a-z and 0-9. The limit is per-localize()-call.
- `connect_fail_limit=<count>`: The number of failed connection attempts before an IP is temporarily banned, from 1 to 100. The server does not enforce it yet.
- `idle_timeout=<time>`: The number of seconds a player can be idle before getting booted, from 0 to 86400. 0 means no limit.
- `unconnected_idle_timeout=<time>`: The number of seconds a connection can sit at the login screen before getting booted, from 10 to 3600. The server does not enforce it yet.

## Communication and economy limits

- `whisper_loudness=<number>`: The percentage chance of a whisper/noisy being heard.
- `starting_quota=<number>`: How much quota new players get.
- `starting_money=<number>`: How many pennies new players get.
- `paycheck=<number>`: How many pennies players get each day they log on.
- `max_pennies=<number>`: The maximum pennies an object can have.
- `mail_limit=<number>`: How many @mail messages the folder new mail arrives in may hold. New mail is refused when that folder is full. Messages filed into other folders do not count, so this is not a limit on all the mail a player keeps. Mail stays until it is deleted and purged.
- `max_depth=<number>`: How deep indirect @lock chains can go.
- `player_queue_limit=<number>`: The number of commands a player can have queued at once.
- `queue_loss=<number>`: One in <number> times, queuing a command will cost an extra penny that doesn't get refunded.
- `queue_chunk=<number>`: How many queued commands get executed in a row before checking for network activity.

## Evaluation and queue limits

- `function_recursion_limit=<number>`: The depth to which softcode functions can call more functions.
- `function_invocation_limit=<number>`: The maximum number of softcode functions that can be called in one command.
- `guest_paycheck=<number>`: How many pennies guests get each day.
- `max_guest_pennies=<number>`: The maximum pennies a guest can have.
- `player_name_len=<number>`: The maximum length of a player name.
- `queue_entry_cpu_time=<number>`: The maximum number of milliseconds a queue entry can take to run.
- `use_quota=<boolean>`: Controls if quotas are used to limit the number of objects a player can own.

## Connection and alias limits

- `max_aliases=<number>`: The maximum number of aliases a player can have.
- `max_gallery_images=<number>`: The most images one character's profile gallery may hold. An upload past it is refused.
- `push_idle_seconds=<number>`: Seconds without input after which `@push` treats a player as away, unless a portal tab of theirs is in view (see [@push]).
- `push_limit=<number>`: The most push notifications `@push` sends one player in a minute.
- `push_devices=<number>`: The most browsers one account may turn push notifications on in.
- `keepalive_timeout=<time>`: How often should an 'Are you still there?' query be sent to clients, to stop players' routers booting idle connections?
- `max_parents=<number>`: The maximum number of levels of parenting allowed.
- `call_limit=<number>`: The maximum number of times the parser can be called recursively for any one expression.
- `chunk_migrate=<number>`: Maximum number of attributes that can be moved to disk cache per second.
