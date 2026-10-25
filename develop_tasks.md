# DML Minecraft 启动器：首个完整版本开发计划

> 目标：做一个 Windows 桌面 Minecraft: Java Edition 启动器。首个完整版本（v1.0）应能管理独立游戏实例、通过正版 Microsoft 账户启动已拥有的游戏、安装官方版本与常用加载器、从合规来源安装 Mod，并提供可靠的日志、设置与更新体验。
>
> 范围说明：项目对标的是 PCL2 的核心使用体验，不在 v1.0 复刻其全部高级功能。不会实现绕过游戏所有权验证、盗版登录或未经授权的游戏下载。

## 1. 技术决策（开发开始前确定）

| 项目 | 首选方案 | 原因 |
| --- | --- | --- |
| 平台 | Windows 10/11 x64 | 先聚焦单平台，减少复杂度 |
| 语言 / 运行时 | C#、.NET 10 LTS | Windows 集成、异步下载、长期支持好 |
| UI | WPF + MVVM（CommunityToolkit.Mvvm） | 原生桌面体验，适合复杂设置与列表 |
| IDE | VS Code + C# Dev Kit | 不必使用完整 Visual Studio；`dotnet` CLI 可构建和调试 |
| HTTP / JSON | HttpClientFactory + System.Text.Json | .NET 官方组件、易测且高性能 |
| 本地数据 | SQLite（Microsoft.Data.Sqlite）+ JSON 配置 | 管理实例、任务记录与可读配置 |
| 安全存储 | Windows Credential Manager / DPAPI | 令牌不写入明文配置文件 |
| Mod 来源 | Modrinth API（v1.0）；CurseForge 放 v1.1 | API 公开、元数据与许可证信息较完整 |
| 安装来源 | Mojang 官方版本清单与其声明的下载地址 | 不硬编码版本、校验 SHA-1 与文件大小 |
| 发布 | `dotnet publish` 自包含 x64 + MSIX/安装包 | 用户无需先装 .NET；后期签名发布 |

### 架构边界

```text
WPF 界面（Views / ViewModels）
          │
应用层（用例、任务队列、状态机、事件）
          │
领域层（实例、版本、账号、下载任务、Mod）
          │
基础设施（Mojang / Modrinth / Microsoft API、文件系统、SQLite、Java 进程、凭据库）
```

建议解决方案结构：

```text
src/Dml.Launcher.App          WPF 启动项目
src/Dml.Launcher.Application  用例与后台任务
src/Dml.Launcher.Domain       领域模型与接口
src/Dml.Launcher.Infrastructure HTTP、存储、进程实现
tests/Dml.Launcher.*.Tests    单元 / 集成测试
docs/                         协议、架构决定与用户文档
```

## 2. 小版本路线图与开发任务

每个版本都应可构建、可手测、可回归；下一版本只在前一版验收后开始。

### v0.1 — 项目骨架与质量基线

- 创建上述 .NET 解决方案、WPF MVVM 页面框架与依赖注入。
- 配置 `.editorconfig`、中央包版本管理、日志（文件滚动 + 界面查看）、本地化资源（先中文）。
- 配置单元测试、格式检查、GitHub Actions（还原、构建、测试）。
- 做设置页：游戏根目录、下载并发数、代理、主题；设置可持久化。
- 验收：全新电脑可按 README 构建；启动后可修改设置，重启不丢失；异常会写入脱敏日志。

### v0.2 — 实例与文件系统管理

- 定义实例模型：名称、Minecraft 版本、加载器、Java 路径、游戏目录、内存、分辨率、最后启动时间。
- 实现创建、复制、重命名、删除（先移入回收站）、导入已有 `.minecraft` 目录。
- 每实例使用独立目录，禁止意外覆盖其他实例或官方启动器目录。
- 实现实例列表、详情页与磁盘空间提示。
- 验收：能创建、复制、删除和导入实例；每项操作都有确认和可读错误提示。

### v0.3 — 官方版本发现与可靠下载

- 拉取 Mojang 版本清单；显示 release / snapshot 及发布日期。
- 下载版本 JSON、client JAR、libraries、assets；支持断点续传、并发、暂停/恢复、取消与重试。
- 下载前后校验大小和 SHA-1；以临时文件写入后原子替换；失败文件绝不视为完成。
- 显示任务进度、速率、剩余数量和错误原因；缓存元数据并支持刷新。
- 验收：可为一个新实例完整安装一个稳定版；断网/关机后可恢复；损坏文件会自动重新下载。

### v0.4 — Java 运行时与原版启动

- 检测系统 Java，读取版本、位数与路径；允许用户手动选择 Java。
- 基于版本元数据解析 `arguments`、libraries、natives、asset index 与规则，生成启动命令。
- 实现 JVM 内存、JVM 参数、窗口尺寸、全屏等实例级设置；提供“复制启动命令”诊断功能。
- 启动 Java 子进程，实时捕获 stdout/stderr，记录启动/退出状态与崩溃日志。
- 验收：在无登录的开发占位账号模式下，能启动一个已安装的原版实例并在退出后显示日志。（发布版本不将此模式作为绕过正版验证的功能。）

### v0.5 — 正版 Microsoft 登录与账户安全

- 在 Microsoft Entra 注册桌面公开客户端应用；使用授权码 + PKCE 和系统浏览器，不在程序内收集密码。
- 完成 Xbox Live、XSTS、Minecraft Services 令牌交换；校验游戏所有权与获取玩家档案。
- 用 Windows 安全存储保存刷新令牌；支持账户列表、切换、登出与令牌过期重登。
- 将正版账户资料注入启动参数；所有日志与界面都脱敏令牌、Cookie 和个人信息。
- 验收：拥有 Java Edition 的 Microsoft 账号可登录、重启后刷新并启动游戏；未拥有游戏的账号得到明确提示而非模糊失败。

### v0.6 — 加载器与版本组合

- 首先支持 Fabric 与 Forge；为 NeoForge / Quilt 预留安装器接口，不在首版承诺全部兼容。
- 下载并校验官方/加载器发布的安装器或元数据；把加载器生成的版本记录归属到实例。
- 检查 Minecraft、加载器和 Java 的兼容矩阵，安装前显示冲突。
- 验收：可创建“指定 MC 版本 + Fabric/Forge”的独立实例，并成功进入游戏主菜单。

### v0.7 — Mod 浏览、安装与配置

- 接入 Modrinth：搜索、筛选（游戏版本 / 加载器 / 分类）、项目详情、版本列表、许可证和依赖展示。
- 安装、更新、禁用、启用、移除 Mod；移除移入回收站或备份目录。
- 自动解析必需依赖，冲突或不支持时先提示用户；将下载任务接入 v0.3 队列与校验机制。
- 提供 `mods`、`config`、`resourcepacks`、`shaderpacks` 文件夹快捷打开和基础文件管理。
- 验收：能从搜索结果安装一个带依赖的 Mod，启停后可生效；错误的加载器或版本组合会被阻止并说明原因。

### v0.8 — 稳定性、诊断与可用性

- 崩溃识别：解析常见 Java/加载器错误，给出链接到实例日志与初步建议（不伪造诊断结论）。
- 启动前检查：磁盘、Java、网络、必需文件、端口/权限；实现“修复实例”（重新校验并补下载）。
- 支持导入/导出实例包（明确包含项、排除账户凭据）；提供安全备份/恢复。
- 键盘操作、缩放、高对比度、加载占位与空状态等可访问性打磨。
- 验收：故意删除库、使用错误 Java、损坏 Mod 等情形可给出可操作的恢复路径；实例导出后可在另一台机器导入。

### v0.9 — 测试、分发与发布候选版

- 覆盖版本参数解析、下载校验、依赖解析、路径安全、令牌脱敏等核心单元测试。
- 使用隔离临时目录与模拟 HTTP 做集成测试；准备最少三种人工冒烟组合：原版、Fabric+Mod、Forge+Mod。
- 添加隐私政策、第三方许可证清单、开源组件 NOTICE、问题反馈模板和用户文档。
- 配置版本号、自动更新策略（先只提示手动下载）、自包含发布与安装/卸载测试。
- 验收：干净 Windows 虚拟机可安装、登录、下载、启动、卸载；测试与冒烟清单全部通过。

### v1.0 — 首个完整版本发布

- 修复发布候选版的 P0/P1 缺陷，冻结功能范围，建立已知问题清单。
- 对安装包进行代码签名（如准备公开发布），在真实账户和多个 Java/系统环境下最终回归。
- 发布安装包、SHA-256 校验值、变更日志和反馈入口；建立崩溃报告与版本更新响应流程。
- 验收：发布包可重复构建、可验证、可安装，并完成 v0.9 的全部用户路径。

## 3. 开发前准备清单

### 账户、合规与服务准备

- [ ] 使用一个拥有 Minecraft: Java Edition 的 Microsoft 账户作为测试账号；不要把私人主账户令牌、密码或截图提交到仓库。
- [ ] 在 Microsoft Entra 管理中心注册“桌面 / 公共客户端”应用，记录 **Client ID**，配置桌面回调地址。公开客户端没有也不应嵌入 client secret。
- [ ] 在隐私政策中说明：使用 Microsoft/Xbox/Minecraft 登录令牌仅用于启动和刷新登录；令牌保存在本机 Windows 凭据库，用户登出即可删除。
- [ ] 确认 Microsoft、Mojang、Modrinth 和后续 CurseForge 的当前 API 条款、品牌使用与速率限制。CurseForge 接入前需要申请自己的 API key，且不可把密钥直接提交到开源仓库。
- [ ] 确定应用名、图标、包标识和公开下载域名；如要向普通用户发布，准备代码签名证书，减少 SmartScreen 警告。
- [ ] 创建 GitHub/GitLab 远程仓库，启用私有变量/Secrets、分支保护、Issue 模板与发布页。

### Windows 与命令行工具

- [ ] Windows 10/11 x64，建议至少 16 GB 空闲磁盘（测试不同版本、资源文件与缓存会较大）。
- [ ] 安装最新稳定的 .NET 10 SDK；运行 `dotnet --info` 验证。
- [ ] 安装 Git for Windows，设置提交身份；运行 `git --version` 验证。
- [ ] 安装 PowerShell 7（建议）及 Windows Terminal（建议）。
- [ ] 安装至少一个测试 Java：Java 21（现代版本常用）。后续按 Minecraft 版本补充 Java 8、17、21；启动器必须自行检测而不能假设唯一 Java。
- [ ] 安装 Minecraft 官方启动器仅作对照测试（目录结构、登录后的正常启动结果），不依赖或修改其文件。

### VS Code 与扩展

- [ ] 安装 [Visual Studio Code](https://code.visualstudio.com/)。
- [ ] 安装 **C# Dev Kit**（微软）：C# 语言服务、解决方案管理、调试和测试入口。
- [ ] 安装 **C#**（微软）：通常由 C# Dev Kit 一并处理；确认已启用并使用最新稳定版。
- [ ] 安装 **.NET Install Tool**（微软）：辅助按项目要求安装 SDK。
- [ ] 安装 **EditorConfig for VS Code**：使团队代码风格自动生效。
- [ ] 安装 **GitLens**（可选）：便于查看 Git 历史与改动归属。
- [ ] 安装 **REST Client** 或 **Thunder Client**（可选）：手动验证 Mojang、Modrinth 等 HTTP 响应。
- [ ] 安装 **XAML**（Red Hat 或微软生态中维护活跃的扩展）：XAML 语法高亮和辅助编辑。以实际扩展市场中与当前 VS Code / C# Dev Kit 兼容的版本为准。
- [ ] 不建议一开始安装大量“AI 自动补全”或未经验证的 XAML 生成扩展；先保持扩展少且可复现。

### 本仓库初始文件与约定

- [ ] 创建 `Dml.Launcher.sln` 及 `src/`、`tests/`、`docs/` 目录；项目目标为 `net10.0-windows`，开启 nullable、隐式 using 和 analyzers。
- [ ] 写入 `.gitignore`（`bin/`、`obj/`、本地数据库、下载缓存、日志、测试账号配置、密钥）。
- [ ] 写入 `.editorconfig`、`Directory.Packages.props`、`global.json`（锁定团队 SDK）与 `README.md`。
- [ ] 预先定义路径约定，例如 `%AppData%\\DML` 保存应用数据、`%LocalAppData%\\DML\\Cache` 保存可重建缓存；禁止把可执行内容放入 `Program Files` 后再尝试写入。
- [ ] 定义配置文件 schema、迁移策略、日志保留天数与“绝不记录的字段”（token、授权码、密码、Cookie）。
- [ ] 在 `docs/adr/` 记录架构决定：MVVM、实例隔离、账户安全存储、下载校验、Mod 来源与更新策略。

### 测试资产与安全基线

- [ ] 为每个目标 Java 大版本准备一份测试矩阵；记录 Windows 版本、GPU、Java 路径、MC/加载器/Mod 组合。
- [ ] 准备可公开使用的轻量测试 Mod 和测试实例，不把付费 Mod、账户凭据或用户世界存入仓库。
- [ ] 设计下载失败、磁盘满、网络代理、文件锁定、损坏缓存、令牌失效和异常退出的测试用例。
- [ ] 先搭建 CI：拉取依赖、构建、单元测试、格式验证、打包；任何凭据只用 CI Secrets。
- [ ] 开发环境启用 Windows Defender；所有外来文件先校验哈希，解压时防 Zip Slip（禁止写到目标目录之外）。

## 4. 首次启动开发的顺序

1. 完成“账户、合规与服务准备”中 Microsoft 应用注册、测试账号和仓库三项。
2. 安装 .NET SDK、Git、VS Code 及 C# Dev Kit，确认 `dotnet --info`、`git --version` 可用。
3. 初始化 v0.1 项目骨架、质量工具和 CI，不先写下载或登录功能。
4. 依次完成 v0.2 到 v0.4，先证明本地实例、下载和启动链路可靠。
5. 再接入 v0.5 正版登录，最后进入加载器、Mod 与发布阶段。

## 5. 关键外部文档

- Microsoft 建议桌面应用使用 OAuth 2.0 授权码流程配合 PKCE，并用支持的认证库处理令牌；参见 [Microsoft identity platform: authorization code flow](https://learn.microsoft.com/azure/active-directory/develop/v2-oauth2-auth-code-flow)。
- Microsoft 的桌面应用登录示例和回调地址配置说明见 [Desktop app sign-in quickstart](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-desktop-app-sign-in)。
- Minecraft 版本元数据应始终从官方版本清单获取；实现时记录实际使用的清单 URL、哈希和下载协议，避免依赖第三方镜像作为唯一来源。

## codex/v0.3 实现进度

- [x] 从 Mojang 官方 `version_manifest_v2.json` 读取并缓存 release / snapshot 与发布日期。
- [x] 解析版本 JSON、Windows 库规则、asset index，并生成实例内的完整下载计划。
- [x] 下载 client、libraries、natives 和 assets；按设置控制并发数。
- [x] 支持暂停、恢复、取消、三次自动重试与 HTTP Range 断点续传。
- [x] 校验文件大小与 SHA-1，以 `.dmlpart` 临时文件下载并在校验成功后原子替换。
- [x] 显示文件/字节进度、平均速率、剩余文件数和可读错误原因。
- [x] 增加续传、损坏文件重下、清单缓存回退和完整安装计划的自动化测试。
- [ ] 在实际 Windows 用户目录中手动安装一个当前稳定版，并模拟断网、退出重启与损坏文件恢复。

