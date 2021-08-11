import '@microsoft/dotnet-js-interop';
import { resetScrollAfterNextBatch } from '../Rendering/Renderer';
import { EventDelegator } from '../Rendering/Events/EventDelegator';

let hasEnabledNavigationInterception = false;
let hasRegisteredNavigationEventListeners = false;

// Will be initialized once someone registers
let notifyLocationChangedCallback: ((uri: string, intercepted: boolean) => Promise<void>) | null = null;

// These are the functions we're making available for invocation from .NET
export const internalFunctions = {
  listenForNavigationEvents,
  enableNavigationInterception,
  navigateTo,
  getBaseURI: () => document.baseURI,
  getLocationHref: () => location.href,
};

function listenForNavigationEvents(callback: (uri: string, intercepted: boolean) => Promise<void>) {
  notifyLocationChangedCallback = callback;

  if (hasRegisteredNavigationEventListeners) {
    return;
  }

  hasRegisteredNavigationEventListeners = true;
  window.addEventListener('popstate', () => notifyLocationChanged(false));
}

function enableNavigationInterception() {
  hasEnabledNavigationInterception = true;
}

export function attachToEventDelegator(eventDelegator: EventDelegator) {
  // We need to respond to clicks on <a> elements *after* the EventDelegator has finished
  // running its simulated bubbling process so that we can respect any preventDefault requests.
  // So instead of registering our own native event, register using the EventDelegator.
  eventDelegator.notifyAfterClick(event => {
    if (!hasEnabledNavigationInterception) {
      return;
    }

    if (event.button !== 0 || eventHasSpecialKey(event)) {
      // Don't stop ctrl/meta-click (etc) from opening links in new tabs/windows
      return;
    }

    if (event.defaultPrevented) {
      return;
    }

    // Intercept clicks on all <a> elements where the href is within the <base href> URI space
    // We must explicitly check if it has an 'href' attribute, because if it doesn't, the result might be null or an empty string depending on the browser
    const anchorTarget = findAnchorTarget(event);

    if (anchorTarget && canProcessAnchor(anchorTarget)) {
      const href = anchorTarget.getAttribute('href')!;
      const absoluteHref = toAbsoluteUri(href);

      if (isWithinBaseUriSpace(absoluteHref)) {
        event.preventDefault();
        performInternalNavigation(absoluteHref, /* interceptedLink */ true, /* replace */ false);
      }
    }
  });
}

// For back-compat, we need to accept multiple overloads
export function navigateTo(uri: string, options: NavigationOptions): void;
export function navigateTo(uri: string, forceLoad: boolean): void;
export function navigateTo(uri: string, forceLoad: boolean, replace: boolean): void;
export function navigateTo(uri: string, forceLoadOrOptions: NavigationOptions | boolean, replaceIfUsingOldOverload: boolean = false) {
  const absoluteUri = toAbsoluteUri(uri);

  // Normalize the parameters to the newer overload (i.e., using NavigationOptions)
  const options: NavigationOptions = forceLoadOrOptions instanceof Object
    ? forceLoadOrOptions
    : { forceLoad: forceLoadOrOptions, replaceHistoryEntry: replaceIfUsingOldOverload };

  if (!options.forceLoad && isWithinBaseUriSpace(absoluteUri)) {
    performInternalNavigation(absoluteUri, false, options.replaceHistoryEntry);
  } else {
    // For external navigation, we work in terms of the originally-supplied uri string,
    // not the computed absoluteUri. This is in case there are some special URI formats
    // we're unable to translate into absolute URIs.
    performExternalNavigation(uri, options.replaceHistoryEntry);
  }
}

function performExternalNavigation(uri: string, replace: boolean) {
  if (location.href === uri) {
    // If you're already on this URL, you can't append another copy of it to the history stack,
    // so we can ignore the 'replace' flag. However, reloading the same URL you're already on
    // requires special handling to avoid triggering browser-specific behavior issues.
    // For details about what this fixes and why, see https://github.com/dotnet/aspnetcore/pull/10839
    const temporaryUri = uri + '?';
    history.replaceState(null, '', temporaryUri);
    location.replace(uri);
  } else if (replace) {
    location.replace(uri);
  } else {
    location.href = uri;
  }
}

function performInternalNavigation(absoluteInternalHref: string, interceptedLink: boolean, replace: boolean) {
  // Since this was *not* triggered by a back/forward gesture (that goes through a different
  // code path starting with a popstate event), we don't want to preserve the current scroll
  // position, so reset it.
  // To avoid ugly flickering effects, we don't want to change the scroll position until
  // we render the new page. As a best approximation, wait until the next batch.
  resetScrollAfterNextBatch();

  if (!replace) {
    history.pushState(null, /* ignored title */ '', absoluteInternalHref);
  } else {
    history.replaceState(null, /* ignored title */ '', absoluteInternalHref);
  }

  notifyLocationChanged(interceptedLink);
}

async function notifyLocationChanged(interceptedLink: boolean) {
  if (notifyLocationChangedCallback) {
    await notifyLocationChangedCallback(location.href, interceptedLink);
  }
}

let testAnchor: HTMLAnchorElement;
export function toAbsoluteUri(relativeUri: string) {
  testAnchor = testAnchor || document.createElement('a');
  testAnchor.href = relativeUri;
  return testAnchor.href;
}

function findAnchorTarget(event: MouseEvent): HTMLAnchorElement | null {
  // _blazorDisableComposedPath is a temporary escape hatch in case any problems are discovered
  // in this logic. It can be removed in a later release, and should not be considered supported API.
  const path = !window['_blazorDisableComposedPath'] && event.composedPath && event.composedPath();
  if (path) {
    // This logic works with events that target elements within a shadow root,
    // as long as the shadow mode is 'open'. For closed shadows, we can't possibly
    // know what internal element was clicked.
    for (let i = 0; i < path.length; i++) {
      const candidate = path[i];
      if (candidate instanceof Element && candidate.tagName === 'A') {
        return candidate as HTMLAnchorElement;
      }
    }
    return null;
  } else {
    // Since we're adding use of composedPath in a patch, retain compatibility with any
    // legacy browsers that don't support it by falling back on the older logic, even
    // though it won't work properly with ShadowDOM. This can be removed in the next
    // major release.
    return findClosestAnchorAncestorLegacy(event.target as Element | null, 'A');
  }
}

function findClosestAnchorAncestorLegacy(element: Element | null, tagName: string) {
  return !element
    ? null
    : element.tagName === tagName
      ? element
      : findClosestAnchorAncestorLegacy(element.parentElement, tagName);
}

function isWithinBaseUriSpace(href: string) {
  const baseUriWithTrailingSlash = toBaseUriWithTrailingSlash(document.baseURI!); // TODO: Might baseURI really be null?
  return href.startsWith(baseUriWithTrailingSlash);
}

function toBaseUriWithTrailingSlash(baseUri: string) {
  return baseUri.substr(0, baseUri.lastIndexOf('/') + 1);
}

function eventHasSpecialKey(event: MouseEvent) {
  return event.ctrlKey || event.shiftKey || event.altKey || event.metaKey;
}

function canProcessAnchor(anchorTarget: HTMLAnchorElement) {
  const targetAttributeValue = anchorTarget.getAttribute('target');
  const opensInSameFrame = !targetAttributeValue || targetAttributeValue === '_self';
  return opensInSameFrame && anchorTarget.hasAttribute('href') && !anchorTarget.hasAttribute('download');
}

// Keep in sync with Components/src/NavigationOptions.cs
export interface NavigationOptions {
  forceLoad: boolean;
  replaceHistoryEntry: boolean;
}
