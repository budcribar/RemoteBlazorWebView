// package: webview
// file: webview.proto

var webview_pb = require("./webview_pb");
var google_protobuf_empty_pb = require("google-protobuf/google/protobuf/empty_pb");
var grpc = require("@improbable-eng/grpc-web").grpc;

var WebViewIPC = (function () {
  function WebViewIPC() {}
  WebViewIPC.serviceName = "webview.WebViewIPC";
  return WebViewIPC;
}());

WebViewIPC.SendMessage = {
  methodName: "SendMessage",
  service: WebViewIPC,
  requestStream: false,
  responseStream: false,
  requestType: webview_pb.SendMessageRequest,
  responseType: webview_pb.SendMessageResponse
};

WebViewIPC.Shutdown = {
  methodName: "Shutdown",
  service: WebViewIPC,
  requestStream: false,
  responseStream: false,
  requestType: webview_pb.IdMessageRequest,
  responseType: google_protobuf_empty_pb.Empty
};

WebViewIPC.CreateWebView = {
  methodName: "CreateWebView",
  service: WebViewIPC,
  requestStream: false,
  responseStream: true,
  requestType: webview_pb.CreateWebViewRequest,
  responseType: webview_pb.WebMessageResponse
};

WebViewIPC.FileReader = {
  methodName: "FileReader",
  service: WebViewIPC,
  requestStream: true,
  responseStream: true,
  requestType: webview_pb.FileReadRequest,
  responseType: webview_pb.FileReadResponse
};

WebViewIPC.Ping = {
  methodName: "Ping",
  service: WebViewIPC,
  requestStream: true,
  responseStream: true,
  requestType: webview_pb.PingMessageRequest,
  responseType: webview_pb.PingMessageResponse
};

WebViewIPC.GetIds = {
  methodName: "GetIds",
  service: WebViewIPC,
  requestStream: false,
  responseStream: false,
  requestType: google_protobuf_empty_pb.Empty,
  responseType: webview_pb.IdArrayResponse
};

exports.WebViewIPC = WebViewIPC;

function WebViewIPCClient(serviceHost, options) {
  this.serviceHost = serviceHost;
  this.options = options || {};
}

WebViewIPCClient.prototype.sendMessage = function sendMessage(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(WebViewIPC.SendMessage, {
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

WebViewIPCClient.prototype.shutdown = function shutdown(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(WebViewIPC.Shutdown, {
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

WebViewIPCClient.prototype.createWebView = function createWebView(requestMessage, metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.invoke(WebViewIPC.CreateWebView, {
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

WebViewIPCClient.prototype.fileReader = function fileReader(metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.client(WebViewIPC.FileReader, {
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

WebViewIPCClient.prototype.ping = function ping(metadata) {
  var listeners = {
    data: [],
    end: [],
    status: []
  };
  var client = grpc.client(WebViewIPC.Ping, {
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

WebViewIPCClient.prototype.getIds = function getIds(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(WebViewIPC.GetIds, {
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

exports.WebViewIPCClient = WebViewIPCClient;

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
  responseType: webview_pb.SendMessageResponse
};

BrowserIPC.Ping = {
  methodName: "Ping",
  service: BrowserIPC,
  requestStream: false,
  responseStream: false,
  requestType: webview_pb.PingMessageRequest,
  responseType: webview_pb.PingMessageResponse
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

BrowserIPCClient.prototype.ping = function ping(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(BrowserIPC.Ping, {
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

ClientIPC.GetServerStatus = {
  methodName: "GetServerStatus",
  service: ClientIPC,
  requestStream: false,
  responseStream: false,
  requestType: google_protobuf_empty_pb.Empty,
  responseType: webview_pb.ServerResponse
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

ClientIPCClient.prototype.getServerStatus = function getServerStatus(requestMessage, metadata, callback) {
  if (arguments.length === 2) {
    callback = arguments[1];
  }
  var client = grpc.unary(ClientIPC.GetServerStatus, {
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

exports.ClientIPCClient = ClientIPCClient;

