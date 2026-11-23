# Zora + Zuzify Discord bot linking

Zora uses Discord's documented OAuth2 authorization-code flow, **not** bot-token impersonation or a Discord password.

1. In the existing Zuzify Discord bot application's Discord Developer Portal, choose **OAuth2 → General**. Register this exact redirect URL:
   `https://br-frosty-bar-bbmdnhr7-zoralicense.compute.c-15.us-east-1.aws.neon.tech/v1/discord/callback`
2. Set these environment variables in the **Zora Neon Function** (not in the desktop app or GitHub):
   - `DISCORD_CLIENT_ID`: the Zuzify bot application's client/application ID.
   - `DISCORD_CLIENT_SECRET`: that application's OAuth2 client secret (not the bot token).
   - `DISCORD_REDIRECT_URI`: the URL above, matching exactly.
   - `ZORA_BOT_SYNC_SECRET`: independently generated 32+ character random secret, never a license key.
3. Redeploy the Zora Neon function after setting environment variables. Its existing `DATABASE_URL`, `ZORA_KEY_PEPPER`, `ZORA_SESSION_SECRET` and owner configuration must remain unchanged.
4. Set `ZORA_SYNC_URL` to the Zora Neon function base URL and `ZORA_BOT_SYNC_SECRET` to the **same** random token in the Zuzify Discord bot's Railway variables. Redeploy the bot and register its `/zora` slash command using its normal command-registration workflow.
5. In Zora open **Discord sync → Connect Discord**, approve identity access in the browser, then return to Zora. `/zora` in Discord shows the linked account *privately*.

**Important:** Never paste the Discord Client Secret, bot token, Neon connection URL, or shared service secret into the desktop app, public source, or a Discord channel. The Zora app asks only for your existing Zora license.

### Data retained

`zora_discord_links`: Zora license ID, Discord user ID, Discord username, optional display/avatar metadata, link timestamp. OAuth state is single-use and expires in 10 minutes; stale request rows can be periodically deleted. Discord OAuth tokens are discarded after retrieving `/users/@me` and are not persisted. Users can unlink from Zora; it deletes the association. No Discord messages, email, guild data, or contacts are collected.

### Limits

Account linking **does not** automatically assign Discord guild roles, connect unrelated Zuzify services, or recover any Unity assets. All of those would require separate opt-in integrations. The bot `/zora` command only reports the user's own linked Zora username.
