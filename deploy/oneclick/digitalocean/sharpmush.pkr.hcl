# Builds the SharpMUSH DigitalOcean Marketplace (1-Click) image.
#
#   export DIGITALOCEAN_TOKEN=...
#   packer init deploy/oneclick/digitalocean
#   packer build deploy/oneclick/digitalocean
#
# The image carries Docker, the stack's files and its pulled images, and starts nothing. Each new
# Droplet configures itself on first boot (files/var/lib/cloud/scripts/per-instance/001_sharpmush)
# and asks its owner for a domain at the first SSH login (files/opt/sharpmush/bin/first-login).
packer {
  required_plugins {
    digitalocean = {
      version = ">= 1.4.0"
      source  = "github.com/digitalocean/digitalocean"
    }
  }
}

variable "do_token" {
  type      = string
  default   = env("DIGITALOCEAN_TOKEN")
  sensitive = true
}

variable "image_tag" {
  type        = string
  default     = "dev"
  description = "SharpMUSH image tag the snapshot pulls and runs."
}

variable "region" {
  type    = string
  default = "nyc3"
}

locals {
  snapshot_name = "sharpmush-${var.image_tag}-${formatdate("YYYYMMDD-hhmm", timestamp())}"
}

source "digitalocean" "sharpmush" {
  api_token = var.do_token
  image     = "ubuntu-24-04-x64"
  region    = var.region
  # The Marketplace guidelines build on the smallest Droplet so every size can use the image. The
  # listing names the size players should pick (4 GB; 2 GB works).
  size          = "s-1vcpu-1gb"
  ssh_username  = "root"
  snapshot_name = local.snapshot_name
}

build {
  sources = ["source.digitalocean.sharpmush"]

  provisioner "shell" {
    inline = ["cloud-init status --wait"]
  }

  provisioner "shell" {
    inline = ["mkdir -p /tmp/sharpmush-oneclick"]
  }

  provisioner "file" {
    sources = [
      "${path.root}/../compose.yml",
      "${path.root}/../Caddyfile",
      "${path.root}/../install.sh",
      "${path.root}/../sharpmush-guard",
      "${path.root}/../sharpmush-telnet-tls-sync",
      "${path.root}/../../../nats.conf",
    ]
    destination = "/tmp/sharpmush-oneclick/"
  }

  provisioner "shell" {
    environment_vars = ["DEBIAN_FRONTEND=noninteractive", "SHARPMUSH_IMAGE_TAG=${var.image_tag}"]
    inline = [
      "apt-get -qy update",
      "apt-get -qy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' full-upgrade",
      "bash /tmp/sharpmush-oneclick/install.sh prepare",
      # The tag the image was built for, read by the first boot.
      "echo 'SHARPMUSH_IMAGE_TAG=${var.image_tag}' > /opt/sharpmush/state/image.env",
      "rm -rf /tmp/sharpmush-oneclick",
    ]
  }

  provisioner "file" {
    source      = "${path.root}/files/"
    destination = "/"
  }

  provisioner "shell" {
    inline = [
      "chmod 755 /var/lib/cloud/scripts/per-instance/001_sharpmush /opt/sharpmush/bin/first-login",
      # Keep root out until the first boot has configured the stack; 001_sharpmush removes this.
      "printf 'Match User root\\n        ForceCommand echo \"Please wait while SharpMUSH starts, then log in again...\"\\n' >> /etc/ssh/sshd_config",
      "echo '/opt/sharpmush/bin/first-login' >> /root/.bashrc",
      # DigitalOcean's own cleanup and image check, as the Marketplace requires.
      "curl -fsSL https://raw.githubusercontent.com/digitalocean/marketplace-partners/master/scripts/90-cleanup.sh -o /tmp/90-cleanup.sh",
      "curl -fsSL https://raw.githubusercontent.com/digitalocean/marketplace-partners/master/scripts/99-img-check.sh -o /tmp/99-img-check.sh",
      "bash /tmp/90-cleanup.sh",
      "bash /tmp/99-img-check.sh",
      "rm -f /tmp/90-cleanup.sh /tmp/99-img-check.sh",
    ]
  }
}
