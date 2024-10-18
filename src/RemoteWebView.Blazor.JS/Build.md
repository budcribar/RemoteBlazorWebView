# Building remote.blazor.desktop.js


* Install Node Version Manager from https://github.com/coreybutler/nvm-windows

* Verify that you have version 22.3.0 of node installed
```
nvm list 
```

* If not, run the following command to install it
```
nvm install 22.3.0
```

* Using a powershell or cmd window with Admin privileges, set the node version
```
nvm use 22.3.0
```

* Install yarn globally and use it to install the dependencies
```
  
  npm install -g yarn
  npm install -g rollup
  cd RemoteWebView.Blazor.JS
  yarn install
  cd .\web.js\ 
  yarn install
```

* npm install -g protoc-gen-ts
* npm install -g protoc-gen-grpc-web

* Add RemoteWebView.Blazor.JS\protoc\bin to your path variable
* Verify you can run "protoc -h" from a terminal window 
* Now you should be able to build remote.blazor.desktop.js using the following command

```
cd RemoteWebView.Blazor.JS
npm run build:production
```


sudo apt update



open ubuntu
cd /mnt/c/users/budcr/source/repos/RemoteBlazorWebView/src
sudo find . \( -name '*.cs' -o -name '*.txt' -o -name '*.ts' -o -name '*.js' -o -name '.yarnrc' -o -name '*.npmproj' \) -exec unix2dos {} \;

sudo find . -path './node_modules' -prune -o \( -name '*.cs' -o -name '*.txt' -o -name '*.ts' -o -name '*.js' -o -name '.yarnrc' -o -name '*.npmproj' \) -exec unix2dos {} \;




