# One-click SharpMUSH servers

These files give a new server owner a running game without editing a compose file. They all end in
the same installer, `install.sh`, which runs the stack in `compose.yml` from published images.

| Launcher | What the owner does | Files |
|---|---|---|
| Any VPS | Pastes `cloud-init.yaml` into the provider's user-data box, or runs the installer over SSH | `cloud-init.yaml`, `install.sh` |
| Akamai / Linode | Deploys the public StackScript and fills in its form | `linode-stackscript.sh` |
| DigitalOcean | Creates a Droplet from the 1-Click image | `digitalocean/` |
| Fly.io | Runs `deploy/fly/launch.sh <app>`, which needs no checkout | `../fly/` (see its README) |
| Railway | Deploys the template | `../railway/` (see its README) |

Over SSH:

```bash
curl -fsSL https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/oneclick/install.sh | sudo bash
```

It needs Ubuntu 24.04 or later, or Debian 12, with 2 GB of memory (4 GB is comfortable).

## What the installer does

1. Installs Docker (from get.docker.com), `jq` and `ufw`, and allows SSH, 80, 443 and 4201 through
   the firewall.
2. Writes the stack to `/opt/sharpmush`: `compose.yml`, `Caddyfile`, `nats.conf`, and `.env` from the
   answers. It also installs `sharpmush.service`, which brings the stack up on boot.
3. Picks the web front door:
   - **Let's Encrypt** (the default). Caddy takes 80/443 for `SHARPMUSH_DOMAIN`. Point the domain's
     A record at the server first. With no domain, the game uses `<ip>.sslip.io`, which needs no DNS.
     sslip.io is not on the Public Suffix List, so every sslip.io user shares one Let's Encrypt
     limit and a certificate can be refused when it is busy. Give a domain when you have one.
   - **Cloudflare Tunnel.** Give `CLOUDFLARE_API_TOKEN` with the domain, and the installer creates
     the tunnel, routes the domain to it and adds a DNS-only `telnet.<domain>` record for the
     server's address. The token needs Account > Cloudflare Tunnel > Edit, Zone > DNS > Edit and
     Zone > Zone > Read, is used once and is not stored. An existing tunnel works too: give
     `CLOUDFLARE_TUNNEL_TOKEN` and point the tunnel's public hostname at `http://caddy:80`. In this
     mode nothing web-facing is published on the server.
4. Arms the first-boot guard (below), starts the stack and prints the addresses and the setup link.
   The same text is shown at every SSH login while the guard holds.

Run `sharpmush-configure` later to change the domain or the Cloudflare settings. It asks each
question again, with the current answer in brackets. `install.sh` lists every variable it reads.

## The first-boot guard

A new server is on a public address within minutes, and `/setup` gives the admin account to
whoever reaches it first. Until the owner finishes `/setup`:

- the portal answers only a browser that opened the setup link
  (`https://<domain>/?setup_code=<code>`, which sets a cookie). Everyone else gets a 403 page.
  `/health` stays open for health checks.
- telnet is published on `127.0.0.1` only.

`sharpmush-guard.timer` asks the game every 15 seconds whether setup is finished. When it is, the
guard clears `guard/guard.caddy`, reloads Caddy, publishes telnet on every address and turns the
timer off. `SHARPMUSH_GUARD=off` skips the guard.

## Testing a launcher

- **cloud-init.** Create an Ubuntu 24.04 server with 2 GB or more and paste `cloud-init.yaml` as
  user data. To test a branch, set `SHARPMUSH_SOURCE_REF` to it. The install log is
  `/var/log/sharpmush-install.log`.
- **Linode.** In Cloud Manager, StackScripts > Create StackScript. Paste `linode-stackscript.sh` and
  choose Ubuntu 24.04 and Debian 12. Deploy it to a Shared 4 GB Linode, with the installer version
  field set to the branch under test. The log is `/var/log/stackscript.log`.
- **DigitalOcean.** `packer init deploy/oneclick/digitalocean` then
  `DIGITALOCEAN_TOKEN=... packer build deploy/oneclick/digitalocean`. Create a Droplet from the
  snapshot it leaves under Images > Snapshots. SSH refuses root until the first boot has started the
  game (about a minute). The first login shows the setup link and offers to set a domain. The
  first-boot log is `/var/log/sharpmush-firstboot.log`.

## Publishing

- **Linode.** Make the StackScript public; its page is the "Deploy" link. A Marketplace listing
  starts with Akamai's partner registration form and a review of the StackScript on GitHub.
- **DigitalOcean.** Submit the snapshot in the Marketplace Vendor Portal. Name 4 GB as the
  recommended size (2 GB works). The build already runs DigitalOcean's `90-cleanup.sh` and
  `99-img-check.sh`. Rebuild and resubmit for each release.
- **Image tag.** Until v1.0.0 is tagged, everything runs `:dev`. Set `SHARPMUSH_IMAGE_TAG` (the
  Packer variable `image_tag`) to a release tag for anything published.
