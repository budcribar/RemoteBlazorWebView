#Steps to setup testing

msedgedriver.exe needs to be installed in the user's path i.e. C:\Program Files (x86)\EdgeDriver


* Publish RemoteBlazorWebViewTutorial.WinFormsApp
* Copy the wwwroot folder from the publish directory to the embedded folder
  Publish RemoteBlazorWebViewTutorial.WinFormsApp using the Embedded publish profile
* Publish RemoteBlazorWebViewTutorial.WpfApp
* Copy the wwwroot folder from the publish directory to the embedded folder
  Publish RemoteBlazorWebViewTutorial.WinFormsApp using the Embedded publish profile
* Rebuild Solution in Debug

* Run Unit Tests

* Note TestRemotePackageBlazorForm.cs and TestRemoteBlazorWebView have been removed from testing
