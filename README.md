# Jorvis

Your AI: your hardware, your data, your rules. 

Optionally accessible from anywhere using a Cloudflare Tunnel.

## Run

Jorvis is intended to be used with [Docker Desktop](https://docs.docker.com/desktop/) to sandbox your AI; make sure it's installed and updated.

To start run the Bash script. For Bash on Windows, it's recommended to use [`Git Bash` which comes installed with Git](https://git-scm.com/install/windows) or [install and use Windows Subsystem for Linux (WSL)](https://learn.microsoft.com/en-us/windows/wsl/install).

```sh
./start.sh
```

The API runs in the `Production` environment by default. To run it as `Development` (for example to get the OpenAPI endpoint), set the environment variable `JORVIS_ENVIRONMENT`:

```sh
JORVIS_ENVIRONMENT=Development ./start.sh
```

The database schema is migrated automatically every time the API starts.

To stop:

```bash
./stop.sh
```

### Without Docker

Run a Postgres matching `ConnectionStrings:Database` in [appsettings.json](src/Jorvis.Api/appsettings.json), then start the API and the web app from `src` with `dotnet run --project Jorvis.Api` and `dotnet run --project Jorvis.Web`. The Sign In and Join app only talks to its own origin (that is enforced by its Content-Security-Policy) and the `dotnet run` web server has no `/api` proxy, so signing in only works through the Docker stack. Without nginx the app files are also downloadable without signing in (the API still refuses every request), and invite links use `http://localhost:5200` unless you set `Jorvis__PublicBaseUrl`. Create invites with `dotnet run --project Jorvis.Api -- invite`.

To check that the public Sign In and Join app still sends the Content-Security-Policy that keeps them from calling any other origin, run `src/Jorvis.Web/nginx/check-auth-csp.sh` against the running stack (it takes an optional base URL).

## Projects

Work is organized into projects. Each one has a name and a unique **label**: lowercase words joined by dashes, such as `home-reno`. The label is the project's permanent address, in the app (`/projects/home-reno`) and in the API (`/api/projects/home-reno/...`), so it can't be changed after the project is created.

- Whoever creates a project is its owner. Only members can see a project; to everyone else it doesn't exist.
- Any member can invite someone by their username. The invitation shows up on that person's Projects page, and they become a member when they accept it.
- The owner can rename the project, remove members, and delete it. Other members can leave.
- A project's board has lanes (starting with Backlog, To do, In progress and Done) that members can add, rename, reorder and delete once empty. Tasks are dragged between lanes and are numbered per project, so `home-reno-12` always means the same task.

## Access control

Jorvis is invite-only and passwordless. There is no open registration, no default account, no password anywhere, and no admin web page. Everything is enforced on the server: the browser only ever holds an opaque, `HttpOnly` session cookie, so nothing in the frontend code (which anyone can decompile) grants access to anything.

- Every API endpoint requires a signed-in user unless it is explicitly public (`/health`, `/api/auth/sign-in/*`, and `/api/auth/join/*` with a valid invite).
- nginx also refuses to serve the app files (the Blazor bundle) to anyone without a valid session. Signed-out visitors only ever get a separate, smaller Blazor app ([Jorvis.Web.Auth](src/Jorvis.Web.Auth)) with the Sign In page at `/sign-in` and the Join page at `/join` (both redirect to `/auth/...`). It uses the same MudBlazor theme, and its only JavaScript is a tiny shim, [passkeys.js](src/Jorvis.Web.Auth/wwwroot/passkeys.js), because browsers only expose passkeys to JavaScript.
- You sign in with a **passkey**, never a password. A passkey is a key pair: the private half stays on your phone or in your password manager (for example Passwords on iPhone, synced by iCloud Keychain) and never leaves it, and you approve each use with Face ID, Touch ID or your screen lock. Jorvis only stores the public half, so a database leak gives an attacker nothing to sign in with, and a fake website cannot trick you into handing anything over. There is no code to type. On a computer, the browser shows a QR code to scan with your phone.
- The passkey ceremonies are verified by ASP.NET Core Identity. Sign In and Join are rate limited per IP address.
- Cookies are `HttpOnly`, `SameSite=Lax` and `Secure` over HTTPS, and state-changing requests from other sites are rejected.
- Removing a user ends their sessions within a minute.

### Inviting people

Run these on the machine hosting Jorvis. Invites are created only here, never over HTTP.

```sh
./admin.sh invite                        # single-use link, valid 72 hours
./admin.sh invite --email a@example.com  # link only usable for that address
./admin.sh invite --hours 1              # shorter lifetime
./admin.sh users                         # list usernames, emails and passkey counts
./admin.sh set-username a@example.com sam  # change an account's username
./admin.sh recover a@example.com         # replace ALL of an account's passkeys (see below)
./admin.sh remove-user a@example.com     # delete an account and end its sessions
```

The first invite you create is how you make your own account. The link opens the Join page, where the person picks a username (3 to 32 letters, numbers, dots, dashes or underscores, unique ignoring case), enters their email and creates a passkey. The username is what the app shows for them. Send the link over a channel you trust: anyone who opens it before the person you meant can create an account, which is why links are single use and expire (and why `--email` exists).

### Losing a phone

Passkeys saved in iCloud Keychain, Google Password Manager or a password manager survive losing a device, because they sync to the account. If one is truly lost, or a device is stolen, run `./admin.sh recover <email>` and send that person the link. Opening it creates a new passkey, **deletes all of the old ones**, and signs out every other session. Without this command, a lost passkey with no backup would lock that person out, so keep the passkey in a synced place.

### Your address matters

A passkey is tied to the address Jorvis is opened at (its "relying party"). Set `JORVIS_PUBLIC_URL` **before** anyone creates an account, and always use that address:

- Links are built from `JORVIS_PUBLIC_URL`, which defaults to `http://localhost:5200`. Set it to your tunnel address, in your shell or in a git-ignored `.env` file next to `docker-compose.yml`, and restart with `./start.sh`:

  ```sh
  JORVIS_PUBLIC_URL=https://jorvis.example.com
  ```

- Passkeys created at `localhost` do not work at your tunnel address, and the other way around. Changing the address later means recovering every account with `./admin.sh recover`.
- Passkeys need a secure address: HTTPS (the tunnel provides it) or `localhost`. They do not work over plain HTTP on a LAN address.

All of Jorvis's ports are bound to `127.0.0.1`, so the only way in from another machine is the Cloudflare Tunnel.

### Cloudflare Tunnel

Have your AI be reached by the public Internet without opening ports which would weaken your network security. It is opt-in and disabled by default; without it your AI is only reachable locally on the same machine. All you need to use a Cloudflare Tunnel is a domain name (human readable web address) and a Cloudflare account that is setup to use said domain name.

#### Setup

1. Create a Cloudflare account for free. Note that you are subject to [Cloudflare Website and Online Services Terms of Use](https://www.cloudflare.com/website-terms/).
2. Add your domain to your Cloudflare account under `Domains` by either registering a new domain using Cloudflare or transfering your domain.
3. Create a tunnel under `Networking > Tunnels`.
   - Add a route.
      - Select `Published application`.
      - Select your domain name.
      - Enter a subdomain or path if you want.
      - For the Service URL use `http://jorvis-web:80`.
4. Save the token to file `~/.config/jorvis/cloudflare-tunnel-token` on macOS / Linux or `%USERPROFILE%\.config\jorvis\cloudflare-tunnel-token` on Windows. This file is outside the Git repository on purpose so you don't accidentally expose your Cloudflare Tunnel token. How you create and write to this file is up to you. Here are some CLI instructions to do so using Bash which include securing the directory and file permissions.

   ```sh
   # create the `.config/jorivs` directory at your user directory if not already created
   mkdir -p ~/.config/jorvis
   # secure the permissions on the directory so only you can read, write, and traverse it (700)
   chmod 700 ~/.config/jorvis
   # create the file (no extension) with the the contents of token copied from your clipboard
   pbpaste > ~/.config/jorvis/cloudflare-tunnel-token
   # secure the permissions on your Cloudflare token file so only you can read and write to the file
   chmod 600 ~/.config/jorvis/cloudflare-tunnel-token
   ```

If you want to use a different file path, set the environment variable`JORVIS_CLOUDFLARE_TUNNEL_TOKEN_FILEPATH` to use a different file path.

5. Simply run `./start.sh`. It enables the `tunnel` profile automatically when the token file exists.

## License

MIT. See [LICENSE](LICENSE) for more details. 
