﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Web.WebView2.Core;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{
    internal class WpfCoreWebView2AcceleratorKeyPressedEventArgsWrapper : ICoreWebView2AcceleratorKeyPressedEventArgsWrapper
    {
        private readonly CoreWebView2AcceleratorKeyPressedEventArgs _eventArgs;

        public WpfCoreWebView2AcceleratorKeyPressedEventArgsWrapper(CoreWebView2AcceleratorKeyPressedEventArgs eventArgs)
        {
            _eventArgs = eventArgs;
        }
        public uint VirtualKey => _eventArgs.VirtualKey;

        public int KeyEventLParam => _eventArgs.KeyEventLParam;

        public bool Handled
        {
            get => _eventArgs.Handled;
            set => _eventArgs.Handled = value;
        }
    }
}
