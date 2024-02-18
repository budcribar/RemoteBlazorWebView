namespace PeakSWC.RemoteWebView.Pages
{
    public class ContactInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
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
            margin: 40px;
            color: #333;
        }}
        .container {{
            max-width: 600px;
            margin: auto;
            padding: 20px;
            border: 1px solid #ddd;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        h1 {{
            color: #0056b3;
            text-align: center;
        }}
        p {{
            line-height: 1.6;
        }}
        .label {{
            font-weight: bold;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Contact Information</h1>
        <p><span class=""label"">Name:</span> {contact.Name}</p>
        <p><span class=""label"">Company:</span> {contact.Company}</p>
        <p><span class=""label"">Email:</span> {contact.Email}</p>
        <p><span class=""label"">Version:</span> {version}</p>
    </div>
</body>
</html>";
            return htmlTemplate;
        }
    }

}
