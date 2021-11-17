# Building the Javascript

Copy and unzip the preview source files 
copy the files from src/Components/Web.JS to RemoteBlazorWebView\src\RemoteWebView.Blazor.JS\upstream\aspnetcore\web.js

`
cd RemoteBlazorWebView\src\RemotePhotino.Blazor.JS\upstream\aspnetcore\web.js
yarn add --dev inspectpack

change package.json

"@microsoft/dotnet-js-interop": "link:../../JSInterop/Microsoft.JSInterop.JS/src",
"@microsoft/signalr": "link:../../SignalR/clients/ts/signalr",
"@microsoft/signalr-protocol-msgpack": "link:../../SignalR/clients/ts/signalr-protocol-msgpack",

to

"@microsoft/signalr": "6.0.0-preview.x",
"@microsoft/signalr-protocol-msgpack":"6.0.0-preview.x",
"@microsoft/dotnet-js-interop": "6.0.0-preview.x",                          // where x is the latest preview

yarn install
yarn run build


Project RemotableWebView




