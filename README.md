# DML

Delta Minecraft Launcher（DML）是一个面向 Windows 的 Minecraft: Java Edition 启动器。项目当前实现 **v0.3：官方版本发现与可靠下载**，尚不包含账户登录或游戏启动功能。

## 已实现

- WPF + MVVM 分层解决方案和依赖注入。
- 中文资源字典与设置页：游戏根目录、下载并发数、代理、主题。
- 设置持久化到 `%AppData%\\DML\\settings.json`，通过临时文件替换防止写入中断。
- 文件日志和应用内日志视图，日志保留 14 天，并掩盖常见令牌、Cookie、密码与授权码字段。
- 集中式 NuGet 包版本、EditorConfig、单元测试与 GitHub Actions CI。
- 独立游戏实例的创建、复制、重命名、导入与回收站删除；实例不会覆盖官方启动器目录。
- 从 Mojang 官方清单发现 release / snapshot，并缓存清单供离线查看。
- 为独立实例下载版本 JSON、client JAR、Windows 所需 libraries、natives 与 assets。
- 下载支持并发、暂停、恢复、取消、三次自动重试和 HTTP Range 断点续传；重启后重新执行安装即可继续未完成的分片。
- 所有游戏文件都校验清单声明的大小与 SHA-1，通过后才由临时文件原子替换；损坏缓存会自动重新下载。

## 构建与运行

前提：Windows 10/11 x64，已安装 .NET 10 SDK（不是只安装运行时）。

```powershell
dotnet restore Dml.Launcher.sln
dotnet build Dml.Launcher.sln
dotnet test Dml.Launcher.sln
dotnet run --project src/Dml.Launcher.App
```

如 SDK 与 `global.json` 的版本不完全一致，安装同一 .NET 10 feature band 的新版 SDK 即可；项目允许向前滚动到最新 feature band。

## 发布包

每次正式发布提供两个 Windows x64 自包含产物：

- `DML-vX.Y.Z-win-x64.zip`：便携版，解压后运行 `dml.exe`。
- `dml_installer.exe`：安装版，安装到当前用户的本地程序目录，并提供开始菜单快捷方式。

两者均不需要预先安装 .NET。最近发布的 v0.2.0 包含实例管理，但不包含本分支正在开发的版本下载、账户登录或游戏启动功能。

从源码生成两个发布包（需要安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)）：

```powershell
.\scripts\Package-Release.ps1 -Version 0.1.0
```

脚本会将最终发布文件放在 `artifacts/releases/vX.Y.Z/`：包括 ZIP、安装程序，以及各自同名的 SHA-256 校验文件。构建中间文件会放在 `artifacts/staging/`，不会与可发布版本混放。最近的正式发布说明见 [v0.2.0](docs/releases/v0.2.0.md)。

## 本地数据位置

| 内容 | 路径 |
| --- | --- |
| 设置与默认游戏目录 | `%AppData%\\DML` |
| 缓存与日志 | `%LocalAppData%\\DML` |

版本清单与版本元数据缓存位于 `%LocalAppData%\\DML\\Cache\\Metadata`。未完成的游戏文件以 `.dmlpart` 保存在目标实例中，成功校验后自动改为正式文件名。

不要将账户凭据、令牌、Cookie、世界存档或付费 Mod 提交到仓库。

## 目录

```text
src/Dml.Launcher.App             WPF 界面
src/Dml.Launcher.Application     用例接口
src/Dml.Launcher.Domain          领域模型
src/Dml.Launcher.Infrastructure  设置、日志、文件系统基础设施
tests/                           自动化测试
docs/adr/                        架构决定记录
```

后续版本规划见 [develop_tasks.md](develop_tasks.md)。
