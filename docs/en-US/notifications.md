# Message notices

The collapsed notch or capsule can briefly show banners that **QQ**, **WeChat** and **mail** send to Windows notification center. The previous contents return after about 6 seconds. When several arrive together, only the latest is shown.

The switches are under **Settings › Quotas and usage › Notifications**:

- **QQ, WeChat and mail**: the master switch. Off, notifications are not read.
- **QQ**, **WeChat** and **Mail**: each app can be left out.
- **Check notification access**: asks Windows for permission again and refreshes the status underneath.

The interface language is **Settings › General › Language**. A first start uses Simplified Chinese when the Windows display language is any Chinese variant, and English otherwise. Both languages stay available.

## Permission you need to grant

BrimDeck reads banners with the public Windows API `Windows.UI.Notifications.Management.UserNotificationListener`. It is the system's notification listener, not a private QQ or WeChat API.

1. Open **Windows Settings › Privacy & security › Notifications**.
2. Under **Notification access**, allow **BrimDeck**.
   The first read may show a system prompt. If it does not, or you previously chose no, turn BrimDeck on yourself on that page.
3. Open **Windows Settings › System › Notifications** and leave notifications on for Windows, and for **QQ**, **WeChat** and **Outlook** (or whichever mail app you use).
4. In QQ, WeChat and the mail app, turn on system notifications or banners. A reminder that stays inside the app's own window is not in Windows notification center, so BrimDeck cannot see it.

An unpackaged desktop app can list current notifications, but subscribing to notification changes fails (`0x80070490`). BrimDeck therefore reads the current list about every 2 seconds, and about every 15 seconds after a failure. It does not click, dismiss or change those notifications.

## Which apps are recognized

Recognition uses only the app id and the display name on the Windows toast:

| Switch | Shown | Not shown |
| --- | --- | --- |
| QQ | `QQ.exe`, `QQNT.exe`, `QQEX.exe`, and banners whose display name is QQ or 腾讯QQ | QQ Music, QQ Browser, QQ Live and similar apps |
| WeChat | `WeChat.exe`, `Weixin.exe`, and banners named 微信, WeChat or Weixin | Messages that only pop up inside WeChat and never become a Windows notification |
| Mail | Outlook, new Outlook, Windows Mail, Thunderbird, Foxmail, and banners named Mail, 邮件 or Outlook | Web mail in a browser, unless that client posts its own Windows notification |

Banners already in notification center when BrimDeck starts are not replayed. Only banners that arrive later are shown. While the panel is hidden for full screen or by the shortcut, the banner is dropped and is not shown when the panel returns.

## Limits

- Chat history, databases and the apps' own window controls are not read. WeChat 4 often uses only its own popup and posts nothing to Windows notification center; the island then shows no WeChat notice. WeChat has to be set to use Windows notifications.
- The island does not open a conversation or send a reply.
- The indicator is too thin for this text. Banners show on the notch and the capsule.
- Banner text is kept in memory only long enough to draw it. It is not written to `settings.json`, `crash.log` or any other file, and it is not uploaded.

## Build

The interface project is WPF (`net10.0-windows`). On **Windows**, install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and run:

```bash
dotnet test tests/BrimDeck.Tests/BrimDeck.Tests.csproj
dotnet build src/BrimDeck/BrimDeck.csproj -c Release
```

Classification and settings live in `BrimDeck.Core` and do not need the Windows UI. A few checks in the full test program call Windows APIs; on another system, run the language and message checks with:

```bash
BRIMDECK_TEST=messages dotnet run --project tests/BrimDeck.Tests/BrimDeck.Tests.csproj
```

The listener itself runs only on Windows.
