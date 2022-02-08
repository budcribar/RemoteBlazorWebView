# Building the Javascript

Install npm
Install yarn
  npm install -g yarn

Install protoc
  npm install protoc -g

nvm use 16.13.0 !! latest node fails !!

dotnet dev-certs https --trust

yarn install
yarn run build

Set your path to include protoc
source\repos\budcribar\RemoteBlazorWebView\src\RemoteWebView.Blazor.JS\protoc\bin


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




