using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using System.Windows.Interop;

namespace Twich
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private LibVLC? _libVlc;
        private VlcMediaPlayer? _mediaPlayer;
        private FloatingPlayerWindow? _pipWindow;

        public MainWindow()
        {
            InitializeComponent();
            Core.Initialize();

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            Topmost = false;
            Loaded += MainWindow_Loaded;
            KeyDown += MainWindow_KeyDown;
            PreviewKeyDown += MainWindow_PreviewKeyDown; // ensure Escape is caught even when WebView2 has focus
            Deactivated += MainWindow_Deactivated; // when user switches apps, try to enable PiP
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _libVlc = new LibVLC();
            _mediaPlayer = new VlcMediaPlayer(_libVlc);
            VideoView.MediaPlayer = _mediaPlayer;

            var userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwitchWebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            var profile = webView.CoreWebView2.Profile;
            profile.IsPasswordAutosaveEnabled = true;
            profile.IsGeneralAutofillEnabled = true;

            webView.CoreWebView2.AddWebResourceRequestedFilter("*://*.doubleclick.net/*", CoreWebView2WebResourceContext.All);
            webView.CoreWebView2.AddWebResourceRequestedFilter("*://*.googlesyndication.com/*", CoreWebView2WebResourceContext.All);
            webView.CoreWebView2.AddWebResourceRequestedFilter("*://*.adservice.google.com/*", CoreWebView2WebResourceContext.All);
            webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            webView.CoreWebView2.Navigate("https://www.twitch.tv/");
        }

        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            string adBlockScript = @"
                (function() {
                    var adSelectors = [
                        '[id^=ad]', '[class*=ad]', '[class*=banner]', '[class*=sponsor]', '[class*=promo]', '[class*=ads]', '[class*=advert]', '[class*=doubleclick]', '[class*=googlesyndication]'
                    ];
                    adSelectors.forEach(function(selector) {
                        var elements = document.querySelectorAll(selector);
                        elements.forEach(function(el) { el.remove(); });
                    });
                })();
            ";
            await webView.ExecuteScriptAsync(adBlockScript);
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HandleEscape();
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HandleEscape();
                e.Handled = true;
            }
        }

        private void HandleEscape()
        {
            var core = webView.CoreWebView2;
            if (core != null)
            {
                if (core.CanGoBack)
                {
                    core.GoBack();
                }
                else
                {
                    core.Navigate("https://www.twitch.tv/");
                }
            }
        }

        private async void PiPButton_Click(object sender, RoutedEventArgs e)
        {
            await TriggerPictureInPictureAsync();
        }

        private async void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            // When the window loses focus (user switches apps), try to enable PiP so the video floats over other apps.
            await TriggerPictureInPictureAsync();
        }

        private async System.Threading.Tasks.Task TriggerPictureInPictureAsync()
        {
            if (webView?.CoreWebView2 == null) return;

            string pipScript = @"
                (async function() {
                    try {
                        // Try to find Twitch overlay container then locate the underlying video
                        const overlay = document.querySelector('[data-a-target=""player-overlay-click-handler""]');
                        let target = null;
                        if (overlay) {
                            // Look within closest player container for a video element
                            const container = overlay.closest('.video-player__container, .persistent-player, .channel-root');
                            if (container) {
                                target = container.querySelector('video');
                            }
                        }
                        // Fallbacks
                        if (!target) {
                            const vids = Array.from(document.querySelectorAll('video'));
                            target = vids.find(v => !v.paused && !v.ended && v.readyState >= 2) || vids[0] || null;
                        }
                        if (!target) { return 'no-video'; }

                        // If already in PiP with another element, switch to this one
                        if (document.pictureInPictureElement && document.pictureInPictureElement !== target) {
                            try { await document.exitPictureInPicture(); } catch {}
                        }

                        // Some sites require disabling fullscreen to allow PiP
                        try { if (document.fullscreenElement) await document.exitFullscreen(); } catch {}

                        // If already in PiP with the same element, do nothing
                        if (document.pictureInPictureElement === target) { return 'ok'; }

                        await target.requestPictureInPicture();
                        return 'ok';
                    } catch (e) {
                        return 'err:' + (e && e.message ? e.message : e);
                    }
                })();
            ";

            await webView.ExecuteScriptAsync(pipScript);
        }

        private void TogglePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle second column to show/hide VLC player panel
            var show = TogglePlayerButton.Content?.ToString() != "Hide Player";
            TogglePlayerButton.Content = show ? "Hide Player" : "Show Player";
            var grid = (Grid)Content;
            var col = grid.ColumnDefinitions[1];
            col.Width = show ? new GridLength(420) : new GridLength(0);

            if (show && _libVlc != null && _mediaPlayer != null)
            {
                EnsurePipWindow();
            }
        }

        private void EnsurePipWindow()
        {
            if (_pipWindow != null) return;
            _pipWindow = new FloatingPlayerWindow(_libVlc!, _mediaPlayer!);
            _pipWindow.Topmost = true;
            PositionPipTopRight(_pipWindow);
            _pipWindow.Show();
        }

        private void PositionPipTopRight(Window win)
        {
            const double width = 360; // 16:9 small preview
            const double height = 203;
            win.Width = width;
            win.Height = height;
            win.Left = SystemParameters.WorkArea.Right - width - 12;
            win.Top = SystemParameters.WorkArea.Top + 12;
        }

        private void PlayUrlButton_Click(object sender, RoutedEventArgs e)
        {
            var url = StreamUrlTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url) || _mediaPlayer is null || _libVlc is null) return;
            using var media = new Media(_libVlc, new Uri(url));
            _mediaPlayer.Play(media);
        }
    }
}