# WSL PortProxy Guardian

[English README](./README.en.md)

`WSL PortProxy Guardian` 是一个面向 Windows 的原生命令行工具，用于持续维护从宿主机到 WSL 发行版的 TCP 端口转发。

它适合以下场景：
- WSL2 使用默认 `NAT` 网络模式
- Windows 宿主机可以访问目标网络，但目标网络不能直接访问 WSL
- 需要把 Windows 上的指定 TCP 端口稳定转发到 WSL 内的服务
- WSL IP 可能变化，需要自动重建转发规则

工具启动后会以前台进程方式持续运行，循环检查目标 WSL 发行版的 IPv4 地址。如果地址发生变化，它会自动重建你声明的端口映射。工具退出时，会自动清理当前进程托管的规则。

## 功能特性

- 基于 C# / .NET 构建，输出原生 `.exe`
- 自动解析目标 WSL 发行版的 IPv4 地址
- 支持多个端口
- 支持 `监听端口:目标端口` 映射格式
- WSL IP 变化时自动重建映射
- 持续输出控制台日志
- 退出时只清理自己托管的端口转发和防火墙规则
- 如果宿主机端口已被监听，会拒绝接管
- 如果目标端口上已有旧的 `portproxy` 规则，会拒绝覆盖
- 支持 `--dry-run` 预演模式

## 示例

```powershell
wslportproxy.exe run -p 4444 -p 8000 -p 8443:443
```

这表示：
- 监听 Windows `4444`，转发到 WSL `4444`
- 监听 Windows `8000`，转发到 WSL `8000`
- 监听 Windows `8443`，转发到 WSL `443`

## 用法

```powershell
wslportproxy.exe run [options]
```

## 参数说明

- `run`
  启动守护模式并持续维护声明的端口映射。

- `-p, --port <值>`
  声明一个端口或端口映射。支持重复传入，也支持逗号分隔。

  示例：
  - `4444`
  - `8000`
  - `8443:443`
  - `4444,8000,8443:443`

- `-d, --distro <名称>`
  指定目标 WSL 发行版，默认值为 `kali-linux`。

- `-l, --listen-address <地址>`
  指定 Windows 监听地址，默认值为 `0.0.0.0`。

- `-i, --interval <秒>`
  指定轮询间隔，默认值为 `3`。

- `--no-firewall`
  不自动维护 Windows 防火墙规则。

- `--dry-run`
  只打印将要执行的动作，不实际修改 `portproxy` 或防火墙。

- `-h, --help`
  输出帮助信息。

## 工作原理

运行时工具会：

1. 检查当前进程是否具有管理员权限
2. 解析命令行参数
3. 获取目标 WSL 发行版的当前 IPv4 地址
4. 在修改每个端口前先进行安全检查
5. 创建所需的 `portproxy` 和防火墙规则
6. 定期轮询 WSL IP
7. 当 WSL IP 变化时自动重建映射
8. 在进程退出时清理当前运行期间托管的规则

## 安全模型

这个工具采用保守策略：

- 只处理你通过 `-p` / `--port` 显式声明的端口
- 不会批量删除无关的 `portproxy` 规则
- 不会在退出时清理未由当前进程托管的端口
- 如果宿主机上已经有活动 TCP 监听器占用该端口，会直接拒绝接管
- 如果该端口上已经存在旧的 `portproxy` 规则，也会直接拒绝覆盖

如果你想使用某个已经被占用的端口，需要先手动释放那个端口，然后再运行工具。

## 构建

### 依赖

- Windows
- .NET SDK 8.0 或更高版本

### 编译

```powershell
dotnet build .\tools\wsl-portproxy-guardian\WslPortProxyGuardian.sln
```

### 测试

```powershell
dotnet test .\tools\wsl-portproxy-guardian\WslPortProxyGuardian.sln
```

### 发布单文件可执行程序

```powershell
dotnet publish .\tools\wsl-portproxy-guardian\src\WslPortProxyGuardian.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o .\tools\wsl-portproxy-guardian\build
```

## 项目结构

```text
tools/wsl-portproxy-guardian/
├─ src/
├─ tests/
├─ build/
├─ README.md
├─ README.en.md
├─ AGENTS.md
└─ AGENTS.en.md
```

## 许可证

本项目使用 MIT License。正式法律文本见 [`LICENSE`](./LICENSE)。
