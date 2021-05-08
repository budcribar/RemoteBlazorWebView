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

RemoteWebWindow.SendMessage = {
  methodName: "SendMessage",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.SendMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebWindow.Shutdown = {
  methodName: "Shutdown",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: webwindow_pb.IdMessageRequest,
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

RemoteWebWindow.GetIds = {
  methodName: "GetIds",
  service: RemoteWebWindow,
  requestStream: false,
  responseStream: false,
  requestType: google_protobuf_empty_pb.Empty,
  responseType: webwindow_pb.IdArrayResponse
};

exports.RemoteWebWindow = RemoteWebWindow;

function RemoteWebWindowClient(serviceHost, options) {
  this.serviceHost = serviceHost;
  this.options = options || {};
}

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

RemoteWebWindowClient.prototype.shutdown = function shutdown(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.Shutdown, {
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

RemoteWebWindowClient.prototype.getIds = function getIds(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebWindow.GetIds, {
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
  requestType: webwindow_pb.SendSequenceMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
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

exports.BrowserIPCClient = BrowserIPCClient;

