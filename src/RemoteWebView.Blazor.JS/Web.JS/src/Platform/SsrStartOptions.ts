// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

export interface SsrStartOptions {
  /**
   * If true, does not attempt to preserve DOM nodes when performing dynamic updates to SSR content
   * (for example, during enhanced navigation or streaming rendering).
   */
  disableDomPreservation?: boolean;
}
