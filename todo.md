# Todo

- [ ] **Database password.** Postgres still uses the default `jorvis` / `jorvis` credentials in [docker-compose.yml](docker-compose.yml). Move it to a secret outside the repository, like the Cloudflare tunnel token. Changing it needs the existing database volume's password updated too, since `POSTGRES_PASSWORD` only applies on first initialization.
- [ ] **Manage passkeys in the app.** Let a signed-in person add another passkey (a second device), see their passkeys, and rename or remove them. Today an extra device means `./admin.sh recover`, which replaces all passkeys.
