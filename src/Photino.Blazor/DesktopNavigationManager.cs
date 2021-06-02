﻿using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Photino.Blazor
{
    internal class DesktopNavigationManager : NavigationManager
    {
        public static readonly DesktopNavigationManager Instance = new DesktopNavigationManager();

        private static readonly string InteropPrefix = "window.Blazor._internal.navigationManager.";
        private static readonly string InteropNavigateTo = InteropPrefix + "navigateTo";

        protected override void EnsureInitialized()
        {
            Initialize(ComponentsDesktop.BaseUriAbsolute, ComponentsDesktop.InitialUriAbsolute);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            ComponentsDesktop.DesktopJSRuntime.InvokeAsync<object>(InteropNavigateTo, uri, forceLoad);
        }

        public void SetLocation(string uri, bool isInterceptedLink)
        {
            Uri = uri;
            NotifyLocationChanged(isInterceptedLink);
        }
    }
}
