# DNF 86 版 GM 管理器

自建的 **C/S 桌面版** DNF 私服 GM 工具（WinForms / .NET 10）。不占服务器资源——从本机经 SSH 隧道直连
MySQL，用完关掉即可，服务器上不需要装任何常驻服务。

## 功能

- **选角色**：按账号/角色名搜索，自动列出等级与职业，启动后自动选中第一条
- **搜物品**：22,217 条物品库，输入名称即联想；支持按 **类型 → 子类型维度** 逐层收窄
  （材料按用途/来源、装备按种类/稀有度/等级段、防具按材质/部位…），多维度为 AND
- **发邮件**：指定数量、强化、锻造、红字，可随信附带游戏币（0 = 不发）
- **无感连接**：启动即自动建隧道并连库，界面只显示「已连接 / 未连接」

## 运行环境

- Windows 10/11 x64
- [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)（Desktop Runtime）

## 构建

```bash
cd src
dotnet build
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../发布
```

产物目录需要以下文件：`DNF86GM.exe`、`items.json`（仓库根目录已有）、`key.txt`、`config.json`。

## 首次配置

仓库里**不含任何服务器地址与口令**（`src/Config.cs` 的默认值是空的），首次运行请：

1. 把 SSH 私钥改名为 `key.txt` 放到 exe 同目录（或放进 `~/.ssh/`）
2. 右键顶栏 →「连接设置」（默认访问密码 `dnf86`，进得去可改），填写
   服务器地址、SSH 用户、数据库账号与口令
3. 保存后自动重连

配置项说明见 [`config.example.json`](config.example.json)，程序会自动在其旁边生成 `config.json`。

> ⚠️ `config.json` 与 `key.txt` 是真实凭证，已在 `.gitignore` 中排除，**不要提交、不要整包外发**。

## 物品库

`items.json` 由 `tools/extract_items.py` 从游戏 `Script.pvf` 解析生成（读取一次约 3 秒），
字段与维度划分见 [`使用说明.md`](使用说明.md)。

## 软件截图
<img width="612" height="778" alt="image" src="https://github.com/user-attachments/assets/d63dbaff-ee1e-49c6-bcba-638a4b01b3ba" />

## 文档

完整使用说明、维度数据结构和踩坑记录都在 [`使用说明.md`](使用说明.md)。

## 自检

```bash
DNF86GM.exe --selftest     # 连接 / 数据库
DNF86GM.exe --uitest       # 候选列表点选与回填
DNF86GM.exe --facetest     # 子类型维度切换与组合
DNF86GM.exe --statetest    # 自动连接 / 选角 / 金币校验
DNF86GM.exe --pwtest       # 访问密码弹窗与改密
DNF86GM.exe --porttest     # 本地端口被占用时能否自动顺延（WinSock 10048）
```
