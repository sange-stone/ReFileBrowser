# ReFileBrowser

一个带标签页和收藏栏的 Windows 文件管理器。界面布局参考了 360 文件管理器，给用惯了它的人一个开源的替代品。

中间的文件列表、左侧导航窗格、状态栏和右键菜单都是系统自带的 `IExplorerBrowser`，所以和资源管理器行为一致：
右键菜单里的第三方扩展、缩略图、拖放、视图设置都照常工作，针对资源管理器文件列表的增强（例如
 Windhawk 的文件夹大小 mod）也照样生效。

- 平台：Windows 10 / 11 x64
- 运行时：[.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)
- 许可证：[MIT](LICENSE)；图标来自 Fluent UI System Icons（MIT），见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

> 本项目与 360 公司无关，不包含 360 文件管理器的任何代码或素材。

## 特点

- **快**：常驻一个进程并预先建好窗口，Win+E 打开窗口大约 60ms。
- **标签页**：新建、中键关闭、拖动排序、拖出成新窗口、拖回合并、重新打开关闭的标签。
- **收藏栏**：拖文件夹进来就添加；第一次运行时自动导入 360 文件管理器的收藏（如果装过）。
- **完整接管「打开文件夹」**：Win+E、双击文件夹、其他程序的「打开所在文件夹」都用它打开；
  配合可选的 Windhawk mod，连其他程序直接运行 `explorer.exe` 也能接住。控制面板仍交给系统。
- **查看方式按文件夹记住**，Ctrl+滚轮切换视图。

## 功能

| 区域 | 内容 |
|---|---|
| 标签栏 | 新建（默认「此电脑」）、关闭、中键关闭、拖动排序、拖出成新窗口、拖回合并、右键菜单、标签列表 |
| 收藏栏 | 点击在新标签打开；拖文件夹进来添加；右键重命名/删除；放不下的收进 » |
| 工具栏 | 新建（系统「新建」菜单）、查看方式、复制、剪切、粘贴、重命名、删除 |
| 地址栏 | 面包屑跳转、点箭头列子文件夹、点空白处手动输入、历史下拉、刷新；输入非路径时当命令运行 |
| 搜索 | 在当前文件夹用 Windows 搜索 |
| 主菜单 ☰ | 添加桌面图标、设为默认/恢复系统文件管理、开机预加载、退出 |

### 快捷键

| 按键 | 作用 |
|---|---|
| Ctrl+T / Ctrl+W | 新建标签（此电脑）/ 关闭标签 |
| Ctrl+Shift+T | 重新打开关闭的标签 |
| Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+1~9 | 切换标签 |
| Ctrl+N | 新窗口 |
| Alt+← / Alt+→ / Alt+↑ / Backspace | 后退 / 前进 / 上一级 / 后退 |
| Alt+D / Ctrl+L / F4 | 编辑地址栏 |
| Ctrl+F / Ctrl+E / F3 | 搜索框 |
| Ctrl+滚轮 | 切换查看方式（内容 → 平铺 → 详细信息 → 列表 → 小图标 → 图标 32~256） |
| Ctrl+Shift+1~8 | 超大图标 / 大图标 / 中图标 / 小图标 / 列表 / 详细信息 / 平铺 / 内容 |
| 鼠标侧键 | 后退 / 前进 |

文件列表里的 Del、F2、Ctrl+C/X/V/A、F5 等由系统控件处理。

## 安装与使用

1. 安装 .NET 10 桌面运行时，按下面「构建」一节发布到 `dist\`（或使用 Releases 里的压缩包）。
2. 运行 `ReFileBrowser.exe`。
3. 想让 Win+E 和双击文件夹都用它：主菜单 ☰ →「设为默认文件管理」（会弹一次 UAC）。
4. 想让打开更快：主菜单 ☰ →「开机预加载」。

### 命令行

```
ReFileBrowser.exe [路径]            打开路径（已有窗口时在其中新开标签）
ReFileBrowser.exe --select <项>     打开所在文件夹并选中该项
ReFileBrowser.exe --background      只启动常驻进程并预建窗口（开机预加载用）
ReFileBrowser.exe --register        接管系统「打开文件夹」（需管理员）
ReFileBrowser.exe --unregister      恢复接管前的设置（需管理员）
```

## 接管系统「打开文件夹」

| 打开方式 | 接管手段 |
|---|---|
| Win+E、SHOpenFolderAndSelectItems | `HKLM\Software\Classes\Folder\shell\opennewwindow\command`，删除 `DelegateExecute` |
| 默认动作（双击桌面上的文件夹等） | `HKCU\Software\Classes\Folder\shell` 默认值 = `ReFileBrowser` 动词 |
| `ShellExecute("open" / "explore")` | `HKCU\Software\Classes\{Directory,Drive}\shell\{open,explore}`（带 `ReFileBrowser=1` 标记） |
| 其他程序直接运行 `explorer.exe 目录` / `explorer.exe /select,文件` | 可选的 Windhawk mod，见下节 |

- 第一次接管前把原值备份到 `%APPDATA%\ReFileBrowser\registry-backup.txt`；「恢复系统文件管理」或
  `--unregister` 按备份原样写回，HKCU 下只删除带 `ReFileBrowser=1` 标记的键。
- 文件列表里双击/回车单个文件夹时通过 `ICommDlgBrowser::OnDefaultCommand` 在当前标签里导航。
- 控制面板（包括电源选项、用户帐户等页面）只能由资源管理器显示，会自动交还给资源管理器；
  其他在内嵌视图里打不开的虚拟位置也会自动转交。

### 可选：Windhawk mod，拦截直接调用 explorer.exe

很多程序的「打开所在文件夹」是直接运行 `explorer.exe /select,文件`，不经过注册表里的文件夹关联，
只改注册表拦不住。[windhawk/refilebrowser-explorer-redirect.wh.cpp](windhawk/refilebrowser-explorer-redirect.wh.cpp)
是一个 [Windhawk](https://windhawk.net) mod：在发起调用的程序里 hook `CreateProcessInternalW`，要启动的是 explorer.exe、
参数是已存在的文件系统目录或 `/select,` 已存在的项时，改为启动 ReFileBrowser。`shell:`、`::{CLSID}`、URL、
`/factory`、`-Embedding`、不认识的开关一律放行。

安装：在 Windhawk 里新建 mod，粘贴该文件内容并编译；或者运行

```
powershell -File tools\install_mod.ps1              用 Windhawk 自带的 clang 编译并安装
powershell -File tools\install_mod.ps1 -Uninstall   卸载
```

- 设置项 `exePath` 留空时使用 `--register` 登记的 exe 路径。
- Windhawk 不注入管理员权限的进程，也不注入它当作游戏的路径（如 `*\steamapps\common\*`）。
  装在 Steam 库里的程序（例如壁纸软件）要生效，需把它的 exe 加进 Windhawk 的进程包含列表。

## 用户数据

| 位置 | 内容 |
|---|---|
| `%APPDATA%\ReFileBrowser\settings.ini` | 窗口大小、是否最大化、窗格开关 |
| `%APPDATA%\ReFileBrowser\bookmarks.txt` | 收藏（`名称\t路径`，UTF-8） |
| `%APPDATA%\ReFileBrowser\history.txt` | 地址栏历史（最多 30 条） |
| `%APPDATA%\ReFileBrowser\registry-backup.txt` | 接管前的注册表原值 |
| `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\<n>\ReFileBrowser` | 每个文件夹的查看方式、排序、分组 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `ReFileBrowser` 值 | 开机预加载 |

第一次运行时，如果装过 360 文件管理器，会从 `%APPDATA%\360FileBrowser\Profile\` 导入收藏和地址历史。
之后可以在收藏栏右键「从 360 文件夹重新导入收藏」补导。

## 构建

需要 .NET SDK 10。

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o dist
```

- 发布前先退出正在运行的 ReFileBrowser（主菜单「退出」），否则文件被占用。
- 接管登记的是 exe 的完整路径，换了位置要重新「设为默认文件管理」。
- 图标：`assets\icons\*.png` 和 `assets\app.ico` 由 `tools\make_icons.ps1` 从 Fluent UI System Icons 字体生成
  （脚本会自动下载字体，字体本身不进仓库）。改图标时改脚本里的映射表再重新生成。
- 调试日志：设置环境变量 `REFILEBROWSER_LOG=1` 后启动，写 `%TEMP%\refilebrowser.log`。

## 目录结构

```
src/
  Program.cs          入口：单实例转发、--register / --unregister
  AppHost.cs          常驻进程：窗口管理、预建窗口、接收转发的命令行、视图同步
  MainForm.cs         主窗口：去标题栏外壳、标签增删/切换/拖出拖回、快捷键、Ctrl+滚轮
  MainForm.Menus.cs   工具栏、查看菜单、地址栏/搜索、收藏栏、标签菜单、主菜单
  BrowserHost.cs      一个标签 = 一个 IExplorerBrowser + 前进后退历史 + 视图保存 + 交还资源管理器
  TitleBar.cs / FavBar.cs / CmdBar.cs / NavBar.cs   四行自绘控件
  Integration.cs      接管注册表、开机启动、桌面快捷方式
  AppId.cs            AppUserModelID 和任务栏固定项修正
  Store.cs            用户数据读写和 360 收藏导入
  Theme.cs            配色、尺寸、图标加载和九宫格绘制
  Shell.cs            PIDL 封装、系统图标缓存、哪些位置交还资源管理器
  Native.cs           Win32 / Shell COM 声明
  Log.cs              调试日志
windhawk/             可选的 Windhawk mod
assets/               生成的图标
tools/
  make_icons.ps1      从 Fluent UI System Icons 生成 assets
  install_mod.ps1     编译、安装、卸载 Windhawk mod
  open_test.ps1       逐个测试 6 种「打开文件夹」方式分别由谁处理
  cp_test.ps1         用 CreateProcess 直接启动 explorer.exe，测 Windhawk mod
  probe_targets.ps1   批量打开系统位置，检查哪些能在内嵌视图里显示（需要调试日志）
  handoff_test.ps1    检查控制面板等位置是否交还资源管理器（需要调试日志）
  shot.ps1            用 PrintWindow 截窗口
```

PowerShell 脚本只用 ASCII：Windows PowerShell 5.1 按系统 ANSI 编码读取没有 BOM 的脚本。

## 卸载

1. 主菜单「恢复系统文件管理」（或管理员运行 `ReFileBrowser.exe --unregister`）。
2. 主菜单关掉「开机预加载」，然后「退出」。
3. 装了 Windhawk mod 的话：`powershell -File tools\install_mod.ps1 -Uninstall`，或在 Windhawk 里移除。
4. 删除 `%APPDATA%\ReFileBrowser\`。

## 已知问题

- 查看方式偶尔仍显示默认排列，尚未稳定复现。
- 冷启动（未开预加载）时窗口约 0.5 秒出现，之后 shell 扩展加载仍会占用一会儿 CPU。
- 界面只有中文。
