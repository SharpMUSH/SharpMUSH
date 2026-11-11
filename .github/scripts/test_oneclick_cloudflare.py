"""Offline installer regressions. Every external/system action is stubbed."""
import base64
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / 'deploy/oneclick/install.sh').read_text().split('\ncase "${1:-install}" in')[0]
NAME = 'sharpmush-mush.example.test-deadbeef'
INGRESS = [{'hostname': 'mush.example.test', 'service': 'http://caddy:80'}, {'service': 'http_status:404'}]
STUBS = r'''
head() { echo deadbeef; }
curl() { echo 'Unexpected network call' >&2; exit 91; }
docker() { exit 92; }
systemctl() { exit 93; }
apt-get() { exit 94; }
public_ip() { echo 192.0.2.1; }
cf_api() {
 printf '%s %s %s\n' "$1" "$2" "${3:-}" >> "$CALLS"
 case "$1 $2" in
 'GET /accounts/account/cfd_tunnel/existing') echo "$INFO" ;;
 'GET /accounts/account/cfd_tunnel/existing/configurations') echo "$CONFIG" ;;
 'GET /accounts/account/cfd_tunnel?name='*) echo "$LOOKUP" ;;
 'POST /accounts/account/cfd_tunnel') echo '{"result":{"id":"created"}}' ;;
 GET*/token) echo '{"result":"synthetic"}' ;;
 *) echo '{"result":[]}' ;;
 esac
}
cf_try() {
 case "$2" in
 /zones?name=*) echo '{"result":[{"id":"zone","account":{"id":"account"}}]}' ;;
 /zones/zone/dns_records\?name=mush.example.test\&type=CNAME) echo '{"result":[{"content":"old.cfargotunnel.com"}]}' ;;
 /accounts/account/cfd_tunnel/old) echo '{"result":{"name":"shared-old"}}' ;;
 *) echo '{"success":true,"result":[]}' ;;
 esac
}
'''


class CloudflareInstallerTests(unittest.TestCase):
    def run_setup(self, token=True, name=NAME, ingress=None, lookup=True, deleted=None, action='cloudflare_setup'):
        with tempfile.TemporaryDirectory() as d:
            calls = Path(d) / 'calls'
            calls.touch()
            env = dict(os.environ, SHARPMUSH_DOMAIN='mush.example.test', CLOUDFLARE_API_TOKEN='synthetic',
                       CLOUDFLARE_TUNNEL_TOKEN=base64.b64encode(b'{"t":"existing"}').decode() if token else '',
                       CF_ZONE_ID='zone', CF_ACCOUNT_ID='account', CALLS=str(calls),
                       INFO=json.dumps({'result': {'id': 'existing', 'name': name, 'deleted_at': deleted}}),
                       CONFIG=json.dumps({'result': {'config': {'ingress': INGRESS if ingress is None else ingress}}}),
                       LOOKUP=json.dumps({'result': [{'id': 'existing'}] if lookup else []}))
            # Only function definitions are sourced; the install command dispatcher is excluded.
            # Synthetic id satisfies the script's root check without privilege elevation.
            script = 'id() { echo 0; }\n' + SOURCE + '\n' + STUBS + '\n' + action
            result = subprocess.run(['bash', '-c', script], env=env, capture_output=True, text=True, timeout=10)
            return result, calls.read_text()

    def assert_no_writes(self, calls):
        self.assertFalse(any(line.startswith(('PUT ', 'POST ', 'DELETE ')) for line in calls.splitlines()), calls)

    def test_shared_token_rejected_before_writes(self):
        result, calls = self.run_setup(name='other-server')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('does not identify this machine-named tunnel', result.stderr)
        self.assert_no_writes(calls)

    def test_changed_domain_rejected_before_writes(self):
        result, calls = self.run_setup(name='sharpmush-old.example.test-deadbeef')
        self.assertNotEqual(result.returncode, 0)
        self.assert_no_writes(calls)

    def test_deleted_token_rejected_before_writes(self):
        result, calls = self.run_setup(deleted='2026-11-01')
        self.assertNotEqual(result.returncode, 0)
        self.assert_no_writes(calls)

    def test_shared_ingress_rejected_before_writes(self):
        result, calls = self.run_setup(ingress=[{'hostname': 'other.example.test', 'service': 'http://other:80'}] + INGRESS)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('different ingress', result.stderr)
        self.assert_no_writes(calls)

    def test_named_lookup_also_protects_ingress(self):
        result, calls = self.run_setup(token=False, ingress=[])
        self.assertNotEqual(result.returncode, 0)
        self.assert_no_writes(calls)

    def test_rerun_does_not_rewrite_existing_configuration(self):
        result, calls = self.run_setup()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn('PUT /accounts/', calls)
        self.assertNotIn('POST /accounts/', calls)
        self.assertIn('POST /zones/', calls)

    def test_fresh_install_creates_and_configures_tunnel(self):
        result, calls = self.run_setup(token=False, lookup=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('POST /accounts/account/cfd_tunnel ', calls)
        self.assertIn('PUT /accounts/account/cfd_tunnel/created/configurations ', calls)

    def test_old_tunnel_note_is_conditional(self):
        result, _ = self.run_setup(action='cloudflare_old_tunnel zone account new; echo "$OLD_TUNNEL_NOTE"')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('may still serve other hostnames or routes', result.stdout)
        self.assertIn('Check all remaining uses', result.stdout)
        self.assertNotIn('now gets no traffic', result.stdout)

    def test_read_preflight_does_not_attempt_writes(self):
        result, calls = self.run_setup(action='cloudflare_check')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_no_writes(calls)


if __name__ == '__main__':
    unittest.main()
