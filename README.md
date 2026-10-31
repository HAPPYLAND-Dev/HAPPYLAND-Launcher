# HAPPYLAND Launcher

C# 启动器和 ASP.NET Core 后端。启动器使用 Avalonia，Minecraft 核心基于 [HyperionMinecraftLauncher](https://github.com/TechTeaStudio/HyperionMinecraftLauncher)。支持 Windows、Linux 和 macOS。

## 后端

从源码构建需要 .NET 10 SDK。`src/HAPPYLAND.Server/appsettings.json` 是公开示例，复制为同目录的 `appsettings.Production.json` 后填写实际配置。环境配置文件已被 Git 忽略，也可使用 `Publication__Version` 等环境变量覆盖。

```json
{
  "Publication": {
    "PublicBaseUrl": "https://launcher.example.com/",
    "Version": "2026-01",
    "MrpackPath": "/srv/happyland/current.mrpack",
    "MicrosoftClientId": "微软应用客户端 ID",
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

`Announcements` 保存更新公告及历史记录。每项包含唯一的 `Id`、非空的 `Title` 和 `Content`，以及使用 ISO 8601 格式并带时区的 `PublishedAt`。空数组表示暂无公告。接口按发布时间倒序返回完整正文，相同时间按 `Id` 排序。

运行：`dotnet run --project src/HAPPYLAND.Server --no-launch-profile -- --environment Production --urls http://127.0.0.1:5197`。此命令读取私有的 `appsettings.Production.json`。`GET /api/launcher` 返回整合包、账号、服务器、首页文本和快捷链接配置；`GET /api/pack` 提供支持范围请求的整合包下载；`GET /api/announcements` 返回全部更新公告，该接口在整合包尚未配置或文件不存在时也可使用。对外提供服务时配置 HTTPS，或通过 HTTPS 反向代理转发。运行已发布的后端需要 ASP.NET Core 10 运行时。

## 启动器

构建解决方案：`dotnet build 'HAPPYLAND Launder.sln'`。运行：`dotnet run --project src/HAPPYLAND.Launcher`。后端地址固定在 `src/HAPPYLAND.Launcher/LauncherDeployment.cs`，当前为 `http://127.0.0.1:5197/`；启动器自动连接。添加账号，选择服务器后下载或启动游戏。Java 由启动器按整合包要求自动管理，不提供地址或路径设置。

界面使用随程序打包的 Fusion Pixel 字体，默认主题为雾蓝，提供八套预设配色、强调色、背景强度、字号和页面动画设置。默认无背景图片；导入自己的图片后可完全按背景取色，也可关闭自动取色使用预设。设置背景后，面板和控件使用半透明配色。首页显示在线人数和所选线路延迟，线路列表分别显示各线路延迟；无法获取的数值显示为横线。首页常驻文本与底部更新公告历史独立。底部还提供后端配置的快捷链接及游戏文件夹入口，左栏底部显示启动器版本。字体许可证见 `src/HAPPYLAND.Launcher/Assets/Fonts/LICENSE-OFL.txt`。

游戏选项提供内存滑块和数值输入、窗口大小预设及自定义宽高、全屏、视距、帧率、垂直同步、界面大小、亮度和声音设置，保存后在下一次启动游戏时应用。写入 `options.txt` 时保留其他选项。可记住启动器窗口大小，选择启动页面及启动游戏后是否最小化窗口。维护操作在主窗口内确认：卸载删除 Minecraft 版本、资源、依赖和托管 Java，保留存档、Mod、配置与整合包记录；重装运行库只替换游戏所需的托管 Java，不重下游戏文件。

游戏启动前检查整合包版本和校验值；版本不一致时先更新。整合包使用的 Mod 可在界面中启用或关闭；更新时按路径或 Mod 标识保留关闭状态。安装过程发生异常或意外中断后恢复已修改的受管文件。Minecraft 下载优先使用 BMCLAPI，遇到请求失败时使用官方地址。

微软账号使用设备代码登录。皮肤站账号使用后端允许的 Yggdrasil 服务。登录令牌保存在 Windows 凭据管理器、macOS 钥匙串或 Linux Secret Service 中，密码不保存，账号 JSON 仅保存显示信息。启动前验证令牌，必要时自动刷新；令牌无法恢复时才重新登录。Linux 需要安装 libsecret 并启用桌面密钥环服务。

## 许可证

本项目采用 [MIT License](LICENSE)。`src/Hyperion/` 包含基于 HyperionMinecraftLauncher 修改的核心源码，保留 Tech Tea Studio 的 [MIT 许可证](src/Hyperion/LICENSE.txt)。Fusion Pixel 字体来自 [TakWolf/fusion-pixel-font](https://github.com/TakWolf/fusion-pixel-font)，采用 [SIL Open Font License 1.1](src/HAPPYLAND.Launcher/Assets/Fonts/LICENSE-OFL.txt)。NuGet 依赖遵循各自的许可证。

仓库提供启动器和后端源码，Minecraft、Java 运行环境、整合包及 Mod 由程序按配置下载。部署配置、账号缓存、系统凭据、日志、截图和本地验证目录不包含在仓库中。
