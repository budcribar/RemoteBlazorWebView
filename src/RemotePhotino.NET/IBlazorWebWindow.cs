using System;
using System.Collections.Generic;
using System.Drawing;

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    public interface IBlazorWebWindow : IBlazorWebWindowBase, IDisposable
    {
        IntPtr WindowHandle { get; }

        IBlazorWebWindow? Parent { get; }
        List<IBlazorWebWindow> Children { get; }

        string Title { get; set; }
        bool Resizable { get; set; }
        Size Size { get; set; }
        int Width { get; set; }
        int Height { get; set; }
        Point Location { get; set; }
        int Left { get; set; }
        int Top { get; set; }
        IReadOnlyList<Structs.Monitor> Monitors { get; }
        Structs.Monitor MainMonitor { get; }
        uint ScreenDpi { get; }
        bool IsOnTop { get; set; }
        bool WasShown { get; }

        event EventHandler? WindowCreating;
        event EventHandler? WindowCreated;
        
        event EventHandler? WindowClosing;

        event EventHandler<Size>? SizeChanged;
        event EventHandler<Point>? LocationChanged;
        
       

        IBlazorWebWindow AddChild(IBlazorWebWindow child);
        IBlazorWebWindow RemoveChild(IBlazorWebWindow child, bool childIsDisposing);
        IBlazorWebWindow RemoveChild(Guid id, bool childIsDisposing);

        IBlazorWebWindow SetIconFile(string path);

        IBlazorWebWindow Show();
        IBlazorWebWindow Hide();
        void Close();

        IBlazorWebWindow UserCanResize(bool isResizable);
        IBlazorWebWindow Resize(Size size);
        IBlazorWebWindow Resize(int width, int height, string unit="px");
        IBlazorWebWindow Minimize();
        IBlazorWebWindow Maximize();
        IBlazorWebWindow Fullscreen();
        IBlazorWebWindow Restore();

        IBlazorWebWindow MoveTo(Point location, bool allowOutsideWorkArea);
        IBlazorWebWindow MoveTo(int left, int top, bool allowOutsideWorkArea);
        IBlazorWebWindow Offset(Point offset);
        IBlazorWebWindow Offset(int left, int top);
        IBlazorWebWindow Center();

        IBlazorWebWindow Load(Uri uri);
        IBlazorWebWindow Load(string path);
        IBlazorWebWindow LoadRawString(string content);


        IBlazorWebWindow SendWebMessage(string message);

        IBlazorWebWindow RegisterWindowClosingHandler(EventHandler handler);
        
        IBlazorWebWindow RegisterSizeChangedHandler(EventHandler<Size> handler);
        IBlazorWebWindow RegisterLocationChangedHandler(EventHandler<Point> handler);

        IBlazorWebWindow RegisterWebMessageReceivedHandler(EventHandler<string> handler);

        Guid Id { get; }
    }
}