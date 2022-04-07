- Increment VersionPrefix from Directory.Build.Props
- Modify Readme.md in the RemoteBlazorWebView and RemoteBlazorWebViewTutorial repositories
- Modify PackageReleaseNotes in Project Files (Wpf,Forms,RemoteWebView, RemoteWebViewService)
- .\RunTests.ps1  -Mode Developer from RemoteBlazorWebView
	** Note if unable to install RemoteWebViewServer you may need to do dotnet nuget remove source 
- Upload the contents of RemoteBlazorWebView\artifacts to nuget.org
- Open the RemoteBlazorWebViewTutorial solution and make sure Package sources are set to all
- Update the RemoteBlazorWebView.WindowsForms, RemoteBlazorWebView.Wpf, RemoteBlazorWebViewTutorial.Shared packages to the latest version
- .\RunTests.ps1  -Mode Release
- Verify RunTests.ps1 passes in release mode
- Update both RemoteBlazorWebView and RemoteBlazorWebViewTutorial repositories with the artifacts/releaze.zip using the latest tag
- Merge the changes into the dotnet6 branch
- Create a new branch in both repositories RemoteBlazorWebView and RemoteBlazorWebViewTutorial
- Copy the webwindow.proto file to the RemoteWebViewAdmin project
- Copy the RemoteWebViewService to the WebWindow server

