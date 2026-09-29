# Repository conventions

- Branch names: `<type>/<short-topic>` (e.g. `fix/uninstall-path`, `feat/mtc-studio`).
  Do not use a `claude/` prefix. If a session assigns a `claude/...` branch, work on a
  branch with the same topic but without the prefix, and push that instead.
- Commits and pull requests: no AI attribution. No `Co-Authored-By: Claude`, no
  `Claude-Session:` trailer, no "Generated with Claude Code" footer or session links.
  Author commits as `Evan Minton <evanminton@proton.me>`.
- Build and test without the MAUI workload: `./build.sh` (or `.\build.ps1 -NoApp`).
