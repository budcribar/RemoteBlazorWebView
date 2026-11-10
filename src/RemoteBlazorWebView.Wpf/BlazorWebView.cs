using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;
using Microsoft.AspNetCore.Components.WebView;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{

    public class BlazorWebView : BlazorWebViewBase, IBlazorWebView, IVoiceHost
    {
        public CoreWebView2CookieManager CookieManager  => WebView.CoreWebView2.CookieManager;
        private bool IsRefreshing { get; set; } = false;

        private ILogger<BlazorWebViewBase> Logger => Services.GetService<ILogger<BlazorWebViewBase>>() ?? NullLogger<BlazorWebViewBase>.Instance;

        #region Properties

        public static readonly DependencyProperty UriProperty = DependencyProperty.Register(
            name: nameof(ServerUri),
            propertyType: typeof(Uri),
            ownerType: typeof(BlazorWebView),
            typeMetadata: new PropertyMetadata(OnServerUriPropertyChanged));

        public static readonly DependencyProperty PingIntervalSecondsProperty = DependencyProperty.Register(
        name: nameof(PingIntervalSeconds),
        propertyType: typeof(uint),
        ownerType: typeof(BlazorWebView),
        typeMetadata: new PropertyMetadata(30U, OnPingIntervalSecondsPropertyChanged));


        public static readonly DependencyProperty GrpcBaseUriProperty = DependencyProperty.Register(
          name: nameof(GrpcBaseUri),
          propertyType: typeof(Uri),
          ownerType: typeof(BlazorWebView),
          typeMetadata: new PropertyMetadata(OnGrpcBaseUriPropertyChanged));

        public static readonly DependencyProperty GroupProperty = DependencyProperty.Register(
                   name: nameof(Group),
                   propertyType: typeof(string),
                   ownerType: typeof(BlazorWebView),
                   typeMetadata: new PropertyMetadata("test", OnGroupPropertyChanged));

        public static readonly DependencyProperty MarkupProperty = DependencyProperty.Register(
                  name: nameof(Markup),
                  propertyType: typeof(string),
                  ownerType: typeof(BlazorWebView),
                  typeMetadata: new PropertyMetadata(OnMarkupPropertyChanged));

        public static readonly DependencyProperty EnableMirrorsProperty = DependencyProperty.Register(
                 name: nameof(EnableMirrors),
                 propertyType: typeof(bool),
                 ownerType: typeof(BlazorWebView),
                 typeMetadata: new PropertyMetadata(OnEnableMirrorsPropertyChanged));
        #endregion

        public Uri? ServerUri
        {
            get => (Uri?)GetValue(UriProperty);
            set => SetValue(UriProperty, value);
        }

        public Uri? GrpcBaseUri
        {
            get => (Uri?)GetValue(GrpcBaseUriProperty);
            set => SetValue(GrpcBaseUriProperty, value);
        }

        public uint PingIntervalSeconds
        {
            get => (uint)GetValue(PingIntervalSecondsProperty);
            set => SetValue(PingIntervalSecondsProperty, value);
        }


        public string Group
        {
            get => (string)GetValue(GroupProperty);
            set => SetValue(GroupProperty, value);
        }

        public string Markup
        {
            get => (string)GetValue(MarkupProperty);
            set => SetValue(MarkupProperty, value);
        }

        public override string HostPage
        {
            get => (string)GetValue(HostPageProperty);
            set
            {
                var markup = (string)GetValue(MarkupProperty);
                // Set a default Markup if necessary
                if (ServerUri != null && Id != Guid.Empty && string.IsNullOrEmpty(markup))
                    Markup = RemoteWebView.RemoteWebView.GenMarkup(ServerUri, Id);

                // Default to the http server
                if (GrpcBaseUri == null)
                    GrpcBaseUri = ServerUri;

                SetValue(HostPageProperty, value);
            }
        }

        public bool EnableMirrors
        {
            get => (bool)GetValue(EnableMirrorsProperty);
            set => SetValue(EnableMirrorsProperty, value);
        } 

        private static void OnServerUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnServerUriPropertyChanged(e);

        private void OnServerUriPropertyChanged(DependencyPropertyChangedEventArgs e) {
            if (RequiredStartupPropertiesSet)
                throw new ArgumentException("ServerUri must be set before HostPage");
            Logger.LogInformation("ServerUriPropertyChanged {e}", e.NewValue.ToString());
        }

        private static void OnGrpcBaseUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnGrpcBaseUriPropertyChanged(e);

        private static void OnPingIntervalSecondsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnPingIntervalSecondsPropertyChanged(e);

        private void OnPingIntervalSecondsPropertyChanged(DependencyPropertyChangedEventArgs e) {
            if (RequiredStartupPropertiesSet)
                throw new ArgumentException("PingIntervalSeconds must be set before HostPage");
            Logger.LogInformation("PingIntervalSecondsPropertyChanged {e}", e.NewValue.ToString()); 
        }

        private void OnGrpcBaseUriPropertyChanged(DependencyPropertyChangedEventArgs e) {
            if (RequiredStartupPropertiesSet)
                throw new ArgumentException("GrpcBaseUri must be set before HostPage");
            Logger.LogInformation("GrpcBaseUriPropertyChanged {e}", e.NewValue.ToString()); 
        }

        private static void OnIdPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnIdPropertyChanged(e);

        private void OnIdPropertyChanged(DependencyPropertyChangedEventArgs e) { Logger.LogInformation("IdPropertyChanged {e}", e.NewValue.ToString()); }

        private static void OnGroupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnGroupPropertyChanged(e);

        private void OnGroupPropertyChanged(DependencyPropertyChangedEventArgs e) {
            if (RequiredStartupPropertiesSet)
                throw new ArgumentException("Group must be set before HostPage");
            Logger.LogInformation("GroupPropertyChanged {e}", e.NewValue.ToString());
        }

        private static void OnMarkupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnMarkupPropertyChanged(e);

        private void OnMarkupPropertyChanged(DependencyPropertyChangedEventArgs e) {
            if (RequiredStartupPropertiesSet)
                throw new ArgumentException("Markup must be set before HostPage");
            Logger.LogInformation("MarkupPropertyChanged {e}", e.NewValue.ToString().Replace("\r\n", "").Replace(" ", "")); 
        }

        private static void OnEnableMirrorsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnEnableMirrorsPropertyChanged(e);

        private void OnEnableMirrorsPropertyChanged(DependencyPropertyChangedEventArgs e) {
            if (RequiredStartupPropertiesSet)
                throw new ArgumentException("EnableMirrors must be set before HostPage");
            Logger.LogInformation("EnableMirrorsPropertyChanged {e}", e.NewValue.ToString());
        }

        private Guid id = Guid.Empty;
        public Guid Id
        {
            get
            {
                if (id == Guid.Empty)
                    throw new Exception("Id not initialized");

                return id;
            }
            set
            {
                if (value == Guid.Empty)
                    id = Guid.NewGuid();
                else
                    id = value;
            }
        }

        public override IFileProvider CreateFileProvider(string contentRootDir) => RemoteWebView.RemoteWebView.CreateFileProvider(contentRootDir,HostPage);

        public override WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,string hostPagePathWithinFileProvider, Action<UrlLoadingEventArgs> externalNavigationStarting,Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized,ILogger logger)
        {
            if (ServerUri == null)
                return new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting, blazorWebViewInitializing, blazorWebViewInitialized,logger);
            else
                return new RemoteWebView2Manager(this,webview, services, dispatcher, fileProvider,store, hostPageRelativePath, hostPagePathWithinFileProvider,externalNavigationStarting, blazorWebViewInitializing, blazorWebViewInitialized, logger);
        }

        private string? _voiceHostKey;
        private bool _voiceMicHooked;

        /// <summary>Enables in-session voice for remote sessions (shown in the control bubble). Set before HostPage.</summary>
        public bool EnableVoice
        {
            get => _voiceHostKey is not null;
            set => _voiceHostKey = value
                ? _voiceHostKey ?? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                : null;
        }

        string IVoiceHost.VoiceHostKey =>
            ServerUri?.Scheme == Uri.UriSchemeHttps && GrpcBaseUri?.Scheme == Uri.UriSchemeHttps
                ? _voiceHostKey ?? string.Empty
                : string.Empty;

        // In-surface voice: the control bubble renders the voice controls, so the voice client is injected into this
        // same WebView rather than a separate window. The host key is furnished by the locally-injected script (never
        // served over the network — only the host can supply it); the surface grants the mic for the relay origin on
        // first, user-initiated use.
        private void StartInSurfaceVoice(Uri server, string key)
        {
            if (WebView.CoreWebView2 is not { } core)
                return;

            if (!_voiceMicHooked)
            {
                _voiceMicHooked = true;
                var authority = server.GetLeftPart(UriPartial.Authority);
                core.PermissionRequested += (_, e) =>
                {
                    if (e.PermissionKind == CoreWebView2PermissionKind.Microphone && e.IsUserInitiated &&
                        Uri.TryCreate(e.Uri, UriKind.Absolute, out var requester) &&
                        requester.GetLeftPart(UriPartial.Authority) == authority)
                    {
                        e.SavesInProfile = false;
                        e.State = CoreWebView2PermissionState.Default; // prompt the owner on first use
                    }
                };
            }

            var js =
                "(function(){if(document.getElementById('__remote-voice-client'))return;" +
                "var s=document.createElement('script');s.id='__remote-voice-client';s.defer=true;" +
                "s.src='/voice/client.js';s.dataset.session=" + JsString(Id.ToString()) + ";" +
                "s.dataset.host='true';s.dataset.hostKey=" + JsString(key) + ";" +
                "document.body.appendChild(s);})();";
            _ = core.ExecuteScriptAsync(js);
        }

        private static string JsString(string value) => System.Text.Json.JsonSerializer.Serialize(value);

        public void FireConnected(ConnectedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                if (((IVoiceHost)this).VoiceHostKey is { Length: > 0 } key && ServerUri is Uri server &&
                    WebView.CoreWebView2 is not null)
                {
                    try
                    {
                        StartInSurfaceVoice(server, key);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Unable to start in-surface voice");
                    }
                }
                Connected?.Invoke(this, args);
            });
        }

        public void FireDisconnected(DisconnectedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsRefreshing)
                    Disconnected?.Invoke(this, args);
            });
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            IsRefreshing = true;
            Dispatcher.Invoke(() => Refreshed?.Invoke(this, args));
        }

        public void FireReadyToConnect(ReadyToConnectEventArgs args)
        {
            Dispatcher.Invoke(() => ReadyToConnect?.Invoke(this, args));
        }

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;
        public event EventHandler<ReadyToConnectEventArgs>? ReadyToConnect;

        private void HandleRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            // TODO wtf?
            Services = this.Services;
            RootComponents.ToList().ForEach(x => RootComponents.Add(x));
            HostPage = HostPage;
            Group = Group;
            Markup = Markup;
            ServerUri = ServerUri;

            // TODO
            if (ServerUri != null)
               Id = Id;
        }

        public void Restart() => RemoteWebView.RemoteWebView.Restart(this);

        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri) => RemoteWebView.RemoteWebView.GetGrpcBaseUriAsync(serverUri);

        public void NavigateToString(string htmlContent) => WebViewManager.NavigateToString(htmlContent);

        /// <summary>
        /// Loads a page in this control's own WebView, waiting until the WebView is initialized (unlike CoreWebView2.Navigate,
        /// which is a no-op before then). For the relay's session pages, e.g. an owner surface showing the controlling /app page.
        /// </summary>
        public void NavigateToUrl(Uri url) => WebViewManager.NavigateToUrl(url);


        public Task WaitForInitializationComplete() => Task.CompletedTask;
    }
}
