using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    internal bool IsToolsPage => CurrentPage == DeckPage.Tools;
    private string _toolsForm = "";

    private void RenderTools()
    {
        if (ToolsContent is null) return;
        RefreshStatus();
        PrepareUtilityPage();
        ShowOnlyPage(ToolsContent);
        string form = $"{ContentCanvas.Width:0}|{PanelTheme.Generation}|{Loc.Language}";
        if (form == _toolsForm && ToolsContent.Children.Count > 0) return;
        _toolsForm = form;
        ToolsContent.Children.Clear();
        var wrap = new WrapPanel { Margin = new Thickness(4) };
        void Add(string title, Action open) => wrap.Children.Add(ToolTile(title, open));
        Add(Loc.T("JSON 格式化", "JSON format"), () => OpenJsonTool(indent: true));
        Add(Loc.T("JSON 压缩", "JSON minify"), () => OpenJsonTool(indent: false));
        Add(Loc.T("二维码", "QR code"), OpenQrTool);
        Add(Loc.T("Base64", "Base64"), () => OpenCodecTool("Base64", TextTools.EncodeBase64, (t, out r, out e) => TextTools.TryDecodeBase64(t, out r, out e)));
        Add(Loc.T("URL 编解码", "URL encode"), () => OpenCodecTool("URL", TextTools.EncodeUrl, (t, out r, out e) => { r = TextTools.DecodeUrl(t); e = ""; return true; }));
        Add(Loc.T("Hex 编解码", "Hex encode"), () => OpenCodecTool("Hex", TextTools.EncodeHex, (t, out r, out e) => TextTools.TryDecodeHex(t, out r, out e)));
        ToolsContent.Children.Add(DeckScroll(wrap));
    }

    private Border ToolTile(string title, Action open)
    {
        var label = UI.Centered(title, 13, UI.Primary, FontWeights.SemiBold);
        label.Margin = new Thickness(16, 18, 16, 18);
        var border = new Border
        {
            Width = 140, Margin = new Thickness(0, 0, 8, 8),
            Background = UI.Brush(PanelTheme.Hex("fill")),
            CornerRadius = new CornerRadius(12),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = label
        };
        border.MouseLeftButtonUp += (_, e) => { open(); e.Handled = true; };
        return border;
    }

    private void OpenJsonTool(bool indent)
    {
        var dialog = ToolDialog(indent ? Loc.T("JSON 格式化", "JSON format") : Loc.T("JSON 压缩", "JSON minify"));
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 120, Margin = new Thickness(0, 0, 0, 8) };
        var output = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 120, IsReadOnly = true };
        var status = new TextBlock { Foreground = UI.Brush(UI.Tertiary), Margin = new Thickness(0, 6, 0, 0) };
        var run = new Button { Content = Loc.T("转换", "Convert"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 8) };
        run.Click += (_, _) =>
        {
            if (TextTools.TryFormatJson(input.Text, indent, out var result, out var error))
            { output.Text = result; status.Text = Loc.T("完成", "Done"); }
            else { output.Text = input.Text; status.Text = error; }
        };
        var root = (StackPanel)dialog.Content!;
        root.Children.Add(input); root.Children.Add(run); root.Children.Add(output); root.Children.Add(status);
        dialog.ShowDialog();
    }

    private void OpenQrTool()
    {
        var dialog = ToolDialog(Loc.T("文本转二维码", "Text to QR"));
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, Margin = new Thickness(0, 0, 0, 8) };
        var image = new System.Windows.Controls.Image { Width = 200, Height = 200, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 0) };
        var run = new Button { Content = Loc.T("生成", "Generate"), Padding = new Thickness(12, 6, 12, 6) };
        run.Click += (_, _) =>
        {
            if (TextTools.QrBgra(input.Text) is not { } qr) { image.Source = null; return; }
            var bmp = new WriteableBitmap(qr.Size, qr.Size, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, qr.Size, qr.Size), qr.Pixels, qr.Size * 4, 0);
            bmp.Freeze();
            image.Source = bmp;
        };
        var root = (StackPanel)dialog.Content!;
        root.Children.Add(input); root.Children.Add(run); root.Children.Add(image);
        dialog.ShowDialog();
    }

    private delegate bool DecodeFn(string text, out string result, out string error);

    private void OpenCodecTool(string title, Func<string, string> encode, DecodeFn decode)
    {
        var dialog = ToolDialog(title);
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100, Margin = new Thickness(0, 0, 0, 8) };
        var output = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100, IsReadOnly = true };
        var status = new TextBlock { Foreground = UI.Brush(UI.Tertiary), Margin = new Thickness(0, 6, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var enc = new Button { Content = Loc.T("编码", "Encode"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var dec = new Button { Content = Loc.T("解码", "Decode"), Padding = new Thickness(12, 6, 12, 6) };
        enc.Click += (_, _) => { output.Text = encode(input.Text); status.Text = Loc.T("完成", "Done"); };
        dec.Click += (_, _) =>
        {
            if (decode(input.Text, out var result, out var error)) { output.Text = result; status.Text = Loc.T("完成", "Done"); }
            else { output.Text = input.Text; status.Text = error; }
        };
        row.Children.Add(enc); row.Children.Add(dec);
        var root = (StackPanel)dialog.Content!;
        root.Children.Add(input); root.Children.Add(row); root.Children.Add(output); root.Children.Add(status);
        dialog.ShowDialog();
    }

    private Window ToolDialog(string title)
    {
        var dialog = new Window
        {
            Title = title, Width = 480, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, Background = UI.Brush(PanelTheme.Hex("surface"))
        };
        dialog.Content = new StackPanel { Margin = new Thickness(16) };
        return dialog;
    }

    private void ToolsPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Tools);
}
