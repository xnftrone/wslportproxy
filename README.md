# WSL PortProxy Guardian

[English README](./README.en.md)

`WSL PortProxy Guardian` 是一个 Windows 原生命令行工具，用于持续维护从 Windows 宿主机到指定 WSL 发行版的 TCP 端口转发。

它适用于 WSL2 默认 NAT 网络场景：Windows 可以被外部访问，但 WSL 内部服务的 IP 可能变化，导致手工维护 `netsh interface portproxy` 规则很容易失效。

## 功能

- 自动解析目标 WSL 发行版的 IPv4 地址
- 支持多个 TCP 端口和 `监听端口:目标端口` 映射
- WSL IP 变化后自动刷新本工具托管的转发规则
- 普通权限启动时自动弹出 UAC 请求管理员权限
- 前台持续输出日志，并为每次运行写入独立日志文件
- 可选创建和清理 Windows 防火墙入站规则
- 退出时只清理当前进程实际托管的规则
- 拒绝覆盖活动 TCP 监听器、外部 `portproxy` 规则和外部同名防火墙规则
- 通过 Windows TCP 状态和 WSL `ss` 输出诊断转发失败位置
- 支持 `--dry-run` 预演模式

## 快速开始

```powershell
.\build\wslportproxy.exe run -p 9999 -d kali-linux
```

第一次从普通终端启动时，工具会请求 UAC 提权。提权后的新窗口会继续运行同一条命令。

常见端口写法：

```powershell
.\build\wslportproxy.exe run -p 4444 -p 8000 -p 8443:443
```

这表示：

- Windows `4444` 转发到 WSL `4444`
- Windows `8000` 转发到 WSL `8000`
- Windows `8443` 转发到 WSL `443`

## 日志

每次运行都会在当前工作目录下创建一个独立日志文件：

```text
.\logs\wslportproxy-YYYYMMDD-HHMMSS-fff-p<PID>.log
```

日志行包含毫秒级时间戳和进程 ID。触发 UAC 时，父进程和提权后的子进程会写入同一个本次运行日志文件，方便排查新窗口一闪而过的问题。

## 用法

```powershell
.\build\wslportproxy.exe run [options]
```

### 选项

- `run`
  启动守护模式，持续维护声明的端口映射。

- `-p, --port <value>`
  声明端口或端口映射。支持重复传入，也支持逗号分隔。
  示例：`4444`、`8443:443`、`4444,8000,8443:443`。

- `-d, --distro <name>`
  目标 WSL 发行版名称。默认：`kali-linux`。

- `-l, --listen-address <address>`
  Windows 监听地址。默认：`0.0.0.0`。

- `-i, --interval <seconds>`
  WSL IP 和连接状态轮询间隔。默认：`3`。

- `--no-firewall`
  不自动创建或删除 Windows 防火墙规则。

- `--dry-run`
  只输出将要执行的动作，不修改 `portproxy` 或防火墙。

- `-h, --help`
  输出帮助。

## 诊断能力

工具会在映射创建、WSL IP 刷新、心跳和观察到连接时输出诊断日志。

Windows 侧会记录声明端口上的 TCP 连接：

```text
Received TCP connection from <remote> to <local>; forwarding to <wsl-ip>:<port>
Forwarded TCP connection closed ...
```

WSL 侧会通过 `ss -ltnp` 和 `ss -tnp` 检查目标端口：

- 目标端口没有监听时输出警告
- 服务只监听 `127.0.0.1` / `::1` 时输出警告
- Windows 已有连接但 WSL 目标端口没有连接时输出警告
- 能看到监听器或连接时输出对应信息，权限允许时包含进程信息

如果转发后收不到请求，优先看日志中的三类信号：

- 没有 `Received TCP connection`：请求可能没有到达 Windows 监听端口
- Windows 有连接但 WSL 无连接：问题可能在 `portproxy` 到 WSL 之间
- WSL 只监听 loopback：目标服务需要监听 `0.0.0.0`、WSL IP，或可被 `portproxy` 连接到的地址

## 安全模型

本工具采用保守策略：

- 只处理通过 `-p` / `--port` 显式声明的端口
- 不批量删除无关 `portproxy` 规则
- 不删除不属于当前进程托管范围的端口映射
- 如果宿主机上已有活动 TCP 监听器占用目标端口，拒绝修改
- 如果目标端口上已有非本工具托管的 `portproxy` 规则，拒绝接管
- 如果发现外部同名防火墙规则，拒绝接管
- 不确定时默认失败退出，并保留日志

## 构建

### 依赖

- Windows
- .NET SDK 8.0 或更高版本

### 编译

```powershell
dotnet build .\WslPortProxyGuardian.sln
```

### 测试

```powershell
dotnet test .\WslPortProxyGuardian.sln
```

### 发布单文件版本

```powershell
dotnet publish .\src\WslPortProxyGuardian.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o .\build
```

## 项目结构

```text
.
+-- src/
+-- tests/
+-- build/
+-- README.md
+-- README.en.md
`-- AGENTS.md
```

## 许可证

本项目使用 MIT License。详见 [LICENSE](./LICENSE)。
