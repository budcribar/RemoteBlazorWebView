using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photino.Blazor
{
    public interface IPCAdapter
    {
        event EventHandler<string> WebMessageReceived;
        IPCAdapter SendWebMessage(string message);
    }
}
