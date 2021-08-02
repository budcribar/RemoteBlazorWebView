// package: webview
// file: webview.proto

var webview_pb = require("./webview_pb");
var google_protobuf_empty_pb = require("google-protobuf/google/protobuf/empty_pb");
var grpc = require("@improbable-eng/grpc-web").grpc;

var RemoteWebView = (function () {
  function RemoteWebView() {}
  RemoteWebView.serviceName = "webview.RemoteWebView";
  return RemoteWebView;
}());

RemoteWebView.SendMessage = {
  methodName: "SendMessage",
  service: RemoteWebView,
  requestStream: false,
  responseStream: false,
  requestType: webview_pb.SendMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebView.Shutdown = {
  methodName: "Shutdown",
  service: RemoteWebView,
  requestStream: false,
  responseStream: false,
  requestType: webview_pb.IdMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

RemoteWebView.CreateWebView = {
  methodName: "CreateWebView",
  service: RemoteWebView,
  requestStream: false,
  responseStream: true,
  requestType: webview_pb.CreateWebViewRequest,
  responseType: webview_pb.WebMessageResponse
};

RemoteWebView.FileReader = {
  methodName: "FileReader",
  service: RemoteWebView,
  requestStream: true,
  responseStream: true,
  requestType: webview_pb.FileReadRequest,
  responseType: webview_pb.FileReadResponse
};

RemoteWebView.GetIds = {
  methodName: "GetIds",
  service: RemoteWebView,
  requestStream: false,
  responseStream: false,
  requestType: google_protobuf_empty_pb.Empty,
  responseType: webview_pb.IdArrayResponse
};

exports.RemoteWebView = RemoteWebView;

function RemoteWebViewClient(serviceHost, options) {
  this.serviceHost = serviceHost;
  this.options = options || {};
}

RemoteWebViewClient.prototype.sendMessage = function sendMessage(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebView.SendMessage, {
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

RemoteWebViewClient.prototype.shutdown = function shutdown(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebView.Shutdown, {
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

RemoteWebViewClient.prototype.createWebView = function createWebView(requestMessage, metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.invoke(RemoteWebView.CreateWebView, {
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

RemoteWebViewClient.prototype.fileReader = function fileReader(metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.client(RemoteWebView.FileReader, {
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

RemoteWebViewClient.prototype.getIds = function getIds(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(RemoteWebView.GetIds, {
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

exports.RemoteWebViewClient = RemoteWebViewClient;

var BrowserIPC = (function () {
  function BrowserIPC() {}
  BrowserIPC.serviceName = "webview.BrowserIPC";
  return BrowserIPC;
}());

BrowserIPC.ReceiveMessage = {
  methodName: "ReceiveMessage",
  service: BrowserIPC,
  requestStream: false,
  responseStream: true,
  requestType: webview_pb.IdMessageRequest,
  responseType: webview_pb.StringRequest
};

BrowserIPC.SendMessage = {
  methodName: "SendMessage",
  service: BrowserIPC,
  requestStream: false,
  responseStream: false,
  requestType: webview_pb.SendSequenceMessageRequest,
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

var ClientIPC = (function () {
  function ClientIPC() {}
  ClientIPC.serviceName = "webview.ClientIPC";
  return ClientIPC;
}());

ClientIPC.GetClients = {
  methodName: "GetClients",
  service: ClientIPC,
  requestStream: false,
  responseStream: true,
  requestType: webview_pb.UserMessageRequest,
  responseType: webview_pb.ClientResponseList
};

exports.ClientIPC = ClientIPC;

function ClientIPCClient(serviceHost, options) {
  this.serviceHost = serviceHost;
  this.options = options || {};
}

ClientIPCClient.prototype.getClients = function getClients(requestMessage, metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.invoke(ClientIPC.GetClients, {
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

exports.ClientIPCClient = ClientIPCClient;

