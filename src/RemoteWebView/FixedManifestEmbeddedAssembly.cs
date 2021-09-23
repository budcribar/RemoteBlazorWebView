using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace PeakSWC.RemoteWebView
{
    public class FixedManifestEmbeddedAssembly : Assembly
    {
        // See:
        // - https://github.com/dotnet/aspnetcore/issues/29306
        // - https://github.com/dotnet/aspnetcore/blob/master/src/FileProviders/Embedded/src/build/netstandard2.0/Microsoft.Extensions.FileProviders.Embedded.targets
        // - https://github.com/dotnet/aspnetcore/tree/master/src/FileProviders/Manifest.MSBuildTask/src
        // - https://github.com/dotnet/aspnetcore/blob/master/src/FileProviders/Embedded/src/Manifest/ManifestParser.cs

        private const string ManifestName = "Microsoft.Extensions.FileProviders.Embedded.Manifest.xml";

        private readonly Assembly _inner;

        public FixedManifestEmbeddedAssembly(Assembly inner) => _inner = inner;

        public override string Location => _inner.Location;

        public override AssemblyName GetName() => _inner.GetName();

        public override Stream? GetManifestResourceStream(string name)
        {
            var stream = _inner.GetManifestResourceStream(name);

            if (name != ManifestName || stream == null)
                return stream;

            using var reader = new StreamReader(stream);

            var xml = XDocument.Parse(reader.ReadToEnd());

            var invalidNode = xml
                .Descendants("File")
                .FirstOrDefault(node => string.IsNullOrEmpty(node.Attribute("Name")?.Value));

            if (invalidNode == null)
            {
                stream.Position = 0;
                return stream;
            }

            invalidNode.Remove();

            return new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
        }
    }
}