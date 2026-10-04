# VS Code setup for BirraPoint

Goal: a Visual Studio 2026-like experience (solution-wide diagnostics, test explorer, Aspire, NuGet/CPM
awareness) plus first-class Angular, Terraform and Docker support.

## What lives where

| Layer | Where | Versioned in GitHub |
|---|---|---|
| Project config (formatters, tasks, launch, recommended extensions) | `.vscode/` | Yes — applies to everyone who opens the repo |
| Personal look & feel (theme, font, keymap) | VS Code *Profile* | Yes, via a profile export (see below) |
| Extension list per language stack | Profiles | Same |

`.vscode/extensions.json` triggers an "install recommended extensions" prompt on first open.

## Profiles

Profiles are per **work context**, not per language: language servers are workspace-scoped, so a
polyglot repo like this one wants one full profile.

| Profile | Use | Extensions |
|---|---|---|
| `Default` | Quick edits, other repos | Almost empty (icons, GitLens, Claude) |
| `BirraPoint` | This repo | `.vscode/extensions.json` |
| `Rust` / `Python` | Side projects | Only that stack's tools |

Create `BirraPoint`: *Manage (gear) → Profiles → Create Profile → From Current Profile*, then run
`@recommended` in Extensions and install all.

## Personal settings (User profile, `settings.json`)

```jsonc
{
    "workbench.colorTheme": "Visual Studio Dark",       // built-in; "Light (Visual Studio)" also exists
    "editor.fontFamily": "'Cascadia Code', Consolas, monospace",
    "editor.fontLigatures": true,
    "editor.fontSize": 13,
    "editor.rulers": [100],
    "editor.guides.bracketPairs": "active",
    "editor.bracketPairColorization.enabled": true,
    "editor.stickyScroll.enabled": true,                // like VS sticky scroll
    "editor.minimap.renderCharacters": false,
    "editor.suggest.preview": true,
    "workbench.iconTheme": "vscode-icons",
    "workbench.startupEditor": "none",
    "workbench.editor.enablePreview": false,            // VS-like: no italic preview tabs
    "workbench.activityBar.location": "top",
    "git.autofetch": true,
    "git.confirmSync": false,
    "terminal.integrated.defaultProfile.windows": "PowerShell",
    "terminal.integrated.fontFamily": "'Cascadia Mono'",
    "problems.decorations.enabled": true,
    "errorLens.delay": 800,
    "errorLens.enabledDiagnosticLevels": ["error", "warning"]
}
```

Keybindings: `ms-vscode.vs-keybindings` already maps F5/F10/F11, Ctrl+K,C/U, Ctrl+Shift+B, Ctrl+. etc.
Add your own in `keybindings.json` (profile-scoped, so they sync too).

## Keep / remove (from your `code --list-extensions`)

**Keep (in `extensions.json`)**: C# Dev Kit + C#, Aspire, dotnet-cpm (`Directory.Packages.props`),
user-secrets, Playwright, Terraform (HashiCorp), Containers, GitHub Actions, YAML, PowerShell
(infra scripts), PostgreSQL client, REST Client, SonarLint (connected to the project), EditorConfig,
GitLens, ErrorLens, Mermaid (Docs), Dependi, Claude Code, VS keybindings, vscode-icons.

**Add (missing for the Angular half)**: Angular Language Service (`angular.ng-template`), ESLint,
Prettier, Tailwind CSS IntelliSense, Jest Runner, GitHub Pull Requests (PR workflow in CLAUDE.md).

**Uninstall for this repo** (or just disable in the BirraPoint profile):

```powershell
# Rust / Python (other projects -> put them in their own profiles)
code --uninstall-extension 1yib.rust-bundle
code --uninstall-extension dustypomerleau.rust-syntax
code --uninstall-extension rust-lang.rust-analyzer
code --uninstall-extension ms-python.python
code --uninstall-extension ms-python.vscode-pylance
code --uninstall-extension ms-python.debugpy
code --uninstall-extension ms-python.vscode-python-envs
code --uninstall-extension donjayamanne.python-environment-manager
# Duplicates / superseded / not used here
code --uninstall-extension 4ops.terraform                      # conflicts with hashicorp.terraform
code --uninstall-extension ms-azuretools.vscode-bicep           # repo uses Terraform
code --uninstall-extension formulahendry.docker-explorer        # superseded by vscode-containers
code --uninstall-extension kreativ-software.csharpextensions   # superseded by C# Dev Kit
code --uninstall-extension nikiforovall.sync-namespaces-dotnet  # Dev Kit handles namespaces
code --uninstall-extension ms-dotnettools.blazorwasm-companion
code --uninstall-extension ms-dotnettools.vscode-dotnet-pack    # bundle; components installed individually
code --uninstall-extension ms-vscode.vscode-node-azure-pack     # bundle
code --uninstall-extension ms-azuretools.vscode-azure-github-copilot
code --uninstall-extension ms-azuretools.vscode-azure-mcp-server
code --uninstall-extension ms-vscode.azurecli
code --uninstall-extension ms-azuretools.azure-dev
# Low value
code --uninstall-extension jerrygoyal.shortcut-menu-bar
code --uninstall-extension smulyono.reveal
code --uninstall-extension vsls-contrib.codetour
code --uninstall-extension wayou.vscode-todo-highlight         # ErrorLens + built-in todo tree cover it
```

Keep `ms-azuretools.vscode-azureresourcegroups` / `vscode-azurecontainerapps` only if you browse the
Azure deployment from VS Code; the repo is cloud-agnostic (Azure or AWS). Add
`amazonwebservices.aws-toolkit-vscode` when T146 (AWS deploy) starts.

## Sync with GitHub

VS Code's built-in **Settings Sync** uses a Microsoft/GitHub *login*, not a git repo, so it is not
visible in GitHub. Use both:

1. **Repo (this change)**: `.vscode/*` — shared, reviewable, versioned.
2. **Personal profile**: *Profiles → Export Profile…* → "Export to GitHub" (creates a secret gist, importable
   on any machine from *Import Profile*). Or save the `.code-profile` file in a private
   `vscode-profiles` repo. Re-export after meaningful changes.
3. Optionally turn on *Settings Sync: Turn On* (sign in with GitHub) so profiles also follow you
   across machines automatically.

Never put tokens in these files; `sonarlint.connectedMode` only stores connection ids.

## Daily workflow cheat sheet

| Action | How |
|---|---|
| Run full stack with debugging | Run & Debug → *Aspire: Launch default AppHost* |
| Debug PWA in Edge | *PWA: debug in Edge (:4200)* |
| Build / test | `Ctrl+Shift+B` / Testing view (C# Dev Kit) / Tasks: Run Test Task |
| Jest, one test | CodeLens "Run/Debug" (Jest Runner) |
| Playwright | Testing view → Playwright section |
| All checks | Tasks: Run Task → `backend: …`, `frontend: …`, `terraform …` |
| Solution Explorer | Explorer → *Solution Explorer* section (Dev Kit) |
