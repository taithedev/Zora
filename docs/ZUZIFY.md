# Connect Zora to Zuzify ID

Zora licenses stay in the **Zora** Neon database. Zuzify accounts stay in **Zuzify Accounts** Neon Auth. The desktop app uses its existing license session to start an OAuth 2.1 authorization-code + PKCE flow in the system browser. No keys or account passwords are shared.

## Production configuration

The public Zuzify OAuth client **Zora** is registered in Neon Auth on project `rough-queen-59692102`, main branch. No client secret is needed.

- OAuth client ID: `NKTcizNpNAplTPMSxWfSjVBIrxLHuApK`
- Zuzify issuer: `https://ep-rough-recipe-bcjp6wq7.neonauth.c-16.us-east-1.aws.neon.tech/neondb/auth`
- Resource: `https://zuzify.xyz`
- Redirect: `https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/v1/zuzify/callback`
- Scopes: `openid profile`
- PKCE: `S256`, mandatory

These public values are the backend defaults. Optional env overrides: `ZUZIFY_AUTH_URL`, `ZUZIFY_OAUTH_CLIENT_ID`, and `ZUZIFY_REDIRECT_URI`. Do not add a private Zuzify database URL or OAuth token to the desktop build.

## Database and deployment

`server/migration-zuzify.sql` adds `zora_zuzify_links` and one-time `zora_zuzify_link_requests`. This migration was already applied to the Zora Neon project's main database (`zora`).

Deploy a new Zora Neon HTTP Function containing `server/src/index.mjs`, `server/src/zuzify.mjs`, and its imports. The existing GitHub workflow `neon-function-bundle.yml` creates the bundle artifact; uploading/deploying it to Neon Functions is a separate step. Keep the existing `DATABASE_URL`, `ZORA_SESSION_SECRET`, `ZORA_KEY_PEPPER`, Discord configuration, and owner settings.

## User flow

1. Activate Zora with your existing license. In **Connected accounts**, click **Connect Zuzify**.
2. The system browser opens the genuine Zuzify Auth authorization page. Sign in and approve Zora.
3. The Zora backend exchanges the code using the single-use, server-stored PKCE verifier, calls the provider's UserInfo endpoint and stores only the verified Zuzify UUID, license ID and link timestamp.
4. Returning to Zora, refresh connection status. **Unlink** removes the Zora mapping. A revoked OAuth consent can additionally be reviewed in Zuzify's account center.

## Discord bot consistency

Connect the same Discord identity to Zora using Zora's Discord OAuth button, and separately run `/connectzuzify` in ZuzifyDiscordBot. The bot's private `/zora` command reports a unified connection only if Zora's verified Zuzify UUID matches the bot's verified Zuzify UUID.

No license key, OAuth token, or user password is sent to the Discord bot. Existing Discord connections continue functioning independently.

## Security and tests

- A Zora license session is required to start, inspect, or unlink.
- Authorization state is random, hashed in storage, single-use, and expires after 10 minutes.
- PKCE verifiers are private server-side values. Only the public OAuth client ID appears in desktop app settings.
- A unique database constraint prevents one Zuzify identity from being attached to multiple Zora licenses.
- Run `cd server && npm test`, then build `desktop/Zora.Desktop/Zora.Desktop.csproj`.
