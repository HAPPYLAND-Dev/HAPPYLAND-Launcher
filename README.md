# HAPPYLAND Launcher

C# 启动器和 ASP.NET Core 后端。启动器使用 Avalonia，Minecraft 核心基于 [HyperionMinecraftLauncher](https://github.com/TechTeaStudio/HyperionMinecraftLauncher)。支持 Windows、Linux 和 macOS。

## 后端

当前后端部署在 `~/Minecraft/Launcher`，固定地址为 `https://port.happylandmc.cc:52130/`，运行于 `launcher` tmux 会话。部署目录的 `Start.sh` 使用内置 .NET 运行时启动后端，生产配置保存在该目录的 `appsettings.Production.json`。Kestrel 使用域名证书提供 HTTPS，客户端使用系统证书验证。证书和私钥只保存在服务器，发布包不包含它们。微软客户端 ID 是应用的公开标识，不作为密钥使用。

从源码构建需要 .NET 10 SDK。`src/HAPPYLAND.Server/appsettings.json` 是公开示例，复制为同目录的 `appsettings.Production.json` 后填写实际配置。环境配置文件已被 Git 忽略，也可使用 `Publication__Version` 等环境变量覆盖。

```json
{
  "Publication": {
    "PublicBaseUrl": "https://launcher.example.com/",
    "Version": "2026-01",
    "MrpackPath": "/srv/happyland/current.mrpack",
    "MicrosoftClientId": "499c8d36-be2a-4231-9ebd-ef291b7bb64c",
    "Servers": [
      {
        "Id": "main", "Name": "HAPPYLAND", "Host": "play.example.com", "Port": 25565,
        "Routes": [
          { "Id": "overseas", "Name": "海外", "Host": "overseas.example.com", "Port": 50002 },
          { "Id": "domestic", "Name": "国内", "Host": "domestic.example.com", "Port": 50001 }
        ]
      }
    ],
    "SkinServers": [
      { "Id": "littleskin", "Name": "LittleSkin", "Url": "https://littleskin.cn/api/yggdrasil" }
    ],
    "HomeNotice": "",
    "Shortcuts": [
      { "Id": "littleskin", "Name": "LittleSkin", "Url": "https://littleskin.cn/", "Icon": "skin" },
      { "Id": "wiki", "Name": "Wiki", "Url": "https://example.com/wiki", "Icon": "wiki" }
    ],
    "Announcements": []
  }
}
```

`Version` 和 `MrpackPath` 指向当前整合包；发布新版本时更新配置。相对文件路径以服务端项目目录为基准。`PublicBaseUrl` 是客户端访问后端的公开地址，反向代理部署时必须设置；直接访问时可留空。配置文件更改后由 ASP.NET Core 自动重载。服务器列表至少包含一项。微软应用需要启用公共客户端和设备代码流；新应用还需通过 [Minecraft API 应用审核](https://aka.ms/mce-reviewappid)，才能使用 Minecraft 登录接口。

每台服务器的 `Host` 和 `Port` 定义主线路，启动器固定使用标识 `main` 和名称“主线路”。`Routes` 只填写附加线路，可省略或设为空数组；每项的 `Id`、`Name`、`Host` 必须非空，`Port` 范围为 1 到 65535。同一服务器内附加线路的 `Id` 必须唯一，且不能使用 `main`。主线路支持 SRV 解析；附加线路使用各自配置的地址和端口。

`HomeNotice` 是游玩页的常驻文本，可留空。它与 `Announcements` 更新公告完全独立：修改首页文本不会增加更新公告，发布更新公告也不会替换首页文本。

`Shortcuts` 控制游玩页底部链接按钮的数量、名称、地址和图标，空数组会隐藏这些链接。每项的 `Id` 必须唯一，`Name` 为按钮名称，`Url` 为绝对 HTTP 或 HTTPS 地址。`Icon` 可使用内建的 `skin`、`wiki`、`link`，也可填写绝对 HTTP 或 HTTPS 图片地址。游戏文件夹和更新公告入口由启动器固定提供，不放入此数组。

`IconPixels` 可为某个快捷链接指定 16 行字符串，每行恰好 16 个 `0` 或 `1`。`1` 使用当前主题的图标颜色绘制，`0` 透明；配置后优先于 `Icon`，显示区域仍为 16×16 逻辑像素。后端会拒绝行数、列数或字符不符合要求的矩阵。

`Announcements` 保存更新公告及历史记录。每项包含唯一的 `Id`、非空的 `Title` 和 `Content`，以及使用 ISO 8601 格式并带时区的 `PublishedAt`。空数组表示暂无公告。接口按发布时间倒序返回完整正文，相同时间按 `Id` 排序。

`LauncherUpdate` 单独管理启动器版本公告，`LatestVersion` 为最新版本的 Unix 秒级时间戳，`Releases` 保存历史记录。每条记录包含 `Version`（同样为秒级时间戳）、`Mandatory`、`Title`、`Content`。默认 `LatestVersion: 0` 和空 `Releases` 表示尚未发布更新公告。最新版必须存在对应记录，版本号不能重复，历史记录保留供跨版本检查使用。例如：

```json
"LauncherUpdate": {
  "LatestVersion": 1793923200,
  "Releases": [
    { "Version": 1793836800, "Mandatory": true, "Title": "必要更新", "Content": "此处填写更新说明。" },
    { "Version": 1793923200, "Mandatory": false, "Title": "新版启动器", "Content": "此处填写更新说明和手动获取新版的方法。" }
  ]
}
```

`GET /api/launcher/updates?after=<当前时间戳版本号>` 返回最新版本号，以及 `(当前版本, 最新版本]` 区间内按版本倒序排列的公告；该接口独立于整合包配置。客户端打开时和启动游戏前检查这个区间，只要其中一条记录的 `Mandatory` 为 `true`，就显示不可关闭的更新公告，阻止使用主界面；仍可退出整个启动器。普通更新在同一次运行中针对同一个最新版只提示一次。弹窗没有更新按钮，程序不自动替换自身。已有的 `Announcements` 及底部公告入口保持独立。

运行：`dotnet run --project src/HAPPYLAND.Server --no-launch-profile -- --environment Production --urls http://127.0.0.1:5197`。此命令读取私有的 `appsettings.Production.json`。`GET /api/launcher` 返回整合包、账号、服务器、首页文本和快捷链接配置；`GET /api/pack` 提供支持范围请求的整合包下载；`GET /api/announcements` 返回全部更新公告，该接口在整合包尚未配置或文件不存在时也可使用。对外提供服务时配置 HTTPS，或通过 HTTPS 反向代理转发。GitHub Release 的后端包已附带运行时；自行使用框架依赖模式发布时需要 ASP.NET Core 10 运行时。

## 启动器

构建解决方案：`dotnet build 'HAPPYLAND Launder.sln'`。运行：`dotnet run --project src/HAPPYLAND.Launcher`。后端地址固定在 `src/HAPPYLAND.Launcher/LauncherDeployment.cs`，当前为 `https://port.happylandmc.cc:52130/`；启动器自动连接。未添加账号时，主按钮禁用并显示“请添加账号”；添加账号后选择服务器，下载或启动游戏。Java 由启动器按整合包要求自动管理，不提供地址或路径设置。

界面使用随程序打包的 Fusion Pixel 字体，默认启用“林间小径”背景并跟随背景取色，提供八套预设配色、强调色、字号、页面动画及 100%、125%、150% 界面缩放。缩放选择自动保存。首次启动使用 960×570 的逻辑窗口大小（150% 界面缩放），由系统处理屏幕 DPI；已有窗口尺寸会迁移并继续保存。外观页面提供四张内置壁纸缩略图，右侧“+”可添加自己的图片，每张图分别保存图片强度和暗化程度。可以完全按背景取色，也可关闭自动取色使用主题预设。设置背景后，面板和控件使用半透明配色。首页显示在线人数和所选线路延迟，线路列表分别显示各线路延迟；无法获取的数值显示为横线。首页常驻文本与底部更新公告历史独立。底部还提供后端配置的快捷链接及游戏文件夹入口，左栏底部显示启动器版本。字体许可证见 `src/HAPPYLAND.Launcher/Assets/Fonts/LICENSE-OFL.txt`。

游戏选项提供内存滑块和数值输入、窗口大小预设及自定义宽高、全屏、视距、帧率、垂直同步、界面大小、亮度和声音设置，保存后在下一次启动游戏时应用。Minecraft 的界面语言跟随系统显示语言，按当前游戏版本的语言资源匹配，区域变体不可用时使用同语言的默认区域。写入 `options.txt` 时保留其他选项。可记住启动器窗口大小，选择启动页面及启动游戏后是否最小化窗口。维护操作在主窗口内确认：卸载删除 Minecraft 版本、资源、依赖和托管 Java，保留存档、Mod、配置与整合包记录；重装运行库只替换游戏所需的托管 Java，不重下游戏文件。

游戏启动前检查整合包版本和校验值；版本不一致时先更新。整合包使用的 Mod 可在界面中启用或关闭；更新时按路径或 Mod 标识保留关闭状态。安装过程发生异常或意外中断后恢复已修改的受管文件。Minecraft 下载优先使用 BMCLAPI，遇到请求失败时使用官方地址。连接后同步后端全部服务器及其线路到游戏内服务器列表，并默认接受这些线路的服务器资源包；已有图标和其他自行添加的服务器保留。

添加微软账号时，启动器会显示验证码并自动打开系统默认浏览器。在微软登录页输入验证码并完成授权，账号会自动添加到启动器。如果浏览器没有打开，可以重新打开登录页，也可以复制页面地址手动访问。皮肤站账号使用后端允许的 Yggdrasil 服务。皮肤站登录令牌保存在 Windows 凭据管理器、macOS 钥匙串或 Linux Secret Service 中，密码不保存，账号 JSON 仅保存显示信息。启动前验证令牌，必要时自动刷新；令牌无法恢复时才重新登录。Linux 需要安装 libsecret 并启用桌面密钥环服务。账号可通过添加按钮的菜单删除。操作失败时显示 Toast，程序异常或请求失败的提示结束后，在右上角保留像素感叹号按钮；点击可在主窗口内查看并复制错误详情。缺少输入、账号密码错误等操作提示只显示 Toast。

## 自动构建与发布

推送 `main`、向 `main` 提交 Pull Request，或在 [Actions](https://github.com/HAPPYLAND-Dev/HAPPYLAND-Launcher/actions/workflows/build-release.yml) 手动运行工作流，会构建 Windows x64、Linux x64、macOS Intel 和 macOS Apple Silicon。每个平台分别打包启动器和后端，压缩包及 SHA-256 校验文件可从该次运行的 Artifacts 下载，保留 7 天。

推送 `v1.2.3` 这样的版本 tag 后，全部平台构建成功才会创建并公开 GitHub Release，附带八个压缩包、`SHA256SUMS.txt` 和自动生成的发布说明。`v1.2.3-rc.1` 这样的 tag 会发布为预发布版本。tag 中的版本号写入程序；普通 CI 构建使用 `Directory.Build.props` 中的显示版本，当前为 `1.0.1`。用于更新比较的 `LauncherVersionCode` 取 Git 提交的 Unix 秒级时间戳，四个平台一致；每份 Actions Artifact 内的 `BUILD-INFO-<平台>.json` 记录显示版本、时间戳和提交 SHA，配置后端版本时使用其中的 `versionCode`。上传失败时 Release 保持草稿，重新运行工作流可继续上传。

```sh
git tag v1.2.3
git push origin v1.2.3
```

Windows 使用 ZIP，Linux 和 macOS 使用保留可执行权限的 `tar.gz`。Windows 和 Linux 的启动器各为一个可执行文件；macOS 启动器保留可双击的 `.app`，其中包含一个可执行文件、图标和应用信息。压缩包同时附带说明与许可证。程序已包含 .NET 运行时，首次运行时会将所需的原生组件释放到系统用户缓存，启动器配置和游戏目录的位置不变。macOS 应用采用本地临时签名，尚未使用 Apple 开发者证书签名或公证；Windows 包尚未进行 Authenticode 签名。Linux 桌面仍需要系统图形库和上述 libsecret 服务。

后端包包含一个可执行文件、公开的 `appsettings.json` 及说明和许可证。部署时在解压目录复制为 `appsettings.Production.json`，填写整合包、账号及服务器配置，然后从该目录运行 `HAPPYLAND.Server`（Windows 为 `HAPPYLAND.Server.exe`）。环境配置文件不会进入发布包，原有配置文件的位置不变。

本地打包需要 .NET 10 SDK 和 Python 3.11 以上版本，例如 `python scripts/package.py --runtime linux-x64 --version 1.2.3`，输出在 `artifacts/`，可用 `--version-code <秒级时间戳>` 显式指定更新比较版本。直接 `dotnet build` 使用当前 UTC 时间的秒级时间戳。macOS `.app` 的打包与签名在 macOS 上执行。

## 许可证

本项目采用 [MIT License](LICENSE)。`src/Hyperion/` 包含基于 HyperionMinecraftLauncher 修改的核心源码，保留 Tech Tea Studio 的 [MIT 许可证](src/Hyperion/LICENSE.txt)。Fusion Pixel 字体来自 [TakWolf/fusion-pixel-font](https://github.com/TakWolf/fusion-pixel-font)，采用 [SIL Open Font License 1.1](src/HAPPYLAND.Launcher/Assets/Fonts/LICENSE-OFL.txt)。其他 NuGet 依赖遵循各自的许可证。

内置背景由维护者提供，署名 Wallpaper Alchemy，包含林间小径、秘境花园、阳光温室和高山湖畔。这些图片不在项目 MIT 许可证范围内，来源说明随发布包附带。

仓库提供启动器和后端源码，Minecraft、Java 运行环境、整合包及 Mod 由程序按配置下载。部署配置、账号缓存、系统凭据、日志、截图和本地验证目录不包含在仓库中。
