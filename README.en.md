# WSL PortProxy Guardian

[中文说明](./README.md)

`WSL PortProxy Guardian` is a Windows-native CLI that continuously maintains selected TCP forwards from the Windows host into a WSL distro.

It is designed for WSL2 NAT networking, where the Windows host is reachable but the WSL IPv4 address can change and hand-written `netsh interface portproxy` rules become stale.

## Features

- Resolves the target WSL distro IPv4 address automatically
- Supports multiple TCP ports and `listenPort:connectPort` mappings
- Refreshes managed forwarding rules when the WSL IP changes
- Requests administrator privileges through UAC when launched unelevated
- Emits foreground logs and writes one log file per run
- Optionally creates and removes Windows Firewall inbound rules
- Cleans up only rules that the current process actually managed
- Refuses to override active TCP listeners, foreign `portproxy` rules, or foreign same-name firewall rules
- Diagnoses forwarding failures with Windows TCP state and WSL `ss` output
- Supports `--dry-run`

## Quick Start

```powershell
.\build\wslportproxy.exe run -p 9999 -d kali-linux
```

When started from a non-elevated terminal, the tool asks for UAC elevation and relaunches the same command in an elevated window.

Common port mapping syntax:

```powershell
.\build\wslportproxy.exe run -p 4444 -p 8000 -p 8443:443
```

This means:

- Windows `4444` forwards to WSL `4444`
- Windows `8000` forwards to WSL `8000`
- Windows `8443` forwards to WSL `443`

## Logs

Each run writes an independent log file under the current working directory:

```text
.\logs\wslportproxy-YYYYMMDD-HHMMSS-fff-p<PID>.log
```

Log lines include millisecond timestamps and process IDs. If UAC elevation is needed, the parent process and the elevated child process write to the same per-run log file, so short-lived elevated failures remain visible.

## Usage

```powershell
.\build\wslportproxy.exe run [options]
```

### Options

- `run`
  Starts guardian mode and continuously maintains the declared mappings.

- `-p, --port <value>`
  Declares a port or port mapping. Repeatable and comma-separated forms are both supported.
  Examples: `4444`, `8443:443`, `4444,8000,8443:443`.

- `-d, --distro <name>`
  Target WSL distro name. Default: `kali-linux`.

- `-l, --listen-address <address>`
  Windows listen address. Default: `0.0.0.0`.

- `-i, --interval <seconds>`
  WSL IP and connection-state poll interval. Default: `3`.

- `--no-firewall`
  Do not create or remove Windows Firewall rules.

- `--dry-run`
  Log intended actions without changing `portproxy` or firewall state.

- `-h, --help`
  Show help output.

## Diagnostics

The tool emits diagnostics after mappings are created, when the WSL IP changes, on heartbeat, and when connections are observed.

On the Windows side it logs TCP connections to declared listen ports:

```text
Received TCP connection from <remote> to <local>; forwarding to <wsl-ip>:<port>
Forwarded TCP connection closed ...
```

On the WSL side it uses `ss -ltnp` and `ss -tnp` to inspect target ports:

- Warns when the target port has no listener
- Warns when the service listens only on `127.0.0.1` / `::1`
- Warns when Windows has active connections but WSL shows none on the target port
- Logs visible listeners and connections, including process details when available

When forwarded traffic is not reaching the service, check these signals first:

- No `Received TCP connection`: traffic may not be reaching the Windows listen port
- Windows has a connection but WSL does not: the issue is likely between `portproxy` and WSL
- WSL only listens on loopback: bind the service to `0.0.0.0`, the WSL IP, or another address reachable by `portproxy`

## Safety Model

The tool is intentionally conservative:

- Only touches ports explicitly declared with `-p` / `--port`
- Never bulk-deletes unrelated `portproxy` rules
- Never removes mappings outside the current process ownership set
- Refuses to modify a port with an active host TCP listener
- Refuses to take over a foreign `portproxy` rule on the declared port
- Refuses to take over a foreign firewall rule with the same managed name
- Fails closed and leaves logs when state is ambiguous

## Build

### Requirements

- Windows
- .NET SDK 8.0 or later

### Build

```powershell
dotnet build .\WslPortProxyGuardian.sln
```

### Test

```powershell
dotnet test .\WslPortProxyGuardian.sln
```

### Publish Single-File Executable

```powershell
dotnet publish .\src\WslPortProxyGuardian.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o .\build
```

## Project Layout

```text
.
+-- src/
+-- tests/
+-- build/
+-- README.md
+-- README.en.md
`-- AGENTS.md
```

## License

This project is licensed under the MIT License. See [LICENSE](./LICENSE).
