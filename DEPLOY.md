# Claude Code 全局通知 Hook — 一键部署

## 所需文件

只需 **4 个文件** 即可部署到其他电脑：

| 文件 | 说明 |
|------|------|
| `ConsoleApp1.exe` | 通知弹窗程序（编译好的 exe） |
| `PermissionRequest` | Hook 脚本（bash） |
| `.claude/settings.json` | 项目级 Hook 配置 |
| `全局 settings.json` | Claude Code 全局配置（追加 hook 部分） |

---

## 部署步骤

### 1. 安装依赖

目标电脑需要：

- **Windows 10/11**
- **.NET 8 Runtime**（仅运行时，不需要 SDK）
  - 下载：https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0
  - 选 **.NET Runtime 8.0.x** → **Windows x64** 安装即可
- **Git Bash**（Claude Code 自带或单独安装）
- **Python 3**（可选，用于解析 JSON，但建议安装）

### 2. 复制文件

```
你的目标目录/
├── bin/
│   └── ConsoleApp1.exe          ← 通知弹窗程序
└── .claude/
    ├── hooks/
    │   └── PermissionRequest     ← Hook 脚本（无扩展名）
    └── settings.json             ← 项目级 Hook 配置
```

**路径推荐**（避免中文和空格，防止编码问题）：
```
C:\tools\claude-hooks\
```

### 3. 配置 Claude Code

**文件 1：** `.claude/settings.json`（项目级，放在 Hook 脚本同目录）

```json
{
  "hooks": {
    "PermissionRequest": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "bash 'C:\\tools\\claude-hooks\\.claude\\hooks\\PermissionRequest'",
            "timeout": 60
          }
        ]
      }
    ]
  }
}
```

**文件 2：** `%USERPROFILE%\.claude\settings.json`（全局配置，若无则新建）

```json
{
  "hooks": {
    "PermissionRequest": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "bash 'C:\\tools\\claude-hooks\\.claude\\hooks\\PermissionRequest'",
            "timeout": 60
          }
        ]
      }
    ]
  }
}
```

> ⚠️ 路径中的反斜杠和单引号必须严格匹配 Windows Git Bash 格式

### 4. 修改 Hook 脚本中的 exe 路径

编辑 `PermissionRequest` 文件第 6 行，改为目标电脑的实际路径：

```bash
NOTIFY_APP="C:/tools/claude-hooks/bin/ConsoleApp1.exe"
```

> 注意用**正斜杠** `/`，不要用反斜杠 `\`

### 5. 验证

打开 Claude Code，执行一个需要权限的命令（如 `rm test.txt`），右下角应弹出带按钮的通知。

手动测试弹窗：
```bash
# 直接运行 exe
"C:\tools\claude-hooks\bin\ConsoleApp1.exe" --title "测试" --msg "hello" --type info --timeout 5

# 测试完整 Hook 链路
echo '{"type":"PermissionRequest","tool_input":{"description":"test","command":"echo hi"}}' | bash 'C:\tools\claude-hooks\.claude\hooks\PermissionRequest'
```

---

## 如果不想装 Python 3

Hook 脚本依赖 Python 3 解析 JSON。如果不装，可在 `PermissionRequest` 中改用 `jq` 或 `grep` 解析：

```bash
# 用 grep 简易提取（不依赖 Python）
TITLE=$(echo "$INPUT" | grep -oP '"description"\s*:\s*"\K[^"]+' 2>/dev/null || echo "Claude Code 需要权限")
MSG=$(echo "$INPUT" | grep -oP '"command"\s*:\s*"\K[^"]+' 2>/dev/null || echo "请查看终端")
```

> 但建议装 Python 3，解析更可靠。

---

## 常见问题

**Q: 弹窗提示"系统找不到驱动器"**
→ 路径用了反斜杠 `\`，改为正斜杠 `/`

**Q: 中文乱码**
→ 确认安装了 Python 3，Hook 脚本中有 `PYTHONIOENCODING=utf-8`

**Q: 点击按钮没反应**
→ 检查 `.claude/settings.json` 中的 `timeout` 是否 >= 30

**Q: 不想要弹窗了**
→ 删除或重命名 `settings.json` 中的 hooks 配置即可
