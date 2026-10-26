#!/bin/bash
# SharpMUSH StackScript for Akamai Cloud (Linode). Publish it as a public StackScript for Ubuntu 24.04
# and Debian 12; the UDF lines below become the deploy form, and each answer reaches this script as
# the environment variable of the same name, which is what install.sh reads. Linode keeps the answers
# with the server, so backup keys are not asked here; they are added after launch over SSH.
#
# <UDF name="SHARPMUSH_DOMAIN" label="Domain for the web portal" example="mush.example.com. Leave empty to start on <ip>.sslip.io." default="" />
# <UDF name="CLOUDFLARE_API_TOKEN" label="Cloudflare API token (optional)" example="Sets up the tunnel and DNS for the domain. Permissions: Account > Cloudflare Tunnel > Edit, Zone > DNS > Edit, Zone > Zone > Read." default="" />
# <UDF name="CLOUDFLARE_TUNNEL_TOKEN" label="Existing Cloudflare tunnel token (optional)" example="Instead of an API token. Point the tunnel's hostname at http://caddy:80." default="" />
# <UDF name="SHARPMUSH_SOURCE_REF" label="SharpMUSH installer version (git ref)" default="main" />
set -euo pipefail
exec > >(tee -a /var/log/stackscript.log) 2>&1

export SHARPMUSH_DOMAIN CLOUDFLARE_API_TOKEN CLOUDFLARE_TUNNEL_TOKEN SHARPMUSH_SOURCE_REF

curl -fsSL "https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/${SHARPMUSH_SOURCE_REF:-main}/deploy/oneclick/install.sh" \
	-o /root/sharpmush-install.sh
bash /root/sharpmush-install.sh
rm -f /root/sharpmush-install.sh
