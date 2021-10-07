using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace PeakSWC.RemoteWebView
{
    public static class RestartFailedPage
    {
        public static string Html(string processName, int pid, string hostName)
        {
           

            string html = $@"
<!DOCTYPE html>
<html lang = 'en' style='height: 100%;' >
<head>
  <title>Restart Failed</title>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;width:100%; height:100%;'>

<div style='vertical-align:middle;margin:0;height:100%;color:#a94442;background-color:#f2dede;text-align:center;font-size: large; font-family: Verdana, Geneva, Tahoma, sans-serif;'>
    <br/>
    <br/>
<span >  Failed to restart {processName}.</span>
<br/>
<br/>

<span >  Running with Pid {pid} on system {hostName}.</span>

</div>
</body>
</html>
";
            return html;
        }
        public static string Html(string guid, bool isRestarting)
        {
            string title = isRestarting ? "Restart" : "Start";
            string failure = isRestarting ? "restart" : "start";

            string html = $@"
<!DOCTYPE html>
<html lang = 'en' style='height: 100%;' >
<head>
  <title>{title} Failed</title>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
</head>
<body style='margin:0;width:100%; height:100%;'>

<div style='vertical-align:middle;margin:0;height:100%;color:#a94442;background-color:#f2dede;text-align:center;font-size: large; font-family: Verdana, Geneva, Tahoma, sans-serif;'>
    <br/>
    <br/>
<span >  Failed to {failure} {guid}.</span>
<br/>
<br/>

<span >  Client id not found.</span>

</div>
</body>
</html>
";
            return html;
        }
        public static string Fragment(string guid)
        {
            string html = $@"
<div style='vertical-align:middle;margin:0;height:100%;color:#a94442;background-color:#f2dede;text-align:center;font-size: large; font-family: Verdana, Geneva, Tahoma, sans-serif;'>
    <br/>
    <br/>
<span >  Failed to restart {guid}.</span>
<br/>
<br/>

<span >  Client id not found.</span>

</div>
";
            return html;
        }
    }
}
