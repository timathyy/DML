# ADR 0002：本地数据与日志安全

## 状态

已接受（2026-10-22）。

## 决定

可读设置保存在 `%AppData%\\DML\\settings.json`；可重建缓存与滚动日志保存在 `%LocalAppData%\\DML`。设置保存使用临时文件后替换，避免半写入文件。日志最多保留 14 天，且必须掩盖 Bearer 令牌及 access token、refresh token、授权码、密码和 Cookie。

未来的登录刷新令牌只存 Windows 凭据库或 DPAPI 保护的存储，绝不进入 JSON 配置和日志。
