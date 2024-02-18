namespace PeakSWC.RemoteWebView.Pages
{
    public class ContactInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public static class HtmlPageGenerator
    {
        public static string GenerateContactPage(ContactInfo contact, string version)
        {
            string htmlTemplate = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Contact Information</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 0;
            padding: 0;
            background-color: #f4f4f4;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
        }}
        .card {{
            background: #ffffff;
            border-radius: 10px;
            box-shadow: 0 4px 8px 0 rgba(0,0,0,0.2);
            transition: 0.3s;
            width: 40%;
            min-width: 300px;
        }}
        .card:hover {{
            box-shadow: 0 8px 16px 0 rgba(0,0,0,0.2);
        }}
        .container {{
            padding: 2px 16px;
        }}
        h1 {{
            color: #0056b3;
            font-size: 24px;
            text-align: center;
            margin-top: 16px;
        }}
        p {{
            font-size: 16px;
            line-height: 1.6;
        }}
        .label {{
            font-weight: bold;
        }}
    </style>
</head>
<body>
   <div class=""card"">
    <div class=""container"">
        <h1>Contact Information</h1>
        <p><span class=""label"">Name:</span> {contact.Name}</p>
        <p><span class=""label"">Company:</span> {contact.Company}</p>
        <p><span class=""label"">Email:</span> <a href=""mailto:{contact.Email}"">{contact.Email}</a></p>
        <p><span class=""label"">Source:</span> <a href=""{contact.Url}"" target=""_blank"">{contact.Url}</a></p>
        <p><span class=""label"">Version:</span> {version}</p>
    </div>
</div>

</body>
</html>";
            return htmlTemplate;
        }
    }


}
