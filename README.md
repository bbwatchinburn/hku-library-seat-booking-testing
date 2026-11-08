# HKU Library Seat Booking

> 当前版本：`0.1.0`，首个公开预览版。

一个面向 Windows 10/11 的香港大学图书馆座位自动预约小工具。用户录入自己的港大预约信息后，程序常驻桌面右下角，显示放号倒计时、当前阶段和目标信息，并在设定时刻自动提交预约；除非用户主动关闭，否则会继续等待下一次放号。

项目沿用 [SeatKiller-GUI](https://github.com/yeliudev/SeatKiller-GUI) 的核心流程思路：本地保存配置、定时等待、到点提交、失败重试和持续监听。网络与表单层已针对香港大学图书馆预约系统重新实现，没有复用武汉大学的 REST 接口。

## 项目解决的问题

港大图书馆座位需要在指定时段预约，热门时段可能很快被订满。手动盯着时间、反复刷新页面并在正确时刻提交，容易错过放号窗口。

本项目只处理一条核心链路：

```text
录入预约配置 -> 读取并校验 Cookie/登录状态 -> 等待放号
-> 自动选择馆区和日期 -> 提交目标时段/座位 -> 展示预约结果
```

它不会代替用户到馆签到，也不会绕过港大登录、验证码、权限或其他访问控制。

## 主要功能

- 首次启动弹出设置窗口，录入用户名、密码、Cookie、馆区、座位、预约时段和重试参数。
- 配置保存到 `%LOCALAPPDATA%\HKU-Library-Seat-Booking\settings.json`；密码和 Cookie 使用当前 Windows 用户的 DPAPI 加密，文件中不保存明文。
- 按香港时间计算下一次放号，默认每天 `00:00`，可在设置中修改；目标日期默认是放号后第 1 天，可配置为第 0–7 天。
- 在放号前约 20 秒加载预约页面，读取服务器 `Date` 响应头校准本机时间，然后在放号时刻提交。
- 自动识别港大 ASP.NET WebForms 的 `__VIEWSTATE`、`__EVENTVALIDATION`、馆区、设施类型、日期、时段、座位和提交控件。
- 支持 Cookie 直连，也支持页面存在普通 HTML 登录表单时的账号密码登录。港大若跳转统一身份认证，应使用 Cookie 模式。
- 支持失败自动重试，重试次数可配置为 `0–50`，单次间隔可配置为 `100–60000` 毫秒。
- 悬浮窗默认位于当前屏幕工作区右下角，始终置顶、无边框、可拖动，并可保存窗口位置。
- 可配置窗口透明度 `35%–100%`。
- 最小化后隐藏到系统托盘；可从托盘恢复窗口、打开设置或退出。
- 显示等待、准备页面、提交、重试、成功、失败和需要重新登录等状态。
- 仅运行当前用户的一个港大账号，不包含云同步、远程通知、批量抢座或复杂仪表盘。

## 港大预约规则

香港大学图书馆当前公开入口是 [Book a Space](https://lib.hku.hk/general/e-form/book-a-space.html)，实际预约系统为 `https://booking.lib.hku.hk/`，登录后的 Study Table 页面是 `Secure/FacilityStatusDate.aspx`。程序会根据 `Location`、`Facility Type`、`Date` 三个 ASP.NET WebForms 下拉框自动选择目标，并继续处理日期、时段和座位页面。公开的 [Booking Policy](https://lib.hku.hk/general/e-form/L3_booking_policy.html) 说明 Study Tables 通常可提前一个时段预约，并给出了周一至周五、周末/公众假期的时段：

| 时段 | 周一至周五 | 周六、周日及公众假期 |
| --- | --- | --- |
| 第 1 时段 | 08:30–13:00 | 09:00–13:00 |
| 第 2 时段 | 13:00–18:00 | 13:00–17:00 |
| 第 3 时段 | 18:00–22:00 | 无 |

公开政策没有给出一个对所有馆区、所有学期都固定不变的“精确放号钟点”。因此程序默认按每天 `00:00` 开始监听，并把放号时间放在设置中供你按当前页面实际规则修改。目标日期默认是“放号后第 1 天”，也可改为第 0–7 天。

一个时段占满日期范围或开放规则变化时，应以港大预约系统当天页面为准。程序不会绕过验证码、统一身份认证或其他访问控制。

## 安装方法

项目不需要安装 .NET SDK、Node.js、Python、PyQt、Electron 或第三方 NuGet 包。Windows 10/11 通常已自带：

- Windows PowerShell 5.x
- .NET Framework 4.x
- 官方 C# 编译器 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- Windows Forms、System.Drawing、System.Security 和 System.Runtime.Serialization

安装步骤：

1. 将整个 `hku-library-seat-booking` 文件夹放到希望保存的位置。
2. 项目已包含编译好的 `bin\HkuLibrarySeatBooking.exe`，可以直接双击运行。
3. 如果修改了源码，可运行 `run.ps1`，脚本会自动检查并重新编译。

程序使用普通用户权限运行，不需要管理员权限，也不会安装后台服务或驱动程序。

从 PowerShell 启动：

```powershell
cd ".\hku-library-seat-booking"
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

构建但不启动：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1 -BuildOnly
```

运行自测：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1 -Test
```

编译输出为 `bin\HkuLibrarySeatBooking.exe`。运行脚本不会初始化 Git 仓库，也不会联网上传源码或配置。

## 使用方法

1. 首次启动后填写：

   - `HKU 用户名`：Cookie 模式可留空。
   - `密码`：Cookie 模式可留空。
   - `浏览器 Cookie`：推荐填写，支持港大统一身份认证后的会话。
   - `目标馆区`：例如 `Main Library 4/F Study Tables`。
   - `目标座位`：填写页面显示的座位号；留空时自动匹配第一个非禁用座位。
   - `预约时段`：第 1、第 2、第 3 时段或自定义时段。
   - `每天放号时间`：默认 `00:00`，按港大当前实际规则调整。
   - `目标日期`：相对放号日的偏移，`1` 表示预约放号后的第 1 天。
   - `失败自动重试次数` 和 `重试间隔`。
   - `窗口透明度`。

2. 保存设置后，程序开始显示倒计时。目标馆区、日期、时段和座位会显示在倒计时下方。

3. 在放号前约 20 秒，状态会变为“正在准备预约页面”。Cookie 无效、页面结构变化或目标日期未开放时，窗口会显示具体原因。

4. 到点后程序提交预约并展示结果：

   - 成功：显示“预约成功”。
   - 失败：显示港大页面返回的信息或网络错误。
   - Cookie 失效：显示“需要处理”，并提示重新登录。

5. 点击“设置”可以修改所有配置；点击“−”隐藏到托盘；点击“×”退出。最小化不会停止后台监听。

6. 预约是否最终成立，仍应以港大 `My Booking Record` 页面为准。预约成功后需要按港大规定到馆签到。

## 配置 Cookie

推荐方式：

1. 在 Microsoft Edge 中打开 `https://booking.lib.hku.hk/` 并手动登录。
2. 进入要预约的馆区和座位页面，确认页面已能正常显示你的预约权限。
3. 按 `F12` 打开开发者工具，在“网络”面板刷新页面。
4. 选择任意一个发往 `booking.lib.hku.hk` 的页面请求，复制请求头中的 `Cookie` 值。
5. 粘贴到程序设置里的“浏览器 Cookie”，保存。

不要把 Cookie、密码或配置文件发送给他人。Cookie 失效后，程序会显示“需要处理”，此时按同样步骤更新即可。

如果港大页面今后支持普通账号密码表单登录，可取消“仅使用 Cookie”，并填写用户名和密码。若系统使用 HKU 统一身份认证，程序会明确提示改用 Cookie 模式，不会尝试绕过登录。

## 高级适配

“高级适配”页用于预约网站在学期更新后改变字段名的情况。默认全部留空，程序会按座位号、时段文字和预约按钮自动匹配。若自动识别失效，可从浏览器开发者工具中查看实际 `name/value`，分别填写座位、时段和提交按钮字段。

程序不会上传任何页面、Cookie 或个人信息。当前版本没有日志上传、崩溃上报、邮件或远程通知、账号云同步。

## 输入输出示例

### 输入示例

| 设置项 | 示例 |
| --- | --- |
| 目标馆区 | `Main Library 4/F Study Tables` |
| 目标座位 | `A-17`；也可留空自动匹配 |
| 预约时段 | `第 2 时段 13:00–18:00` |
| 放号时间 | `00:00` |
| 目标日期偏移 | `1` |
| 失败重试 | `5` 次，每 `800` 毫秒 |
| 透明度 | `90%` |
| Cookie | 浏览器登录港大预约系统后的请求头 Cookie |

### 等待输出示例

```text
等待放号
23:37:13
放号 11-09 00:00:00 · 到点自动提交
目标  Main Library 4/F Study Tables · 11-10 Tue · 13:00–18:00 · A-17
放号 11-09 00:00:00 · 重试 5 次
```

### 成功输出示例

```text
预约成功
港大预约系统返回预约成功。 · 11-10 13:00–18:00
目标  Main Library 4/F Study Tables · 11-10 Tue · 13:00–18:00 · A-17
```

### 失败输出示例

```text
预约失败
目标日期或时段暂未开放，未找到可选座位。
```

实际输出的日期、时段和失败原因取决于港大系统当时返回的页面内容。

## 项目结构

```text
hku-library-seat-booking/
├─ .gitignore
├─ README.md
├─ run.ps1
├─ src/
│  ├─ Program.cs                 # 悬浮窗、托盘和程序入口
│  ├─ SettingsForm.cs            # 首次设置与修改设置
│  ├─ BookingModels.cs           # 配置、时段、预约结果
│  ├─ ConfigStore.cs             # JSON + DPAPI 本地存储
│  ├─ ReleaseSchedule.cs         # 放号排程与倒计时
│  ├─ BookingScheduler.cs        # 常驻监听、校准和重试
│  ├─ HkuBookingClient.cs        # 港大 HTTP/表单适配
│  ├─ WebFormParser.cs           # 无第三方依赖的 HTML 表单解析
│  ├─ AssemblyInfo.cs            # 程序集产品与版权信息
│  └─ app.manifest
├─ tests/
│  ├─ CoreTests.cs
│  └─ TestProgram.cs
└─ bin/
   └─ HkuLibrarySeatBooking.exe
```

### 必要依赖

- 操作系统：Windows 10 或 Windows 11。
- 运行环境：系统自带的 .NET Framework 4.x。
- 构建环境：系统自带的 `csc.exe` 和 Windows PowerShell。
- 系统程序集：Windows Forms、System.Drawing、System.Security、System.Runtime.Serialization。
- 网络：可访问 `booking.lib.hku.hk`，并需要有效的港大登录 Cookie。
- 第三方包：无。项目不依赖 Node.js、Python、Electron、PyQt 或 NuGet 包。
- 外部服务：不依赖自建服务器、云数据库、邮件服务或通知服务。

## 测试范围

自测覆盖工作日/周末时段映射、目标日期、放号宽限窗口、倒计时格式、表单自动识别、下拉框与 WebForms 回发、显式字段覆盖、登录表单、配置边界、敏感字段非明文保存、目标信息摘要，以及一次由本地模拟服务器完成的多步预约流程。

真实预约提交需要有效 HKU 登录 Cookie 和实际可预约时段，不能在未授权或没有用户账号的情况下安全验证。首次实际使用时建议先用一个不重要的时段确认页面字段和目标日期，再长期常驻。

运行自测不会提交真实预约，也不会把配置发送到外部服务器。

## 使用边界

- 仅操作当前用户自己的单个港大账号。
- 不进行多账号并发、批量抢座、云端同步或远程通知。
- 预约成功后仍需按港大规定到馆签到；未签到座位可能被系统释放。
- 使用前请自行确认港大图书馆预约政策及账号使用规定。

## 许可证

本项目采用 [MIT License](LICENSE)。
