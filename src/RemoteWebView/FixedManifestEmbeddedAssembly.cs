using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace PeakSWC.RemoteWebView
{
    public class FixedManifestEmbeddedAssembly(Assembly inner) : Assembly
    {
        // See:
        // - https://github.com/dotnet/aspnetcore/issues/29306
        // - https://github.com/dotnet/aspnetcore/blob/master/src/FileProviders/Embedded/src/build/netstandard2.0/Microsoft.Extensions.FileProviders.Embedded.targets
        // - https://github.com/dotnet/aspnetcore/tree/master/src/FileProviders/Manifest.MSBuildTask/src
        // - https://github.com/dotnet/aspnetcore/blob/master/src/FileProviders/Embedded/src/Manifest/ManifestParser.cs

        private const string ManifestName = "Microsoft.Extensions.FileProviders.Embedded.Manifest.xml";

        public override string Location => inner.Location;

        public override AssemblyName GetName() => inner.GetName();

        public override Stream? GetManifestResourceStream(string name)
        {
            var stream = inner.GetManifestResourceStream(name);

            if (name != ManifestName || stream == null)
                return stream;

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            XDocument xml;
            try
            {
                xml = XDocument.Parse(content);
            }
            catch
            {
                return new MemoryStream(Encoding.UTF8.GetBytes(content));
            }

            var invalidNodes = xml
                .Descendants("File")
                .Where(node => string.IsNullOrEmpty(node.Attribute("Name")?.Value))
                .ToList();

            foreach (var invalidNode in invalidNodes)
            {
                invalidNode.Remove();
            }

            return new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
        }
    }
}
