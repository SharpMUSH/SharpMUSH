# Account sign-in providers

SharpMUSH can offer Discord, Google, Microsoft and Grapevine sign-in alongside existing account passwords and passkeys. Every provider starts disabled. These registrations are independent of Discord bot linking, Grapevine chat and their secrets.

Existing account-password login, direct character-password `connect`, character-password account login where retained, browser OTT, remembered sign-in and reconnect remain available. Migration retains existing character-password account access. New externally created accounts do not enable that fallback. Adding a provider never disables an existing method automatically.

## Configure a provider

Open **Administration → Configuration → Applications** and choose the provider's sign-in tile, or visit `/admin/config/signin/discord`, `/google`, `/microsoft` or `/grapevine` under that same prefix. Configuration requires the existing administrative configuration capability.

Register an application with the chosen provider. Copy the exact callback URL displayed in its SharpMUSH tile. Enter its client id and the game's public HTTPS origin, without a path, query or fragment. The callbacks are `/signin-external-discord`, `/signin-external-google`, `/signin-external-microsoft` and `/signin-external-grapevine` at that origin. Forwarded headers must already identify the trusted public origin/client IP as described in the deployment guide.

For Discord, Google and Microsoft, store the application's client secret in the existing **Secrets** page under `auth.discord.clientsecret`, `auth.google.clientsecret` or `auth.microsoft.clientsecret`. Secret values are never returned by the provider settings endpoint. Grapevine Login uses its documented public authorization-code exchange and does not require a separate client secret; do not reuse the Grapevine chat secret.

Google and Microsoft request `openid profile`; Discord requests `identify`; Grapevine requests `profile`. No email matching, bot scope, guild scope, Graph access, offline access or refresh-token storage is used. Microsoft supports `common`, `organizations`, `consumers` or a concrete tenant UUID, with an optional allowed-tenant list. Match the audience to the registration's supported account types.

Save and perform a controlled server restart after changing registration settings or secrets. Readiness reports missing/invalid configuration and restart requirements. Disabling a provider takes effect immediately. Changing a client id with linked identities is refused: provider subjects can be application-specific and require a separately reviewed identity migration. Disabling or changing trust settings requires acknowledgement when accounts are affected. Keep another working account method available before doing so.

Keep the world data and its Data Protection key ring together in backups. Provider secrets stored through **Secrets** are encrypted in world data; their keys live at `<world path>.dataprotection-keys` (normally `/app/data/lightning.dataprotection-keys`), or the directory selected by `SHARPMUSH_DATAPROTECTION_PATH`. Preserve access controls and restore the matching keys before enabling sign-in. Restoring the world alone cannot decrypt those stored secrets. In-flight external, recent-proof and native-secret records are process-local and do not survive restart or restore.

Protocol references: [Discord OAuth2](https://discord.com/developers/docs/topics/oauth2), [Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect), [Microsoft identity platform](https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols-oidc), and [Grapevine Login](https://grapevine.haus/docs). Grapevine's public exchange is also visible in its [official token controller](https://github.com/oestrich/grapevine/blob/master/apps/grapevine/lib/web/controllers/oauth/token_controller.ex).

## Sign in and link accounts

Ready providers appear on the existing sign-in page. A provider identity already linked here signs into that account. An unrecognized identity asks for a new username and explicit confirmation before creating an account. Cancelling creates nothing. A provider's email does not locate, merge or claim a SharpMUSH account.

To add an identity to an existing account, sign in using an existing method, open **My account → Sign-in methods**, verify a dedicated account method and choose **Link**. Complete the provider's sign-in ceremony. An identity attached to another account is refused; linking never moves characters or grants roles. Multiple identities from the same provider are supported.

A short-lived, one-action verification is required for security changes. It can use an account password, a registered passkey or a fresh ceremony for an already-linked external identity. A remembered browser login and an ordinary character password do not count as this verification. An account with only legacy character-password access has a separate explicit upgrade form that verifies an owned character and installs an account password without disabling character-password account login.

Removing the final configured usable account method is refused. Identities count only while their provider is enabled and ready. Provider outages are not proof that a registration has permanently ceased to work; add another provider, passkey or optional account password in advance.

## Native MU* clients

An external-only account does not need an account password to play. Create a character from its account roster, then use that character's native-credentials control to mint a one-use `nc1_` secret after account verification. Copy the displayed `connect` command to the MU* client. It expires after two minutes, is usable once and is bound to the exact character, its ownership and the issuing live account session. Never paste it into chat or a password manager as a permanent password.

Use a TLS-enabled MU* connection when available. Plain Telnet exposes the one-use secret to anyone able to observe that connection; its short expiry and single use do not encrypt the transport.

Alternatively, set a separate character password after account verification. That password works with direct character `connect`. It does not automatically enable character-password account login. Existing character passwords continue working.

Signing out or revoking the source session invalidates its pending native secrets. Credential or ownership changes also invalidate stale native authority. Issuance/redemption are rate limited and pending secret storage is bounded; raw native secrets and provider tokens are not stored in durable world records.

## Recovery and limits

Use another attached provider, passkey or local account password when a provider is unavailable. Character-password account fallback remains available only where explicitly retained. With no surviving method, contact staff: there is no email-only password reset or automatic identity attachment by email.

Staff recovery uses the existing moderation capability, recent proof of the staff account and persisted role hierarchy, including inactive targets. It establishes a temporary account password with a required change, revokes local sessions and live connections, and preserves the target's disabled/closed status. Ordinary moderators cannot recover peers, higher roles, the owner/God account or reserved system accounts. Recovery without a surviving owner credential is an authenticated operator/console procedure.

A provider's remote logout or revocation is checked on future provider ceremonies. It does not push an immediate revocation of already-issued SharpMUSH sessions. Local account disable/ban and session revocation remain authoritative.

Before enabling a registration publicly, exercise its real provider ceremony with an operator test account, including cancellation and a second sign-in. Deterministic protocol tests cover server handling; they do not verify the operator's provider-console registration or live provider availability.
