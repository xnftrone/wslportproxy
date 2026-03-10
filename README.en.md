# WSL PortProxy Guardian

[中文说明](./README.md)

`WSL PortProxy Guardian` is a Windows-native CLI that keeps selected TCP port forwards pointed at a WSL distro.

It is designed for environments where:
- WSL2 runs in the default `NAT` mode
- The Windows host can reach a target network, but the target network cannot directly reach WSL
- You need stable inbound TCP forwarding from Windows into a WSL service
- The WSL IP may change and forwarding rules must be reconciled automatically

The tool runs in the foreground, emits logs continuously, tracks the current WSL IPv4 address, and removes only the mappings it actively managed when the process exits.

## Features

- Native `.exe` built with C# / .NET
- Auto-detects the target WSL IPv4 address
- Supports multiple ports
- Supports `listenPort:connectPort` mappings
- Reconciles mappings when the WSL IP changes
- Emits live console logs while running
- Cleans up only its owned mappings and firewall rules on exit
- Refuses to override an already-listening host TCP service
- Refuses to take over a pre-existing `portproxy` mapping on the same declared port
- Supports `--dry-run`

## Example

```powershell
wslportproxy.exe run -p 4444 -p 8000 -p 8443:443
```

## Usage

```powershell
wslportproxy.exe run [options]
```

## Options

- `run`
  Start guardian mode and continuously maintain the declared mappings.

- `-p, --port <value>`
  Declare a port or port mapping. Repeatable and comma-separated forms are both supported.

- `-d, --distro <name>`
  Target WSL distro name. Default: `kali-linux`

- `-l, --listen-address <address>`
  Windows listen address. Default: `0.0.0.0`

- `-i, --interval <seconds>`
  Poll interval in seconds. Default: `3`

- `--no-firewall`
  Skip Windows Firewall rule management.

- `--dry-run`
  Log intended actions without mutating `portproxy` or firewall state.

- `-h, --help`
  Show help output.

## Safety Model

- Only touches ports explicitly declared through `-p` / `--port`
- Does not bulk-delete unrelated `portproxy` rules
- Does not remove unmanaged ports on shutdown
- Refuses to override a host TCP port that already has a live listener
- Refuses to override a pre-existing `portproxy` rule for the same declared port

## Build

```powershell
dotnet build .\tools\wsl-portproxy-guardian\WslPortProxyGuardian.sln
dotnet test .\tools\wsl-portproxy-guardian\WslPortProxyGuardian.sln
dotnet publish .\tools\wsl-portproxy-guardian\src\WslPortProxyGuardian.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o .\tools\wsl-portproxy-guardian\build
```

## License

This project is licensed under the MIT License. See [`LICENSE`](./LICENSE).
