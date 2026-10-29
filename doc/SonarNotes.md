# Sonar notes

## SYSLIB1044 — NOT FIXED / WON'T FIX

`src/RemoteWebViewService/RemoteStaticFiles/RemoteFileResolver.cs`, `HrefRegEx`.

The base-href pattern is already a `GeneratedRegex`. SYSLIB1044 is a limitation of Microsoft's regex generator on that pattern. Rewriting the expression so the generator accepts it could change which tag it matches, so no code change was made.
