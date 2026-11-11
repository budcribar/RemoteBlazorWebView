using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Windows;
using System.Windows.Controls;

namespace PeakSWC.RemoteBlazorWebView.Wpf;

internal sealed class VoiceWindow : Window
{
    private readonly WebView2CompositionControl _view = new();
    private bool _closed;

    internal VoiceWindow(Uri page, string key, CoreWebView2Environment environment, ILogger logger)
    {
        Title = "Remote voice";
        Width = 390;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _view;
        Closed += (_, _) =>
        {
            _closed = true;
            _view.Dispose();
        };
        Loaded += async (_, _) =>
        {
            try
            {
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.ProfileName = "RemoteVoice";
                options.IsInPrivateModeEnabled = true;
                await _view.EnsureCoreWebView2Async(environment, options);
                if (_closed)
                    return;

                var core = _view.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.NavigationStarting += (_, e) => e.Cancel = !IsVoicePage(e.Uri);
                core.FrameNavigationStarting += (_, e) => e.Cancel = true;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.PermissionRequested += (_, e) =>
                {
                    e.SavesInProfile = false;
                    e.State = e.PermissionKind == CoreWebView2PermissionKind.Microphone &&
                        e.IsUserInitiated && IsVoicePage(core.Source) &&
                        Uri.TryCreate(e.Uri, UriKind.Absolute, out var requester) &&
                        requester.GetLeftPart(UriPartial.Authority) == page.GetLeftPart(UriPartial.Authority)
                        ? CoreWebView2PermissionState.Default : CoreWebView2PermissionState.Deny;
                };
                core.NavigationCompleted += (_, e) =>
                {
                    if (e.IsSuccess && IsVoicePage(core.Source))
                        core.PostWebMessageAsString(key);
                    else
                    {
                        logger.LogWarning("Unable to navigate to the voice page: {Status}", e.WebErrorStatus);
                        ShowUnavailable();
                    }
                };
                core.Navigate(page.AbsoluteUri);
            }
            catch (Exception) when (_closed)
            {
                // The owner can close while WebView2 is initializing.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to initialize the local voice window");
                ShowUnavailable();
            }
        };

        bool IsVoicePage(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri == page;
    }

    private void ShowUnavailable()
    {
        if (!_closed)
            Content = new TextBlock { Text = "Remote voice is unavailable.", Margin = new Thickness(12) };
    }
}
