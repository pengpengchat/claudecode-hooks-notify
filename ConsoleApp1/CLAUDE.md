# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

Claude Code 全局 Hook 通知系统。当 Claude Code 需要用户权限时，右下角弹出交互式通知窗口，按钮与终端动态一致（2 或 3 个选项），点击后以 `hookSpecificOutput` 格式回复 stdout，Claude Code 自动处理。

## 构建与测试

```bash
# 构建 Release
cd ConsoleApp1 && dotnet build -c Release

# 无警告无错误构建
dotnet build -c Release 2>&1 | findstr /V "已成功生成\|0 个警告\|0 个错误"
```

```bash
# 手动测试通知弹窗
bin/Release/net8.0-windows/ConsoleApp1.exe --title "测试" --msg "消息" --type info --actions "Yes|allow||No|deny" --timeout 10 --sound on

# 测试带结果回写的弹窗
rm -f /tmp/t.txt && bin/Release/net8.0-windows/ConsoleApp1.exe --title "测试" --msg "消息" --type permission --actions "Yes|allow||No|deny" --result-file /tmp/t.txt --timeout 10 && cat /tmp/t.txt
```

```bash
# 测试完整 Hook 链路
echo '{"type":"PermissionRequest","tool_input":{"description":"test","command":"echo hi"}}' | bash '.claude/hooks/PermissionRequest'
```

## 项目结构

```
ConsoleApp1/
├── ConsoleApp1.csproj          # .NET 8 WinForms 项目 (WinExe)
├── Program.cs                  # 入口: 参数解析 → 启动 NotificationForm
├── NotificationForm.cs         # 核心: 右下角无边框弹窗 + 动画 + 交互按钮
├── NotificationForm.resx       # WinForms resx
└── .claude/
    ├── hooks/
    │   └── PermissionRequest   # Hook 脚本 (bash + Python)
    └── settings.json           # 项目级 Hook 配置
```

## 架构与数据流

```
Claude Code 触发 PermissionRequest
  │ stdin: JSON {tool_input: {description, command}}
  ▼
Hook 脚本 (.claude/hooks/PermissionRequest)
  ├─ Python 解析 JSON → 提取 tool_name, tool_input, permission_suggestions
  ├─ 按工具类型构建与终端一致的提示消息:
  │   Bash     → description + "$ command"
  │   Write    → description + "写入文件: path" + "内容: N 字节" + 预览
  │   Read     → description + "读取文件: path"
  │   Edit     → description + "修改文件: path" + "原内容/新内容"
  │   Glob     → description + "搜索模式: pattern" + "搜索目录: path"
  │   Grep     → description + "搜索内容: pattern" + "搜索路径: path"
  │   WebSearch → description + "搜索: query"
  │   WebFetch  → description + "获取 URL: url" + "查询: prompt"
  │   通用兜底 → description + 关键字段
  ├─ 根据 suggestions 动态决定按钮:
  │   localSettings → Allow | Allow and remember | Deny (3项)
  │   其他/无       → Allow | Deny (2项)
  ├─ 写入解析结果到 JSON 临时文件 (UTF-8, 经 cygpath -m 转 Windows 路径)
  ├─ Bash 读取 JSON → 获取 title/msg/actions (指定 encoding='utf-8')
  ├─ 启动 ConsoleApp1.exe → 显示弹窗并等待
  ├─ 用户点击 → exe 写行为值 (allow/allowRemember/deny) 到临时文件
  ├─ 读取结果 → 去掉 UTF-8 BOM
  └─ stdout: hookSpecificOutput {behavior: allow|deny}
  ▼
Claude Code 接收响应 → 自动批准或拒绝
```

## 关键技术决策

| 决策 | 原因 |
|------|------|
| `GetAsyncKeyState(VK_LBUTTON)` 防护 | `WS_EX_NOACTIVATE` 窗口会合成完整的鼠标事件序列（MouseEnter→MouseClick），必须用物理按键检测区分真假点击 |
| 按钮用 `MouseDown` 而非 `Click`/`MouseClick` | `MouseClick` 触发时鼠标已释放，`GetAsyncKeyState` 返回 0；`MouseDown` 时按键仍在按下状态 |
| `Encoding.UTF8` 写文件 + BOM 问题 | `File.WriteAllText` 默认写 UTF-8 BOM，bash 中 `case` 匹配失败，需要用 `sed 's/^\xef\xbb\xbf//'` 剥离 |
| `label|behavior||label|behavior` 格式 | 按钮文字可能含逗号（如 "Yes, and do not ask again"），不能用逗号作分隔符 |
| `Semaphore(3)` 而非 `Mutex` | 允许多条通知同时显示（最多 3 条） |
| FixEncoding() | Git Bash 传 UTF-8 中文到 .NET 时被解释为 GBK，需重新解码 |
| `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` | 弹窗不抢焦点、不占任务栏 |
| 动态宽度 (320~600px) + 按钮上限 160px | 适应长文字（如 "Yes, and do not ask again"） |
| `cygpath -m` 转换路径 | `mktemp` 返回 MSYS2 风格 `/tmp/...`，Windows Python 无法识别，需转为 Windows 路径 |
| `open(encoding='utf-8')` | Windows Python 默认 `open()` 用 GBK 编码，读取 UTF-8 JSON 文件时解码失败 |
| 工具类型消息模板 | 每种工具提取 tool_input 关键字段，生成与终端提示一致的消息 |

## 命令行参数 (ConsoleApp1.exe)

| 参数 | 说明 |
|------|------|
| `--title` / `-t` | 标题（默认 "Claude Code 提醒"） |
| `--msg` / `-m` | 消息正文 |
| `--type` / `-T` | info / warning / error / permission |
| `--actions` / `-a` | 按钮列表，`label|value` 格式，`||` 分隔 |
| `--result-file` / `-r` | 结果文件路径（点击后写入选中的 value） |
| `--timeout` / `-d` | 自动关闭秒数（默认 15，最大 600） |
| `--sound` / `-s` | on / off |
| `--debug` | 日志到 `%TEMP%\ClaudeCodeNotify.log` |

## Hook 脚本关键点

- `NOTIFY_APP` 路径用正斜杠 `/`
- Python JSON 解析用 `PYTHONIOENCODING=utf-8`
- 临时文件用 `mktemp` 创建 IPC 文件
- 响应格式必须用 `hookSpecificOutput`（非 `{"value": N}`）
- `timeout` 需 ≥ 30s（等用户看弹窗并点击）
- 结果文件需去 BOM 后再 case 匹配

## 调试

```bash
# 日志文件
cat /c/Users/Administrator/AppData/Local/Temp/ClaudeCodeNotify.log
cat /c/Users/Administrator/AppData/Local/Temp/ClaudeCodeNotify_debug.log
```

## 部署

见 `DEPLOY.md`。核心：目标机器需装 .NET 8 Runtime，复制 `ConsoleApp1.exe` + `PermissionRequest` 脚本 + `settings.json`，改路径即可。
