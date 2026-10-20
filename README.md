# Jorvis

Your AI: your hardware, your data, your rules. 

Optionally accessible from anywhere using a Cloudflare Tunnel.

## Run

Jorvis is intended to be used with [Docker Desktop](https://docs.docker.com/desktop/) to sandbox your AI; make sure it's installed and updated.

To start run the Bash script. For Bash on Windows, it's recommended to use [`Git Bash` which comes installed with Git](https://git-scm.com/install/windows) or [install and use Windows Subsystem for Linux (WSL)](https://learn.microsoft.com/en-us/windows/wsl/install).

```sh
./start.sh
```

To stop:

```bash
./stop.sh
```

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
