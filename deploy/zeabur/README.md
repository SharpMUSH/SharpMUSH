# SharpMUSH on Zeabur

`template.yaml` runs a game on Zeabur as three services, one per image, the same layout as Railway
(`../railway/`): the edge (`sharpmush/sharpmush-edge`; Caddy, NATS and the connection server), the
game server (`sharpmush/sharpmush-server`) and the renderer (`sharpmush/sharpmush-renderer`). The
edge holds every player's socket, so updating the server or the renderer drops nobody.

## Publishing the template

A deploy button belongs to the account that created the template. With the
[Zeabur CLI](https://zeabur.com/docs/en-US/developer/cli):

```bash
npx zeabur@latest auth login
npx zeabur@latest template deploy -f deploy/zeabur/template.yaml   # try it in a new project
npx zeabur@latest template create -f deploy/zeabur/template.yaml   # publish it
```

The template's code is the end of its `zeabur.com/templates/<code>` address. After a
change to this file, update the published template with
`npx zeabur@latest template update -c <code> -f deploy/zeabur/template.yaml`; projects already
deployed keep what they were created with. The button's code is under the template's share button
in the dashboard's Template tab.

## How the services find each other

Each service is reached on the project's private network at `<service>.zeabur.internal`, by the
name it has in the template: `edge`, `server` and `renderer`. The names are written out rather than
exposed from one service to another because the edge and the server each need the other's address.

| Service | Ports | Volume |
|---|---|---|
| `edge` | `web` 8000 (the portal, on the domain), `telnet` 4201 (forwarded), `nats` 4222 | `/data`: NATS's store, the setup guard's state |
| `server` | `game` 8080 | `/data`: the world, its backups, the wiki's files |
| `renderer` | `render` 5100 | none |

Zeabur gives every TCP port a public forwarded address. NATS, the server and the renderer ask for no
credentials, so their ports are declared as HTTP, which Zeabur exposes only through a domain, and
none is bound to them. Only `web` and `telnet` are public.

The setup link is in the edge's log and uses the bound domain (`ZEABUR_WEB_DOMAIN`). Players connect
to telnet at the edge's forwarded host and port, shown in its instructions; Zeabur picks both, and
says they can change in rare cases.

## Updates

The services follow each image's `dev` tag. Redeploying a service pulls the newest build; nothing
redeploys on its own. Take an `@backup` first:

- `server` and `renderer`: redeploy whenever. Players stay connected through the restart.
- `edge`: every redeploy drops everyone connected, so announce a time first.
