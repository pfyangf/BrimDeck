# 消息提示

收起的刘海或胶囊可以短暂显示 **QQ**、**微信** 和 **邮件** 发到 Windows 通知中心的横幅。大约 6 秒后恢复原来的内容。一次只显示最新的一条。

开关在 **设置 › 配额与统计 › 提示**：

- **QQ、微信和邮件**：总开关。关闭后不再读取通知。
- **QQ**、**微信**、**邮件**：可以单独关掉其中某一个。
- **检查通知访问**：向 Windows 再次请求权限，并刷新下面的状态。

界面语言在 **设置 › 常规 › 语言**。Windows 显示语言是中文（含繁体）时，第一次启动使用简体中文；其他语言使用 English。两种语言都保留，可以随时切换。

## 需要授予的权限

BrimDeck 使用 Windows 公开接口 `Windows.UI.Notifications.Management.UserNotificationListener` 读取操作中心里的横幅。这是系统提供给应用的通知监听接口，不是 QQ 或微信的私有接口。

1. 打开 **Windows 设置 › 隐私和安全性 › 通知**。
2. 在 **通知访问**（Notification access）中允许 **BrimDeck**。
   第一次读取时，系统可能弹出允许提示。若没有提示，或之前选了拒绝，就在这一页手动打开。
3. 打开 **Windows 设置 › 系统 › 通知**，确认通知总开关是开的，并且 **QQ**、**微信**、**Outlook**（或你使用的邮件应用）自己的通知也是开的。
4. 在 QQ、微信和邮件客户端里打开“系统通知”或“横幅通知”。只显示在软件自己窗口里的提醒，Windows 通知中心看不到，BrimDeck 也看不到。

未打包的桌面程序可以列出当前通知，但订阅“通知已变化”会失败（错误 `0x80070490`）。因此 BrimDeck 大约每 2 秒读取一次当前通知；读不到时改为约每 15 秒重试。程序不会点击、清除或修改这些通知。

## 能识别哪些应用

只认 Windows 通知里的应用标识和显示名称：

| 开关 | 会显示 | 不会显示 |
| --- | --- | --- |
| QQ | `QQ.exe`、`QQNT.exe`、`QQEX.exe`，以及显示名称为 QQ、腾讯QQ 的横幅 | QQ 音乐、QQ 浏览器、QQ 直播等 |
| 微信 | `WeChat.exe`、`Weixin.exe`，以及显示名称为微信、WeChat、Weixin 的横幅 | 只在微信自己的窗口里弹出、没有 Windows 通知的消息 |
| 邮件 | Outlook、新 Outlook、Windows 邮件、Thunderbird、Foxmail，以及显示名称为邮件、Outlook 等的横幅 | 浏览器里的网页邮件，除非那个客户端自己发了 Windows 通知 |

启动之后，通知中心里**已经存在**的横幅不会补显示。只有之后新出现的才会出现在刘海或胶囊上。全屏隐藏或被快捷键隐藏时，这条横幅直接丢掉，面板重新出现时不会补上。

## 做不到的事

- 不读取 QQ、微信的聊天记录、数据库或窗口内部控件。微信 4 经常只用自己的弹窗，不往 Windows 通知中心发消息；这种情况下灵动岛不会出现微信提示。要看到它，需要微信自己打开 Windows 通知。
- 不显示通知正文以外的内容，例如不打开会话、不回复。
- 指示条太细，放不下这段文字；横幅出现在刘海和胶囊上。
- 通知正文只在内存里用于显示，不写入 `settings.json`、`crash.log` 或其他文件，也不上传。

## 编译

界面工程是 WPF（`net10.0-windows`），需要在 **Windows** 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/download) 后编译：

```bash
dotnet test tests/BrimDeck.Tests/BrimDeck.Tests.csproj
dotnet build src/BrimDeck/BrimDeck.csproj -c Release
```

消息分类和设置读写在 `BrimDeck.Core` 里，不依赖 Windows 界面。完整测试程序里有个别检查会调用 Windows 接口；在其他系统上可以只跑语言和消息检查：

```bash
BRIMDECK_TEST=messages dotnet run --project tests/BrimDeck.Tests/BrimDeck.Tests.csproj
```

通知监听本身只能在 Windows 上执行。
