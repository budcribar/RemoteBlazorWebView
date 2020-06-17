// package: webwindow
// file: webwindow.proto

var webwindow_pb = require("./webwindow_pb");
var google_protobuf_empty_pb = require("google-protobuf/google/protobuf/empty_pb");
var grpc = require("@improbable-eng/grpc-web").grpc;

var RemoteWebWindow = (function () {
  function RemoteWebWindow() {}
  RemoteWebWindow.serviceName = "webwindow.RemoteWebWindow";
  return RemoteWebWindow;
}());

RemoteWebWindow.GetHeight = {
  methodName: "GetHeight",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.IntMessageResponse
};

RemoteWebWindow.SetHeight = {
  methodName: "SetHeight",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IntMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetLeft = {
  methodName: "GetLeft",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.IntMessageResponse
};

RemoteWebWindow.SetLeft = {
  methodName: "SetLeft",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IntMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetLocation = {
  methodName: "GetLocation",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.PointMessageResponse
};

RemoteWebWindow.SetLocation = {
  methodName: "SetLocation",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.PointMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetMonitors = {
  methodName: "GetMonitors",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.MonitorResponse
};

RemoteWebWindow.GetResizable = {
  methodName: "GetResizable",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.BoolResponse
};

RemoteWebWindow.SetResizable = {
  methodName: "SetResizable",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.BoolRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetScreenDpi = {
  methodName: "GetScreenDpi",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.UInt32Response
};

RemoteWebWindow.SendMessage = {
  methodName: "SendMessage",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.SendMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.SetIconFile = {
  methodName: "SetIconFile",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.SendMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.Show = {
  methodName: "Show",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.ShowMessage = {
  methodName: "ShowMessage",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.ShowMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetSize = {
  methodName: "GetSize",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.SizeMessageResponse
};

RemoteWebWindow.SetSize = {
  methodName: "SetSize",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.SizeMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetTitle = {
  methodName: "GetTitle",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.StringResponse
};

RemoteWebWindow.SetTitle = {
  methodName: "SetTitle",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.StringRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetTop = {
  methodName: "GetTop",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.IntMessageResponse
};

RemoteWebWindow.SetTop = {
  methodName: "SetTop",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IntMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.NavigateToLocalFile = {
  methodName: "NavigateToLocalFile",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.FileMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.NavigateToString = {
  methodName: "NavigateToString",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.StringRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.NavigateToUrl = {
  methodName: "NavigateToUrl",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.UrlMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.WaitForExit = {
  methodName: "WaitForExit",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.GetWidth = {
  methodName: "GetWidth",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.IntMessageResponse
};

RemoteWebWindow.SetWidth = {
  methodName: "SetWidth",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IntMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.CreateWebWindow = {
  methodName: "CreateWebWindow",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: true,
  requestType: webwindow_pb.CreateWebWindowRequest,
  responseType: webwindow_pb.WebMessageResponse
};

RemoteWebWindow.FileReader = {
  methodName: "FileReader",
  service: RemoteWebWindow,
  requestStream: true,
  responseStream: true,
  requestType: webwindow_pb.FileReadRequest,
  responseType: webwindow_pb.FileReadResponse
};

exports.RemoteWebWindow = RemoteWebWindow;

function RemoteWebWindowClient(serviceHost, options) {
  this.serviceHost = serviceHost;
  this.options = options || {};
}

RemoteWebWindowClient.prototype.getHeight = function getHeight(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetHeight, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setHeight = function setHeight(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetHeight, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getLeft = function getLeft(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetLeft, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setLeft = function setLeft(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetLeft, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getLocation = function getLocation(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetLocation, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setLocation = function setLocation(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetLocation, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getMonitors = function getMonitors(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetMonitors, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getResizable = function getResizable(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetResizable, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setResizable = function setResizable(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetResizable, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getScreenDpi = function getScreenDpi(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetScreenDpi, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.sendMessage = function sendMessage(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SendMessage, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setIconFile = function setIconFile(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetIconFile, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.show = function show(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.Show, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.showMessage = function showMessage(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.ShowMessage, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getSize = function getSize(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetSize, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setSize = function setSize(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetSize, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getTitle = function getTitle(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetTitle, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setTitle = function setTitle(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetTitle, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getTop = function getTop(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetTop, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setTop = function setTop(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetTop, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.navigateToLocalFile = function navigateToLocalFile(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.NavigateToLocalFile, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.navigateToString = function navigateToString(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.NavigateToString, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.navigateToUrl = function navigateToUrl(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.NavigateToUrl, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.waitForExit = function waitForExit(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.WaitForExit, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.getWidth = function getWidth(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetWidth, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.setWidth = function setWidth(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.SetWidth, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.createWebWindow = function createWebWindow(requestMessage, metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.invoke(RemoteWebWindow.CreateWebWindow, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onMessage: function (responseMessage) {
      listeners.data.forEach(function (handler) {
        handler(responseMessage);
      });
    },
    onEnd: function (status, statusMessage, trailers) {
      listeners.status.forEach(function (handler) {
        handler({ code: status, details: statusMessage, metadata: trailers });
      });
      listeners.end.forEach(function (handler) {
        handler({ code: status, details: statusMessage, metadata: trailers });
      });
      listeners = null;
    }
  });
  return {
    on: function (type, handler) {
      listeners[type].push(handler);
      return this;
    },
    cancel: function () {
      listeners = null;
      client.close();
    }
  };
};

RemoteWebWindowClient.prototype.fileReader = function fileReader(metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.client(RemoteWebWindow.FileReader, {
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport
  });
  client.onEnd(function (status, statusMessage, trailers) {
    listeners.status.forEach(function (handler) {
      handler({ code: status, details: statusMessage, metadata: trailers });
    });
    listeners.end.forEach(function (handler) {
      handler({ code: status, details: statusMessage, metadata: trailers });
    });
    listeners = null;
  });
  client.onMessage(function (message) {
    listeners.data.forEach(function (handler) {
      handler(message);
    })
  });
  client.start(metadata);
  return {
    on: function (type, handler) {
      listeners[type].push(handler);
      return this;
    },
    write: function (requestMessage) {
      client.send(requestMessage);
      return this;
    },
    end: function () {
      client.finishSend();
    },
    cancel: function () {
      listeners = null;
      client.close();
    }
  };
};

exports.RemoteWebWindowClient = RemoteWebWindowClient;

var BrowserIPC = (function () {
  function BrowserIPC() {}
  BrowserIPC.serviceName = "webwindow.BrowserIPC";
  return BrowserIPC;
}());

BrowserIPC.ReceiveMessage = {
  methodName: "ReceiveMessage",
  service: BrowserIPC,
  requestStream: false,
  responseStream: true,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.StringRequest
};

BrowserIPC.SendMessage = {
  methodName: "SendMessage",
  service: BrowserIPC,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.StringRequest,
  responseType: webwindow_pb.EmptyRequest
};

BrowserIPC.GetHeight = {
  methodName: "GetHeight",
  service: BrowserIPC,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
  responseType: webwindow_pb.IntMessageResponse
};

exports.BrowserIPC = BrowserIPC;

function BrowserIPCClient(serviceHost, options) {
  this.serviceHost = serviceHost;
  this.options = options || {};
}

BrowserIPCClient.prototype.receiveMessage = function receiveMessage(requestMessage, metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.invoke(BrowserIPC.ReceiveMessage, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onMessage: function (responseMessage) {
      listeners.data.forEach(function (handler) {
        handler(responseMessage);
      });
    },
    onEnd: function (status, statusMessage, trailers) {
      listeners.status.forEach(function (handler) {
        handler({ code: status, details: statusMessage, metadata: trailers });
      });
      listeners.end.forEach(function (handler) {
        handler({ code: status, details: statusMessage, metadata: trailers });
      });
      listeners = null;
    }
  });
  return {
    on: function (type, handler) {
      listeners[type].push(handler);
      return this;
    },
    cancel: function () {
      listeners = null;
      client.close();
    }
  };
};

BrowserIPCClient.prototype.sendMessage = function sendMessage(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(BrowserIPC.SendMessage, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

BrowserIPCClient.prototype.getHeight = function getHeight(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(BrowserIPC.GetHeight, {
    request: requestMessage,
    host: this.serviceHost,
    metadata: metadata,
    transport: this.options.transport,
    debug: this.options.debug,
    onEnd: function (response) {
      if (callback) {
        if (response.status !== grpc.Code.OK) {
          var err = new Error(response.statusMessage);
          err.code = response.status;
          err.metadata = response.trailers;
          callback(err, null);
        } else {
          callback(null, response.message);
        }
      }
    }
  });
  return {
    cancel: function () {
      callback = null;
      client.close();
    }
  };
};

exports.BrowserIPCClient = BrowserIPCClient;

