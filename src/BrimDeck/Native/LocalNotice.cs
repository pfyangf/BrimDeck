using System.Media;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BrimDeck.Native;

// Fires a short Windows toast under BrimDeck.Desktop (App.xaml.cs AUMID) and plays the default system sound.
// Used by the alarm and pomodoro pages; not the QQ/WeChat toast mirror.
internal static class LocalNotice
{
    private const string AppId = "BrimDeck.Desktop";

    public static void Show(string title, string body, bool sound = true)
    {
        try
        {
            string safeTitle = Escape(title);
            string safeBody = Escape(body);
            string xml =
                $"<toast><visual><binding template=\"ToastGeneric\">" +
                $"<text>{safeTitle}</text><text>{safeBody}</text>" +
                $"</binding></visual></toast>";
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(doc));
        }
        catch (Exception)
        {
            // Toast APIs can fail on locked-down hosts; the sound below still alerts.
        }
        if (sound)
        {
            try { SystemSounds.Asterisk.Play(); } catch (Exception) { }
        }
    }

    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
