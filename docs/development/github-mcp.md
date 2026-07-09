# GitHub MCP-Server (Claude Code)

Bindet den **GitHub MCP-Server** in Claude Code ein, damit die Arbeit an EBICO
(Issues, Pull Requests, Repo-Inhalte, Commits, Code-Suche, Actions) direkt über
strukturierte Tools statt nur über die `gh`-CLI läuft. Ergänzt die
issue-getriebene Arbeitsweise (`feat/<nr>-<slug>` + PR mit `Closes #<nr>`).

Betrifft die Entwickler-Umgebung (Tooling), keinen EBICS-Feature-Code.

## Konfiguration

Der Server ist im **Project-Scope** in `.mcp.json` (Repo-Root) eingetragen und
wird damit mit dem Team geteilt:

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

- **Remote-Server**: der offizielle, von GitHub gehostete MCP-Endpoint
  (`https://api.githubcopilot.com/mcp/`) — kein lokaler Docker-Container, keine
  eigene Versionspflege.
- **Auth per Personal Access Token (PAT)** im `Authorization`-Header. `${GITHUB_MCP_PAT}`
  wird zur Laufzeit aus einer Umgebungsvariable expandiert — **kein Secret im Repo**.

### Warum PAT statt OAuth?

Claude Codes eingebauter OAuth-Flow benötigt **Dynamic Client Registration
(RFC 7591)**. Der GitHub-MCP-Endpoint unterstützt das aktuell nicht; der Login
scheitert mit `Incompatible auth server: does not support dynamic client
registration`. Der PAT-Header ist der dokumentierte Ausweg und für die
Remote-Variante die zuverlässige Auth-Methode.

## Einrichtung je Entwickler

Jeder nutzt seinen **eigenen** PAT — die `.mcp.json` referenziert nur die
Variable, den Token setzt jeder lokal.

### 1. Fine-grained PAT erstellen

<https://github.com/settings/personal-access-tokens> → *Generate new token*

- **Resource owner / Repository access**: das/die relevanten Repos (z. B. `dominikz98/ebico`).
- **Repository permissions** (Minimum für den Alltag):

  | Permission     | Zugriff          | Wofür                          |
  | -------------- | ---------------- | ------------------------------ |
  | Contents       | Read (o. Write)  | Dateien/Branches lesen, pushen |
  | Metadata       | Read (Pflicht)   | Grundzugriff aufs Repo         |
  | Issues         | Read and write   | Issues lesen/anlegen/kommentieren |
  | Pull requests  | Read and write   | PRs erstellen/reviewen         |
  | Actions        | Read             | CI-Läufe/Logs (optional)       |

  Nur Lesezugriff genügt, wenn der Server ausschließlich lesen soll.
- Alternativ ein Classic-Token mit Scope `repo`.

### 2. Token als Umgebungsvariable `GITHUB_MCP_PAT` setzen

Dauerhaft im User-Profil hinterlegen (den Token **nicht** in freigegebene
Terminals/Transcripts einfügen):

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

> Die Variable greift nur für **neu gestartete** Prozesse. Terminal **und**
> Claude Code danach neu starten, damit der MCP-Client sie im Environment sieht.

### 3. Claude Code neu starten & Server freigeben

Beim Start fragt Claude Code, ob dem Server aus der Project-`.mcp.json`
vertraut wird (Sicherheitsabfrage für geteilte MCP-Configs) → **bestätigen**.

### 4. Verbindung prüfen

```
claude mcp get github     # erwartet: ✔ connected
```

## Sicherheit

- **Kein Token im Repo**: `.mcp.json` enthält nur die Env-Referenz
  `${GITHUB_MCP_PAT}`, nie den Klartext-Token.
- **Pro Entwickler ein PAT** mit minimalen Permissions; bei Verdacht auf Leak in
  den GitHub-Settings widerrufen und neu erzeugen.
- Fine-grained Tokens auf die konkret benötigten Repos einschränken.

## Troubleshooting

| Symptom | Ursache / Lösung |
| ------- | ---------------- |
| `Incompatible auth server: does not support dynamic client registration` | OAuth wird nicht unterstützt → PAT-Header nutzen (siehe oben), nicht per `/mcp → Authenticate` einloggen. |
| `The filename, directory name, or volume label syntax is incorrect` | PowerShell-Syntax in cmd.exe ausgeführt. Entweder `pwsh` starten oder `setx` verwenden. |
| Server bleibt `failed` / Variable „leer" | Env-Variable erst nach Neustart aktiv; Terminal **und** Claude Code neu starten. |
| `⏸ Pending approval` | Project-Scope-Server müssen einmalig bestätigt werden (`claude` erneut starten und Vertrauen bestätigen). |

Schnelltest des Tokens (ohne ihn auszugeben) gegen den Endpoint:

```powershell
$t = [Environment]::GetEnvironmentVariable("GITHUB_MCP_PAT","User")
Invoke-WebRequest -Uri "https://api.github.com/user" `
  -Headers @{ Authorization = "Bearer $t"; "User-Agent" = "ebico" } | Select-Object StatusCode
```
