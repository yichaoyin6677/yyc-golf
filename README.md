# yyc-golf · 下班一杆

[![Windows build](https://github.com/yichaoyin6677/yyc-golf/actions/workflows/windows.yml/badge.svg)](https://github.com/yichaoyin6677/yyc-golf/actions/workflows/windows.yml)

一个 Windows 本地迷你高尔夫小游戏。每个人打开游戏时，都会读取自己电脑上的桌面图标，生成属于自己的球场。把球绕过那些熟悉的应用，打进“下班”文件夹前的球洞。

**[下载 Windows 免安装版](https://github.com/yichaoyin6677/yyc-golf/releases/latest)**

## 下载和启动

1. 打开上面的下载页，下载 `yyc-golf-v1.1.0-windows-x64.zip`。
2. 解压后，双击 `yyc-golf.exe` 开始游戏。

适用于 Windows 10 / 11 x64，使用系统自带的 .NET Framework 4.x。整个游戏是单文件应用。

## 你的桌面，就是球场

每次启动游戏时，会重新读取当前用户的桌面和公共桌面。应用快捷方式优先，其次是文件夹和普通文件；最多载入 24 个图标。Windows 已重定向的桌面目录也通过系统接口获取。

启动后默认进入“我的桌面球场”，左上角显示载入的本机图标数量。新增或删除桌面快捷方式后，可点击右上角“刷新桌面图标”。桌面为空或无法读取时，会使用示例图标，仍可继续游戏。

图标和文件名在本机读取、绘制，不会上传到 GitHub 或其他服务器。球场中使用图标的副本，桌面原来的文件与布局保持原样。

## 玩法

- 按住白球向后拖动，松手击球；拉得越远，力度越大。
- 虚线显示部分预测路线，右键取消蓄力。
- 图标和场地边缘会反弹，沙坑会减速，落水加罚一杆。
- 点击“摆一摆球场”，可以拖动障碍物、白球和球洞，自定义路线。
- 上方数字可选择六个设计关卡，总标准杆为 20。

| 按键 | 操作 |
| --- | --- |
| 左右方向键 | 调整击球方向，按住 Shift 加快转向 |
| 上下方向键 | 调整力度 |
| 空格 | 击球；结算后进入下一洞 |
| R | 重开当前球场 |
| H | 玩法说明 |
| M | 声音开关 |
| F11 | 全屏 / 窗口 |
| Esc | 退出游戏 |

最佳成绩保存在 `%LOCALAPPDATA%\DesktopMiniGolf\records-v1.txt`。自定义球场布局在当前游戏会话中生效。

## 构建与检查

在 Windows 上，从项目目录运行：

```powershell
.\build.ps1
```

产物为 `dist\yyc-golf.exe`。构建使用系统的 .NET Framework C# 编译器，默认只编译，不运行程序。

运行物理检查、桌面图标读取检查和窗口启动检查：

```powershell
.\build.ps1 -RunTests -SmokeTest
```

GitHub Actions 的 Windows 工作流会执行上述检查，覆盖碰撞、沙坑、落水、进洞、关卡可达性、桌面目录合并、图标加载，以及主窗口的初始化、离屏绘制、消息循环与关闭。

本程序尚未作商业代码签名；目标电脑若要求企业签名，需要由管理员按既有流程审核。制作机器因该策略无法试运行，本仓库的 Windows CI 提供独立运行检查；自动检查不等同于人工完整通关。

## 来源与许可

玩法灵感来自 [Desktop Icon Billiards](https://github.com/liuxiaogang-com/desktop-icon-billiards)。本项目使用 C# / Windows Forms 独立实现，采用 [MIT License](LICENSE)。
