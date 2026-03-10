# AGENTS

[中文版本](./AGENTS.md)

## Project Purpose

This repository contains a Windows-native CLI that continuously maintains TCP port forwarding from the host into a WSL distro.

## Non-Negotiable Safety Rules

- Never broaden rule cleanup beyond ports explicitly declared by the current command.
- Never bulk-delete unrelated `portproxy` rules.
- Never remove firewall rules that are not owned by this tool.
- Refuse to mutate a declared port if the host already has a live TCP listener on that port.
- Refuse to mutate a declared port if a pre-existing foreign `portproxy` rule already uses that port.
- Prefer failing closed over forcing a takeover.

## Implementation Expectations

- Keep the tool Windows-specific and explicit about that in code and docs.
- Preserve foreground logging and automatic cleanup on shutdown.
- Keep command-line help accurate whenever options change.
- Update tests before changing runtime behavior.
- Keep README public-facing and environment-agnostic.
- Put machine-specific notes in local-only files, not in the public README.

## Testing Expectations

- Add or update unit tests for every behavior change.
- Verify parsing, conflict detection, and cleanup behavior.
- Do not claim a port-safety change is complete without a passing test that proves it.
