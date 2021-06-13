using PhotinoNET.Structs;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf;
using PhotinoNET;
using System.Threading.Tasks;
using System.Reflection;
using System.Net;
using Microsoft.JSInterop;
using Photino.Blazor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PeakSwc.RemoteableWebWindows;

namespace PeakSWC.RemotePhotinoNET
{
    public class RemotePhotinoWindow : IPhotinoWindow, IDisposable
    {
        private RemoteWebWindow.RemoteWebWindowClient? client = null;
        private readonly Uri uri;
        private readonly CancellationTokenSource cts = new();

        // TODO
        //private readonly string windowTitle;
        private readonly string hostHtmlPath;
        private readonly string hostname;
        private readonly object bootLock = new();

        private Func<string, Stream?> FrameworkFileResolver { get; } = SupplyFrameworkFile;

        private Point _lastLocation;

        public Guid Id { get; set; }

        // EventHandlers
        public event EventHandler? WindowCreating;
        public event EventHandler? WindowCreated;
        public event EventHandler? WindowClosing;

        private event EventHandler<Size>? SizeChangedEvent;
        public event EventHandler<Size>? SizeChanged
        {
            add
            {
                lock (eventLock)
                {
                    JSRuntime?.InvokeVoidAsync("RemotePhotino.setResizeEventHandlerAttached", new object[] { true });
                    SizeChangedEvent += value;
                }

            }
            remove
            {
                lock (eventLock)
                {
                    SizeChangedEvent -= value;

                    if (SizeChangedEvent == null || SizeChangedEvent.GetInvocationList().Length == 0)
                        JSRuntime?.InvokeVoidAsync("RemotePhotino.setResizeEventHandlerAttached", new object[] { false });
                }
            }
        }

        private readonly object eventLock = new object();

        private event EventHandler<Point>? LocationChangedEvent;
        public event EventHandler<Point>? LocationChanged
        {
            add
            {
                lock (eventLock)
                {
                    JSRuntime?.InvokeVoidAsync("RemotePhotino.setLocationEventHandlerAttached", new object[] { true });
                    LocationChangedEvent += value;
                }

            }
            remove
            {
                lock (eventLock)
                {
                    LocationChangedEvent -= value;

                    if (LocationChangedEvent == null || LocationChangedEvent.GetInvocationList()?.Length == 0)
                        JSRuntime?.InvokeVoidAsync("RemotePhotino.setLocationEventHandlerAttached", new object[] { false });
                }
            }
        }
        public event EventHandler<string>? WebMessageReceived;

        public static Stream? SupplyFrameworkFile(string uri)
        {
            try
            {
                // TODO
                if (Path.GetFileName(uri) == "remote.blazor.desktop.js")
                    return Assembly.GetExecutingAssembly().GetManifestResourceStream("PeakSWC.RemotePhotino.NET.remote.blazor.desktop.js");

                if (File.Exists(uri))
                    return File.OpenRead(uri);
            }
            catch (Exception) { return null; }

            return null;
        }

        private RemoteWebWindow.RemoteWebWindowClient Client
        {
            get
            {
                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(uri);

                    client = new RemoteWebWindow.RemoteWebWindowClient(channel);
                    var events = client.CreateWebWindow(new CreateWebWindowRequest { Id = Id.ToString(), HtmlHostPath = hostHtmlPath, Hostname = hostname }, cancellationToken: cts.Token); // TODO parameter names
                    var completed = new ManualResetEventSlim();

                    _ = Task.Run(async () =>
                      {
                          try
                          {
                              await foreach (var message in events.ResponseStream.ReadAllAsync())
                              {
                                  var command = message.Response.Split(':')[0];
                                  var data = message.Response.Substring(message.Response.IndexOf(':') + 1);
                                  try
                                  {
                                      switch (command)
                                      {
                                          case "created":
                                              completed.Set();
                                              break;
                                          case "webmessage":
                                              if (data == "booted:")
                                              {
                                                  lock (bootLock)
                                                  {
                                                      Shutdown();
                                                      WindowClosing?.Invoke(this, new());
                                                  }
                                              }
                                              else if (data == "connected:")
                                                  WindowCreated?.Invoke(this, new());
                                              else if (data.StartsWith("size:"))
                                              {

                                                  var jo = JsonConvert.DeserializeObject<JObject>(data.Replace("size:", ""));
                                                  var size = new Size(jo?["Width"]?.Value<int>() ?? 0, jo?["Height"]?.Value<int>() ?? 0);
                                                  this.InitSize = size;
                                                  // TODO don't throw size changed on initial set
                                                  IDispatcher? pd = PlatformDispatcher as PlatformDispatcher;
                                                  if (pd != null)
                                                      await pd.InvokeAsync(() => {
                                                          SizeChangedEvent?.Invoke(null, size);
                                                      });


                                              }
                                              else if (data.StartsWith("location:"))
                                              {


                                                  var jo = JsonConvert.DeserializeObject<JObject>(data.Replace("location:", ""));
                                                  var location = new Point(jo?["X"]?.Value<int>() ?? 0, jo?["Y"]?.Value<int>() ?? 0);
                                                  InitLocation = location;
                                                  PlatformDispatcher? pd = PlatformDispatcher as PlatformDispatcher;
                                                  if (pd != null)
                                                      await pd.InvokeAsync(() => {
                                                          LocationChangedEvent?.Invoke(null, location);
                                                      });
                                              }
                                              else if (data.StartsWith("title:"))
                                              {
                                                  InitTitle = data.Replace("title:", "").Trim();
                                              }
                                              else
                                                  OnWebMessageReceived(data);
                                              break;


                                      }
                                  }
                                  catch (Exception ex)
                                  {
                                      var m = ex.Message;
                                  }

                              }
                          }
                          catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                          {
                              OnWindowClosing();
                              Console.WriteLine("Stream cancelled.");  //TODO
                          }
                          catch (Exception)
                          {
                              // TODO
                              // exceptions will stop ui 
                          }
                      }, cts.Token);

                    completed.Wait();

                    Task.Run(async () =>
                    {
                        var files = client.FileReader();

                        await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id.ToString(), Path = "Initialize" });

                        // TODO Use multiple threads to read files - See RemoteableWindWindow.cs from RemoteBlazorWebView
                        await foreach (var message in files.ResponseStream.ReadAllAsync())
                        {
                            var bytes = FrameworkFileResolver(message.Path) ?? null;
                            await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id.ToString(), Path = message.Path, Data = bytes == null ? null : ByteString.FromStream(bytes) });
                        }

                    }, cts.Token);

                }

                return client;
            }
        }

        private void Shutdown()
        {
            Client.Shutdown(new IdMessageRequest { Id = Id.ToString() });
        }

        /// <summary>
        /// Send a message to the window's JavaScript context.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        public IPhotinoWindow SendWebMessage(string message)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".SendWebMessage(string message)");
            Client.SendMessage(new SendMessageRequest { Id = Id.ToString(), Message = message }, new());
            return this;
        }

        public IntPtr WindowHandle => throw new NotImplementedException();

        public IPhotinoWindow Parent => throw new NotImplementedException();

        public IReadOnlyList<PhotinoNET.Structs.Monitor> Monitors => new List<PhotinoNET.Structs.Monitor>();

        public PhotinoNET.Structs.Monitor MainMonitor => throw new NotImplementedException();

        public List<IPhotinoWindow> Children => throw new NotImplementedException();

        public uint ScreenDpi => 0;

        public IDispatcher? PlatformDispatcher { get; set; } 

        public IJSRuntime? JSRuntime { get; set; }
        
        private string InitTitle {set {_title = string.IsNullOrEmpty(value.Trim()) ? "Untitled Window" : value; } }

        private string _title = "Untitled Window";
        public string Title
        {
            get => _title;
            set
            {
                if (string.IsNullOrEmpty(value.Trim()))
                    value = "Untitled Window";
                JSRuntime?.InvokeVoidAsync("RemotePhotino.setTitle", new object[] { value.Trim() });
            }
        }

        private Size InitSize { set { _lastSize = value;  } }

        private Size _lastSize;
        public Size Size
        {
            get => _lastSize;
               
            set
            {
                // ToDo:
                // Should this be locked if _isResizable == false?
                if (_width != value.Width || _height != value.Height)
                {
                    _width = value.Width;
                    _height = value.Height;
                    JSRuntime?.InvokeVoidAsync("RemotePhotino.setSize", new object[] { new Size(_width,_height) });
                }
            }
        }

        private int _width;
        public int Width
        {
            get => this.Size.Width;
            set
            {
                Size currentSize = this.Size;

                if (currentSize.Width != value)
                {
                    _width = value;
                    this.Size = new Size(_width, currentSize.Height);
                }
            }
        }

        private int _height;
        public int Height
        {
            get => this.Size.Height;
            set
            {
                Size currentSize = this.Size;

                if (currentSize.Height != value)
                {
                    _height = value;
                    this.Size = new Size(currentSize.Width, _height);
                }
            }
        }
        
        private Point InitLocation { set { _left = value.X; _top = value.Y; } }

        public Point Location
        {
            get
            {
                return new Point { X = _left, Y = _top };
            }
            set
            {
                if (_left != value.X || _top != value.Y)
                {
                    _left = value.X;
                    _top = value.Y;
                    JSRuntime?.InvokeVoidAsync("RemotePhotino.setLocation", new object[] { new Point(value.X, value.Y) });
                }
            }
        }

        private int _left;
        public int Left
        {
            get => this.Location.X;
            set
            {
                Point currentLocation = this.Location;

                if (currentLocation.X != value)
                {
                    _left = value;
                    this.Location = new Point(_left, currentLocation.Y);
                }
            }
        }

        private int _top;
        public int Top
        {
            get => this.Location.Y;
            set
            {
                Point currentLocation = this.Location;

                if (currentLocation.Y != value)
                {
                    _top = value;
                    this.Location = new Point(currentLocation.X, _left);
                }
            }
        }

        public bool IsOnTop { get => false; set { throw new NotImplementedException(); } }

        private int _logVerbosity;
        ///<summary>0 = Critical Only, 1 = Critical and Warning, 2 = Verbose, >2 = All Details</summary>
        public int LogVerbosity
        {
            get => _logVerbosity;
            set { _logVerbosity = value; }
        }

        /// <summary>
        /// Shows the current IPhotinoWindow instance window.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Show()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Show()");

            _ = Client;
            //Client.Show(new IdMessageRequest { Id = Id.ToString() });

            // Is used to indicate that the window was
            // shown to the user at least once. Some
            // functionality like registering custom
            // scheme handlers can only be executed on
            // the native window before it was shown the
            // first time.
            _wasShown = true;

            return this;
        }

        private bool _wasShown = false;
        public bool WasShown => _wasShown;

        /// <summary>
        /// Sets whether the user can resize the current window or not.
        /// </summary>
        /// <param name="isResizable">Let user resize window</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow UserCanResize(bool isResizable = true)
        {
            return this;
        }

        /// <summary>
        /// Adds a child IPhotinoWindow instance to the current instance.
        /// </summary>
        /// <param name="child">The IPhotinoWindow child instance to be added</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow AddChild(IPhotinoWindow child)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Removes a child IPhotinoWindow instance from the current instance.
        /// </summary>
        /// <param name="child">The IPhotinoWindow child instance to be removed</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow RemoveChild(IPhotinoWindow child, bool childIsDisposing = false)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Removes a child IPhotinoWindow instance identified by its Id from the current instance.
        /// </summary>
        /// <param name="id">The Id of the IPhotinoWindow child instance to be removed</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow RemoveChild(Guid id, bool childIsDisposing = false)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Set the window icon file
        /// </summary>
        /// <param name="path">The path to the icon file</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow SetIconFile(string path)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".SetIconFile(string path)");

            // ToDo:
            // Determine if Path.GetFullPath is always safe to use.
            // Perhaps it needs to be constrained to the application
            // root folder?


            return this;
        }

        /// <summary>
        /// Hides the current IPhotinoWindow instance window.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Hide()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Hide()");

            throw new NotImplementedException("Hide is not yet implemented in PhotinoNET.");
        }

        public bool Resizable { get; set; } = true;

        /// <summary>
        /// Resizes the current window instance using a Size struct.
        /// </summary>
        /// <param name="size">The Size struct for the window containing width and height</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Resize(Size size)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Resize(Size size)");

            if (LogVerbosity > 2)
            {
                Console.WriteLine($"Current size: {this.Size}");
                Console.WriteLine($"New size: {size}");
            }

            // Save last size
            _lastSize = this.Size;

            // Don't allow window size values smaller than 0px
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new ArgumentOutOfRangeException($"Window width and height must be greater than 0. (Invalid Size: {size}.)");
            }

            // Don't allow window to be bigger than work area
            // TODO
            //Size workArea = this.MainMonitor.WorkArea.Size;
            //size = new Size(
            //    size.Width <= workArea.Width ? size.Width : workArea.Width,
            //    size.Height <= workArea.Height ? size.Height : workArea.Height
            //);

            this.Size = size;

            return this;
        }
        /// <summary>
        /// Resizes the current window instance using width and height.
        /// </summary>
        /// <param name="width">The width for the window</param>
        /// <param name="height">The height for the window</param>
        /// <param name="unit">Unit of the given dimensions: px (default), %, percent</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Resize(int width, int height, string unit = "px")
        {
            // TODO bad log message
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Resize(int width, int height, bool isPercentage)");

            Size size;

            switch (unit)
            {
                case "px":
                case "pixel":
                    size = new Size(width, height);

                    break;
                case "%":
                case "percent":
                case "percentage":
                    // Check if the given values are in range. Prevents divide by zero.
                    if (width < 1 || width > 100)
                    {
                        throw new ArgumentOutOfRangeException("Resize width % must be between 1 and 100.");
                    }

                    if (height < 1 || height > 100)
                    {
                        throw new ArgumentOutOfRangeException("Resize height % must be between 1 and 100.");
                    }

                    // TODO MainMonitor will throw exception
                    // Calculate window size based on main monitor work area
                    size = new Size
                    {
                        Width = (int)Math.Round((decimal)(this.MainMonitor.WorkArea.Width / 100 * width), 0),
                        Height = (int)Math.Round((decimal)(this.MainMonitor.WorkArea.Height / 100 * height), 0)
                    };

                    break;
                default:
                    throw new ArgumentException($"Unit \"{unit}\" is not a valid unit for window resize.");
            }

            return this.Resize(size);
        }

        /// <summary>
        /// Minimizes the window into the system tray.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Minimize()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Minimize()");

            throw new NotImplementedException("Minimize is not yet implemented in PhotinoNET.");
        }
        /// <summary>
        /// Maximizes the window to fill the work area.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Maximize()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Maximize()");

            // TODO MainMonitor will throw exception
            Size workArea = this.MainMonitor.WorkArea.Size;

            return this
                .MoveTo(0, 0)
                .Resize(workArea.Width, workArea.Height);
        }


        /// <summary>
        /// Makes the window fill the whole screen area 
        /// without borders or OS interface.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Fullscreen()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Fullscreen()");

            throw new NotImplementedException("Fullscreen is not yet implemented in PhotinoNET.");
        }
        /// <summary>
        /// Restores the previous window size and position.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Restore()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Restore()");

            if (LogVerbosity > 2)
            {
                Console.WriteLine($"Last location: {_lastLocation}");
                Console.WriteLine($"Last size: {_lastSize}");
            }

            bool isRestorable = _lastSize.Width > 0 && _lastSize.Height > 0;

            if (isRestorable == false)
            {
                if (LogVerbosity > 0)
                    Console.WriteLine("Can't restore previous window state.");
                return this;
            }

            return this
                .Resize(_lastSize)
                .MoveTo(_lastLocation, true); // allow moving to outside work area in case the previous window Rect was outside.
        }
        /// <summary>
        /// Moves the window to the specified location 
        /// on the screen using a Point struct.
        /// </summary>
        /// <param name="location">The Point struct defining the window location</param>
        /// <param name="allowOutsideWorkArea">Allow the window to move outside the work area of the monitor</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow MoveTo(Point location, bool allowOutsideWorkArea = false)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Move(Point location)");

            if (LogVerbosity > 2)
            {
                Console.WriteLine($"Current location: {this.Location}");
                Console.WriteLine($"New location: {location}");
            }

            // Save last location
            _lastLocation = this.Location;

            // Check if the window is within the work area.
            // If the window is outside of the work area,
            // recalculate the position and continue.
            if (allowOutsideWorkArea == false)
            {
                // TODO MainMonitor will throw exception
                int horizontalWindowEdge = location.X + this.Width; // x position + window width
                int verticalWindowEdge = location.Y + this.Height; // y position + window height

                int horizontalWorkAreaEdge = this.MainMonitor.WorkArea.Width; // like 1920 (px)
                int verticalWorkAreaEdge = this.MainMonitor.WorkArea.Height; // like 1080 (px)

                bool isOutsideHorizontalWorkArea = horizontalWindowEdge > horizontalWorkAreaEdge;
                bool isOutsideVerticalWorkArea = verticalWindowEdge > verticalWorkAreaEdge;

                Point locationInsideWorkArea = new(
                    isOutsideHorizontalWorkArea ? horizontalWorkAreaEdge - this.Width : location.X,
                    isOutsideVerticalWorkArea ? verticalWorkAreaEdge - this.Height : location.Y
                );

                location = locationInsideWorkArea;
            }

            this.Location = location;

            return this;
        }
        /// <summary>
        /// Moves the window relative to its current location
        /// on the screen using a Point struct.
        /// </summary>
        /// <param name="offset">The Point struct defining the location offset</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Offset(Point offset)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Offset(Point offset)");

            Point location = this.Location;

            int left = location.X + offset.X;
            int top = location.Y + offset.Y;

            return this.MoveTo(left, top);
        }

        /// <summary>
        /// Moves the window relative to its current location
        /// on the screen using left and top coordinates.
        /// </summary>
        /// <param name="left">The location offset from the left</param>
        /// <param name="top">The location offset from the top</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Offset(int left, int top)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Offset(int left, int top)");

            return this.Offset(new Point(left, top));
        }

        /// <summary>
        /// Closes the current IPhotinoWindow instance. Also closes
        /// all children of the current IPhotinoWindow instance.
        /// </summary>
        public void Close()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Close()");

            // TODO
        }

        /// <summary>
        /// Wait for the current window to close and send exit
        /// signal to the native WebView instance.
        /// </summary>
        public void WaitForClose()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".WaitForClose()");

            while (true)
            {
                // TODO
                Thread.Sleep(1000);
            }
        }

        #region TODO


        // Static API Members
        public static bool IsWindowsPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsMacOsPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static bool IsLinuxPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        //public event EventHandler<string> ;

        /// <summary>
        /// Creates a new PhotinoWindow instance with
        /// the supplied arguments. Register WindowCreating and
        /// WindowCreated handlers in the configure action, they
        /// are triggered in the constructor, whereas handlers
        /// that are registered otherwise will be triggered
        /// after the native PhotinoWindow instance was created.
        /// </summary>
        /// <param name="title">The window title</param>
        /// <param name="configure">PhotinoWindow options configuration</param>
        /// <param name="width">The window width</param>
        /// <param name="height">The window height</param>
        /// <param name="left">The position from the left side of the screen</param>
        /// <param name="top">The position from the top side of the screen</param>
        /// <param name="fullscreen">Open window in fullscreen mode</param>
        
        public RemotePhotinoWindow(
            Uri uri,
            string hostHtmlPath,
            string title,
            Guid id = default,
            Action<PhotinoWindowOptions>? configure = null,
            int width = 800,
            int height = 600,
            int left = 20,
            int top = 20,
            bool fullscreen = false)
        {
            this.uri = uri;
            this.Id = id == default ? Guid.NewGuid() : id;
            this.hostHtmlPath = hostHtmlPath;
            this.hostname = Dns.GetHostName();
           

            // Configure Photino instance
            var options = new PhotinoWindowOptions();
            configure?.Invoke(options);

            this.RegisterEventHandlersFromOptions(options);


           
            // TODO Need to set up JSRuntime first 
            //this.Title = title;

            // Fire pre-create event handlers
            this.OnWindowCreating();

            foreach (var (scheme, handler) in options.CustomSchemeHandlers)
            {
                this.RegisterCustomSchemeHandler(scheme, handler);
            }

            // Fire post-create event handlers
            this.OnWindowCreated();
        }

        public void Dispose()
        {
            
        }

        /// <summary>
        /// Moves the window to the specified location
        /// on the screen using left and right position.
        /// </summary>
        /// <param name="left">The location from the left of the screen</param>
        /// <param name="top">The location from the top of the screen</param>
        /// <param name="allowOutsideWorkArea">Allow the window to move outside the work area of the monitor</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow MoveTo(int left, int top, bool allowOutsideWorkArea = false)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Move(int left, int top)");
            
            return this.MoveTo(new Point(left, top), allowOutsideWorkArea);
        }

       
        /// <summary>
        /// Centers the window on the main monitor work area.
        /// </summary>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Center()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Center()");

            Size workAreaSize = this.MainMonitor.WorkArea.Size;

            Point centeredPosition = new Point(
                ((workAreaSize.Width / 2) - (this.Width / 2)),
                ((workAreaSize.Height / 2) - (this.Height / 2))
            );

            return this.MoveTo(centeredPosition);
        }

        /// <summary>
        /// Loads a URI resource into the window view.
        /// </summary>
        /// <param name="uri">The URI to the resource</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Load(Uri uri)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Load(Uri uri)");

            // Navigation only works after the window was shown once.
            if (this.WasShown == false)
            {
                this.Show();
            }
            
            // ––––––––––––––––––––––
            // SECURITY RISK!
            // This needs validation!
            // ––––––––––––––––––––––
            //Photino_NavigateToUrl(_nativeInstance, uri.ToString());

            return this;
        }

        /// <summary>
        /// Loads a path resource into the window view.
        /// </summary>
        /// <param name="path">The path to the resource</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow Load(string path)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".Load(string path)");
            
            // ––––––––––––––––––––––
            // SECURITY RISK!
            // This needs validation!
            // ––––––––––––––––––––––
            // Open a web URL string path
            if (path.Contains("http://") || path.Contains("https://"))
            {
                return this.Load(new Uri(path));
            }

            // Open a file resource string path
            string absolutePath = Path.GetFullPath(path);

            // For bundled app it can be necessary to consider
            // the app context base directory. Check there too.
            if (File.Exists(absolutePath) == false)
            {
                absolutePath = $"{System.AppContext.BaseDirectory}/{path}";

                // If the file does not exist on this path,
                // send an error message to user.
                if (File.Exists(absolutePath) == false)
                {
                    Console.WriteLine($"File \"{path}\" could not be found.");
                    return this;
                }
            }

            return this.Load(new Uri(absolutePath, UriKind.Absolute));
        }

        /// <summary>
        /// Loads a raw string into the window view, like HTML.
        /// </summary>
        /// <param name="content">The raw string resource</param>
        /// <returns>The current IPhotinoWindow instance</returns>
        public IPhotinoWindow LoadRawString(string content)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".LoadRawString(string content)");

            // Navigation only works after the window was shown once.
            if (this.WasShown == false)
            {
                this.Show();
            }

            //Photino_NavigateToString(_nativeInstance, content);

            return this;
        }

        /// <summary>
        /// Opens a native alert window with a title and message.
        /// </summary>
        /// <param name="title">The window title.</param>
        /// <param name="message">The window message body.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        public IPhotinoWindow OpenAlertWindow(string title, string message)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OpenAlertWindow(string title, string message)");

            // TODO OpenAlertWindow
            //Client.ShowMessage(new ShowMessageRequest { Id = Id.ToString(), Body = message, Title = title });
            // Bug:
            // Closing the message shown with the OpenAlertWindow
            // method closes the sender window as well.
            //Invoke(() => Photino_ShowMessage(_nativeInstance, title, message, /* MB_OK */ 0));

            return this;
        }

        

        /// <summary>
        /// Register event handlers from options on window init,
        /// both publicly accessible and private handlers can be registered.
        /// </summary>
        /// <param name="options"></param>
        private void RegisterEventHandlersFromOptions(PhotinoWindowOptions options)
        {
            if (options.WindowCreatingHandler != null)
            {
                this.RegisterWindowCreatingHandler(options.WindowCreatingHandler);
            }

            if (options.WindowCreatedHandler != null)
            {
                this.RegisterWindowCreatedHandler(options.WindowCreatedHandler);
            }
            
            if (options.WindowClosingHandler != null)
            {
                this.RegisterWindowClosingHandler(options.WindowClosingHandler);
            }

            if (options.SizeChangedHandler != null)
            {
                this.RegisterSizeChangedHandler(options.SizeChangedHandler);
            }

            if (options.LocationChangedHandler != null)
            {
                this.RegisterLocationChangedHandler(options.LocationChangedHandler);
            }
            
            if (options.WebMessageReceivedHandler != null)
            {
                this.RegisterWebMessageReceivedHandler(options.WebMessageReceivedHandler);
            }
        }

        // Register public event handlers

        /// <summary>
        /// Register a handler that is fired on a window closing event.
        /// </summary>
        /// <param name="handler">A handler that accepts a IPhotinoWindow argument.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        public IPhotinoWindow RegisterWindowClosingHandler(EventHandler handler)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".RegisterWindowClosingHandler(EventHandler handler)");
            
            this.WindowClosing += handler;

            return this;
        }

        // Register private event handlers

        /// <summary>
        /// Register a handler that is fired on a window creating event.
        /// Can only be registered in IPhotinoWindowOptions.
        /// </summary>
        /// <param name="handler">A handler that accepts a IPhotinoWindow argument.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        private IPhotinoWindow RegisterWindowCreatingHandler(EventHandler handler)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".RegisterWindowCreatingHandler(EventHandler handler)");
            
            this.WindowCreating += handler;

            return this;
        }
        
        /// <summary>
        /// Register a handler that is fired on a window created event.
        /// Can only be registered in IPhotinoWindowOptions.
        /// </summary>
        /// <param name="handler">A handler that accepts a IPhotinoWindow argument.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        private IPhotinoWindow RegisterWindowCreatedHandler(EventHandler handler)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".RegisterWindowCreatedHandler(EventHandler handler)");
            
            this.WindowCreated += handler;

            return this;
        }

        // Register native event handlers

        /// <summary>
        /// Register a custom request path scheme that matches a url
        /// scheme like "app", "api" or "assets".  Some schemes can't 
        /// be used because they're already in use like "http" or "file".
        /// A url path like "api://some-resource" can be caught with a 
        /// scheme handler like this and dynamically processed on the backend.
        /// 
        /// Can only be registered in IPhotinoWindowOptions.
        /// </summary>
        /// <param name="scheme">Name of the scheme, like "app".</param>
        /// <param name="handler">Handler that processes a request path.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        private IPhotinoWindow RegisterCustomSchemeHandler(string scheme, CustomSchemeDelegate handler)
        {
            // Because of WKWebView limitations, this can only be called during the constructor
            // before the first call to Show. To enforce this, it's private and is only called
            // in response to the constructor options.
            if (this.WasShown == true)
            {
                throw new InvalidOperationException("Can only register custom scheme handlers from within the PhotinoWindowOptions context.");
            }

            //WebResourceRequestDelegate callback = (string url, out int numBytes, out string contentType) =>
            //{
            //    var responseStream = handler(url, out contentType);
            //    if (responseStream == null)
            //    {
            //        // Webview should pass through request to normal handlers (e.g., network)
            //        // or handle as 404 otherwise
            //        numBytes = 0;
            //        return default;
            //    }

            //    // Read the stream into memory and serve the bytes
            //    // In the future, it would be possible to pass the stream through into C++
            //    using (responseStream)
            //    using (var ms = new MemoryStream())
            //    {
            //        responseStream.CopyTo(ms);

            //        numBytes = (int)ms.Position;
            //        var buffer = Marshal.AllocHGlobal(numBytes);
            //        Marshal.Copy(ms.GetBuffer(), 0, buffer, numBytes);
            //        _hGlobalToFree.Add(buffer);
            //        return buffer;
            //    }
            //};

            //_gcHandlesToFree.Add(GCHandle.Alloc(callback));
            //Invoke(() => Photino_AddCustomScheme(_nativeInstance, scheme, callback));

            return this;
        }

        /// <summary>
        /// Register a handler that is fired on a size changed event.
        /// </summary>
        /// <param name="handler">A handler that accepts a IPhotinoWindow and Size argument.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        public IPhotinoWindow RegisterSizeChangedHandler(EventHandler<Size> handler)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".RegisterSizeChangedHandler(EventHandler<Size> handler)");
            
            this.SizeChanged += handler;

            return this;
        }

        /// <summary>
        /// Register a handler that is fired on a location changed event.
        /// </summary>
        /// <param name="handler">A handler that accepts a IPhotinoWindow and Point argument.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        public IPhotinoWindow RegisterLocationChangedHandler(EventHandler<Point> handler)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".RegisterLocationChangedHandler(EventHandler<Point> handler)");
            
            this.LocationChanged += handler;

            return this;
        }

        /// <summary>
        /// Register a handler that is fired on a web message received event.
        /// </summary>
        /// <param name="handler">A handler that accepts a IPhotinoWindow argument.</param>
        /// <returns>The current IPhotinoWindow instance.</returns>
        public IPhotinoWindow RegisterWebMessageReceivedHandler(EventHandler<string> handler)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".RegisterWebMessageReceivedHandler(EventHandler<string> handler)");
            
            this.WebMessageReceived += handler;

            return this;
        }

        // Invoke public event handlers
        private void OnWindowCreating()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OnWindowCreating()");

            this.WindowCreating?.Invoke(this, EventArgs.Empty);
        }
        
        private void OnWindowCreated()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OnWindowCreated()");

            this.WindowCreated?.Invoke(this, EventArgs.Empty);
        }

        private void OnWindowClosing()
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OnWindowClosing()");

            this.WindowClosing?.Invoke(this, EventArgs.Empty);
        }

        // Invoke native event handlers
        // These event handlers are called from inside
        // the native window context and are not handled.
        // Don't forget to add new handlers to the
        // garbage collector along with existing ones.
        private void OnClosing()
        {

        }

        private void OnSizeChanged(int width, int height)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OnSizeChanged(int width, int height)");

            this.SizeChangedEvent?.Invoke(this, new Size(width, height));
        }

        private void OnLocationChanged(int left, int top)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OnLocationChanged(int left, int top)");

            this.LocationChangedEvent?.Invoke(this, new Point(left, top));
        }

        private void OnWebMessageReceived(string message)
        {
            if (this.LogVerbosity > 1)
                Console.WriteLine($"Executing: \"{this.Title ?? "RemotePhotinoWindow"}\".OnMWebessageReceived(string message)");

            this.WebMessageReceived?.Invoke(this, message);
        }

        public IPhotinoWindowBase OpenAlertWindowBase(string title, string message)
        {
            return OpenAlertWindow(title, message);
        }

        public IPhotinoWindowBase SendWebMessageBase(string message)
        {
            return SendWebMessage(message);
        }

        public IPhotinoWindowBase LoadBase(string path)
        {
            return Load(path);
        }

        #endregion

    }
}