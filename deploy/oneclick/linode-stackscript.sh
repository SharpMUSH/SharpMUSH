#!/bin/bash
# SharpMUSH StackScript for Akamai Cloud (Linode). Publish it as a public StackScript for Ubuntu 24.04
# and Debian 12; the UDF lines below become the deploy form, and each answer reaches this script as
# the environment variable of the same name, which is what install.sh reads.
#
# <UDF name="SHARPMUSH_DOMAIN" label="Domain for the web portal" example="mush.example.com. Leave empty to start on <ip>.sslip.io." default="" />
# <UDF name="CLOUDFLARE_API_TOKEN" label="Cloudflare API token (optional)" example="Sets up the tunnel and DNS for the domain. Permissions: Account > Cloudflare Tunnel > Edit, Zone > DNS > Edit, Zone > Zone > Read." default="" />
# <UDF name="CLOUDFLARE_TUNNEL_TOKEN" label="Existing Cloudflare tunnel token (optional)" example="Instead of an API token. Point the tunnel's hostname at http://caddy:80." default="" />
# <UDF name="RESTIC_REPOSITORY" label="Backup repository (optional)" example="b2:bucket:sharpmush or s3:https://<account>.r2.cloudflarestorage.com/bucket" default="" />
# <UDF name="RESTIC_PASSWORD" label="Backup encryption password" example="Keep a copy: without it the backups cannot be read." default="" />
# <UDF name="B2_ACCOUNT_ID" label="Backblaze B2 key ID" default="" />
# <UDF name="B2_ACCOUNT_KEY" label="Backblaze B2 application key" default="" />
# <UDF name="AWS_ACCESS_KEY_ID" label="S3 or R2 access key ID" default="" />
# <UDF name="AWS_SECRET_ACCESS_KEY" label="S3 or R2 secret access key" default="" />
# <UDF name="SHARPMUSH_SOURCE_REF" label="SharpMUSH installer version (git ref)" default="main" />
set -euo pipefail
exec > >(tee -a /var/log/stackscript.log) 2>&1

export SHARPMUSH_DOMAIN CLOUDFLARE_API_TOKEN CLOUDFLARE_TUNNEL_TOKEN RESTIC_REPOSITORY RESTIC_PASSWORD \
	B2_ACCOUNT_ID B2_ACCOUNT_KEY AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY SHARPMUSH_SOURCE_REF

curl -fsSL "https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/${SHARPMUSH_SOURCE_REF:-main}/deploy/oneclick/install.sh" \
	-o /root/sharpmush-install.sh
bash /root/sharpmush-install.sh
rm -f /root/sharpmush-install.sh
