# 2026-06-12 PuTTY remote git pull profile

## Goal

Verify the PuTTY/plink remote workflow against the SGStudio Raspberry Pi host and persist the reusable, non-secret connection profile into the remote build skill.

## Verified Flow

- SSH host: `192.168.3.179`
- SSH user: `htra`
- Host key: `ssh-ed25519 255 SHA256:VgtR4/pnkPONoSY8NhUC8bH3SrgkAHVrVLMJI9TQYQ4`
- Project path: `~/Desktop/SGSProject`
- Git remote: `http://192.168.3.29:8089/rdd/sgstudio.git`
- Git user: `jiashilin`
- Command result: `git pull` completed with `Already up to date.`

## Security Boundary

- Do not write plaintext SSH or Git passwords into repository files or skills.
- Use session-only environment variables, an SSH key/Pageant, or a Git credential helper.
- For non-interactive Git HTTP authentication, use a temporary `GIT_ASKPASS` script and delete it after the command.

## Notes

- The first `plink -batch` probe failed because the host key was not cached; using `-hostkey` fixed the non-interactive path.
- PowerShell/plink quoting matters. To strip Windows CRLF from a piped temporary script on the remote side, pass `tr -d '\r'` using doubled PowerShell single quotes inside the remote command.
