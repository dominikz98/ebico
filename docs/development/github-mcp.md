# GitHub MCP server (Claude Code)

Integrates the **GitHub MCP server** into Claude Code so that the work on EBICO
(issues, pull requests, repo content, commits, code search, actions) runs directly through
structured tools instead of only through the `gh` CLI. Complements the
issue-driven way of working (`feat/<nr>-<slug>` + PR with `Closes #<nr>`).

Concerns the developer environment (tooling), not any EBICS feature code.

## Configuration

The server is registered in **project scope** in `.mcp.json` (repo root) and
is thereby shared with the team:

```json
{
  "mcpServers": {
    "github": {
      "type": "http",
      "url": "https://api.githubcopilot.com/mcp/",
      "headers": {
        "Authorization": "Bearer ${GITHUB_MCP_PAT}"
      }
    }
  }
}
```

- **Remote server**: the official MCP endpoint hosted by GitHub
  (`https://api.githubcopilot.com/mcp/`) — no local Docker container, no
  own version maintenance.
- **Auth via personal access token (PAT)** in the `Authorization` header. `${GITHUB_MCP_PAT}`
  is expanded at runtime from an environment variable — **no secret in the repo**.

### Why PAT instead of OAuth?

Claude Code's built-in OAuth flow needs **Dynamic Client Registration
(RFC 7591)**. The GitHub MCP endpoint does not currently support it; the login
fails with `Incompatible auth server: does not support dynamic client
registration`. The PAT header is the documented way out and, for the
remote variant, the reliable auth method.

## Setup per developer

Everyone uses their **own** PAT — the `.mcp.json` only references the
variable, the token is set by each person locally.

### 1. Create a fine-grained PAT

<https://github.com/settings/personal-access-tokens> → *Generate new token*

- **Resource owner / Repository access**: the relevant repo(s) (e.g. `dominikz98/ebico`).
- **Repository permissions** (minimum for everyday use):

  | Permission     | Access           | For what                       |
  | -------------- | ---------------- | ------------------------------ |
  | Contents       | Read (or Write)  | Read files/branches, push      |
  | Metadata       | Read (mandatory) | Basic access to the repo       |
  | Issues         | Read and write   | Read/create/comment on issues  |
  | Pull requests  | Read and write   | Create/review PRs              |
  | Actions        | Read             | CI runs/logs (optional)        |

  Read-only access is enough if the server is only meant to read.
- Alternatively a classic token with scope `repo`.

### 2. Set the token as environment variable `GITHUB_MCP_PAT`

Store it permanently in the user profile (do **not** paste the token into shared
terminals/transcripts):

**PowerShell** (`pwsh`):

```powershell
[Environment]::SetEnvironmentVariable("GITHUB_MCP_PAT", "<DEIN_TOKEN>", "User")
```

**cmd.exe:**

```cmd
setx GITHUB_MCP_PAT "<DEIN_TOKEN>"
```

**macOS/Linux** (in `~/.bashrc` / `~/.zshrc`):

```sh
export GITHUB_MCP_PAT="<DEIN_TOKEN>"
```

> The variable only takes effect for **newly started** processes. Restart the terminal **and**
> Claude Code afterwards, so that the MCP client sees it in the environment.

### 3. Restart Claude Code & trust the server

On startup, Claude Code asks whether the server from the project `.mcp.json`
is trusted (security prompt for shared MCP configs) → **confirm**.

### 4. Check the connection

```
claude mcp get github     # erwartet: ✔ connected
```

## Security

- **No token in the repo**: `.mcp.json` contains only the env reference
  `${GITHUB_MCP_PAT}`, never the plaintext token.
- **One PAT per developer** with minimal permissions; on suspicion of a leak, revoke it in
  the GitHub settings and regenerate.
- Restrict fine-grained tokens to the specifically needed repos.

## Troubleshooting

| Symptom | Cause / solution |
| ------- | ---------------- |
| `Incompatible auth server: does not support dynamic client registration` | OAuth is not supported → use the PAT header (see above), do not log in via `/mcp → Authenticate`. |
| `The filename, directory name, or volume label syntax is incorrect` | PowerShell syntax executed in cmd.exe. Either start `pwsh` or use `setx`. |
| Server stays `failed` / variable "empty" | Env variable only active after restart; restart terminal **and** Claude Code. |
| `⏸ Pending approval` | Project-scope servers must be confirmed once (restart `claude` and confirm trust). |

Quick test of the token (without printing it) against the endpoint:

```powershell
$t = [Environment]::GetEnvironmentVariable("GITHUB_MCP_PAT","User")
Invoke-WebRequest -Uri "https://api.github.com/user" `
  -Headers @{ Authorization = "Bearer $t"; "User-Agent" = "ebico" } | Select-Object StatusCode
```
