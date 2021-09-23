// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace PeakSWC.RemoteWebView
{
    public class ManifestRootDirectory : ManifestDirectory
    {
        public ManifestRootDirectory(ManifestEntry[] children)
            : base(name: string.Empty, children: children)
        {
            SetParent(ManifestSinkDirectory.Instance);
        }

        public override ManifestDirectory ToRootDirectory() => this;
    }
}
