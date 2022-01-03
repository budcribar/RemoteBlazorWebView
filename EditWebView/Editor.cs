using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditWebView
{
    public class Editor
    {
        string text;
        string fileName;
        public Editor(string fileName)
        {
            this.fileName = fileName;
            text = File.ReadAllText(fileName);
        }

        public void Edit()
        {

        }

        public void WriteAllText(string path)
        {
            File.WriteAllText(path, text);
        }

        public void Replace(string oldValue, string newValue)
        {
            text = text.Replace($"{oldValue}", $"{newValue}");
        }

        public void Comment(string target)
        {
            text = text.Replace($"{target}", $"//{target}");
        }
        public void InsertUsing (string nameSpace) {
            text = text.Replace("using System;", $"using System;\nusing {nameSpace};");
        }
    }
}
