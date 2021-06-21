# Building the Javascript

Copy and unzip the preview source files 
copy the files from src/Components/Web.JS to RemoteBlazorWebView\src\RemoteWebWindow.Blazor.JS\upstream\aspnetcore\web.js



`
cd RemoteBlazorWebView\src\RemotePhotino.Blazor.JS\upstream\aspnetcore\web.js
yarn add --dev inspectpack

change package.json

  "@microsoft/dotnet-js-interop": "link:../../JSInterop/Microsoft.JSInterop.JS/src",
    "@microsoft/signalr": "link:../../SignalR/clients/ts/signalr",
    "@microsoft/signalr-protocol-msgpack": "link:../../SignalR/clients/ts/signalr-protocol-msgpack",

to

"@microsoft/signalr": "6.0.0-preview.3.21201.13",
    "@microsoft/signalr-protocol-msgpack":"6.0.0-preview.3.21201.13",
    "@microsoft/dotnet-js-interop": "6.0.0-preview.3.21201.13",

 yarn install

yarn run build


Open the solution ComponentsNoDeps
Copy  the files from aspnetcore\src\Components\Web.JS to RemoteBlazorWebView\src\RemoteWebWindow.Blazor.JS\upstream\aspnetcore\web.js
 
Update the following packages
"@microsoft/dotnet-js-interop": "6.0.0-preview.4.21253.5",
"@microsoft/signalr": "6.0.0-preview.4.21253.5",
    "@microsoft/signalr-protocol-msgpack": "6.0.0-preview.4.21253.5",
