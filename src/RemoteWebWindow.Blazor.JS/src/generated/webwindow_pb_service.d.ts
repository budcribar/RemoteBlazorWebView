// package: webwindow
// file: webwindow.proto

import * as webwindow_pb from "./webwindow_pb";
import * as google_protobuf_empty_pb from "google-protobuf/google/protobuf/empty_pb";
import {grpc} from "@improbable-eng/grpc-web";

type RemoteWebWindowGetHeight = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.IntMessageResponse;
};

type RemoteWebWindowSetHeight = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IntMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetLeft = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.IntMessageResponse;
};

type RemoteWebWindowSetLeft = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IntMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetLocation = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.PointMessageResponse;
};

type RemoteWebWindowSetLocation = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.PointMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetMonitors = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.MonitorResponse;
};

type RemoteWebWindowGetResizable = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.BoolResponse;
};

type RemoteWebWindowSetResizable = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.BoolRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetScreenDpi = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.UInt32Response;
};

type RemoteWebWindowSendMessage = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.SendMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowSetIconFile = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.SendMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowShow = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowShowMessage = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.ShowMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetSize = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.SizeMessageResponse;
};

type RemoteWebWindowSetSize = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.SizeMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetTitle = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.StringResponse;
};

type RemoteWebWindowSetTitle = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.StringRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetTop = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.IntMessageResponse;
};

type RemoteWebWindowSetTop = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IntMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowNavigateToLocalFile = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.FileMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowNavigateToString = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.StringRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowNavigateToUrl = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.UrlMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowWaitForExit = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowGetWidth = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.IntMessageResponse;
};

type RemoteWebWindowSetWidth = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IntMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowCreateWebWindow = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webwindow_pb.CreateWebWindowRequest;
  readonly responseType: typeof webwindow_pb.WebMessageResponse;
};

type RemoteWebWindowFileReader = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: true;
  readonly responseStream: true;
  readonly requestType: typeof webwindow_pb.FileReadRequest;
  readonly responseType: typeof webwindow_pb.FileReadResponse;
};

export class RemoteWebWindow {
  static readonly serviceName: string;
  static readonly GetHeight: RemoteWebWindowGetHeight;
  static readonly SetHeight: RemoteWebWindowSetHeight;
  static readonly GetLeft: RemoteWebWindowGetLeft;
  static readonly SetLeft: RemoteWebWindowSetLeft;
  static readonly GetLocation: RemoteWebWindowGetLocation;
  static readonly SetLocation: RemoteWebWindowSetLocation;
  static readonly GetMonitors: RemoteWebWindowGetMonitors;
  static readonly GetResizable: RemoteWebWindowGetResizable;
  static readonly SetResizable: RemoteWebWindowSetResizable;
  static readonly GetScreenDpi: RemoteWebWindowGetScreenDpi;
  static readonly SendMessage: RemoteWebWindowSendMessage;
  static readonly SetIconFile: RemoteWebWindowSetIconFile;
  static readonly Show: RemoteWebWindowShow;
  static readonly ShowMessage: RemoteWebWindowShowMessage;
  static readonly GetSize: RemoteWebWindowGetSize;
  static readonly SetSize: RemoteWebWindowSetSize;
  static readonly GetTitle: RemoteWebWindowGetTitle;
  static readonly SetTitle: RemoteWebWindowSetTitle;
  static readonly GetTop: RemoteWebWindowGetTop;
  static readonly SetTop: RemoteWebWindowSetTop;
  static readonly NavigateToLocalFile: RemoteWebWindowNavigateToLocalFile;
  static readonly NavigateToString: RemoteWebWindowNavigateToString;
  static readonly NavigateToUrl: RemoteWebWindowNavigateToUrl;
  static readonly WaitForExit: RemoteWebWindowWaitForExit;
  static readonly GetWidth: RemoteWebWindowGetWidth;
  static readonly SetWidth: RemoteWebWindowSetWidth;
  static readonly CreateWebWindow: RemoteWebWindowCreateWebWindow;
  static readonly FileReader: RemoteWebWindowFileReader;
}

type BrowserIPCReceiveMessage = {
  readonly methodName: string;
  readonly service: typeof BrowserIPC;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.StringRequest;
};

type BrowserIPCSendMessage = {
  readonly methodName: string;
  readonly service: typeof BrowserIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.StringRequest;
  readonly responseType: typeof webwindow_pb.EmptyRequest;
};

type BrowserIPCGetHeight = {
  readonly methodName: string;
  readonly service: typeof BrowserIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
  readonly responseType: typeof webwindow_pb.IntMessageResponse;
};

export class BrowserIPC {
  static readonly serviceName: string;
  static readonly ReceiveMessage: BrowserIPCReceiveMessage;
  static readonly SendMessage: BrowserIPCSendMessage;
  static readonly GetHeight: BrowserIPCGetHeight;
}

export type ServiceError = { message: string, code: number; metadata: grpc.Metadata }
export type Status = { details: string, code: number; metadata: grpc.Metadata }

interface UnaryResponse {
  cancel(): void;
}
interface ResponseStream<T> {
  cancel(): void;
  on(type: 'data', handler: (message: T) => void): ResponseStream<T>;
  on(type: 'end', handler: (status?: Status) => void): ResponseStream<T>;
  on(type: 'status', handler: (status: Status) => void): ResponseStream<T>;
}
interface RequestStream<T> {
  write(message: T): RequestStream<T>;
  end(): void;
  cancel(): void;
  on(type: 'end', handler: (status?: Status) => void): RequestStream<T>;
  on(type: 'status', handler: (status: Status) => void): RequestStream<T>;
}
interface BidirectionalStream<ReqT, ResT> {
  write(message: ReqT): BidirectionalStream<ReqT, ResT>;
  end(): void;
  cancel(): void;
  on(type: 'data', handler: (message: ResT) => void): BidirectionalStream<ReqT, ResT>;
  on(type: 'end', handler: (status?: Status) => void): BidirectionalStream<ReqT, ResT>;
  on(type: 'status', handler: (status: Status) => void): BidirectionalStream<ReqT, ResT>;
}

export class RemoteWebWindowClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  getHeight(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  getHeight(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  setHeight(
    requestMessage: webwindow_pb.IntMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setHeight(
    requestMessage: webwindow_pb.IntMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getLeft(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  getLeft(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  setLeft(
    requestMessage: webwindow_pb.IntMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setLeft(
    requestMessage: webwindow_pb.IntMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getLocation(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.PointMessageResponse|null) => void
  ): UnaryResponse;
  getLocation(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.PointMessageResponse|null) => void
  ): UnaryResponse;
  setLocation(
    requestMessage: webwindow_pb.PointMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setLocation(
    requestMessage: webwindow_pb.PointMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getMonitors(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.MonitorResponse|null) => void
  ): UnaryResponse;
  getMonitors(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.MonitorResponse|null) => void
  ): UnaryResponse;
  getResizable(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.BoolResponse|null) => void
  ): UnaryResponse;
  getResizable(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.BoolResponse|null) => void
  ): UnaryResponse;
  setResizable(
    requestMessage: webwindow_pb.BoolRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setResizable(
    requestMessage: webwindow_pb.BoolRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getScreenDpi(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.UInt32Response|null) => void
  ): UnaryResponse;
  getScreenDpi(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.UInt32Response|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webwindow_pb.SendMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webwindow_pb.SendMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setIconFile(
    requestMessage: webwindow_pb.SendMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setIconFile(
    requestMessage: webwindow_pb.SendMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  show(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  show(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  showMessage(
    requestMessage: webwindow_pb.ShowMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  showMessage(
    requestMessage: webwindow_pb.ShowMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getSize(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.SizeMessageResponse|null) => void
  ): UnaryResponse;
  getSize(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.SizeMessageResponse|null) => void
  ): UnaryResponse;
  setSize(
    requestMessage: webwindow_pb.SizeMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setSize(
    requestMessage: webwindow_pb.SizeMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getTitle(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.StringResponse|null) => void
  ): UnaryResponse;
  getTitle(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.StringResponse|null) => void
  ): UnaryResponse;
  setTitle(
    requestMessage: webwindow_pb.StringRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setTitle(
    requestMessage: webwindow_pb.StringRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getTop(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  getTop(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  setTop(
    requestMessage: webwindow_pb.IntMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setTop(
    requestMessage: webwindow_pb.IntMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  navigateToLocalFile(
    requestMessage: webwindow_pb.FileMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  navigateToLocalFile(
    requestMessage: webwindow_pb.FileMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  navigateToString(
    requestMessage: webwindow_pb.StringRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  navigateToString(
    requestMessage: webwindow_pb.StringRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  navigateToUrl(
    requestMessage: webwindow_pb.UrlMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  navigateToUrl(
    requestMessage: webwindow_pb.UrlMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  waitForExit(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  waitForExit(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  getWidth(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  getWidth(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  setWidth(
    requestMessage: webwindow_pb.IntMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  setWidth(
    requestMessage: webwindow_pb.IntMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  createWebWindow(requestMessage: webwindow_pb.CreateWebWindowRequest, metadata?: grpc.Metadata): ResponseStream<webwindow_pb.WebMessageResponse>;
  fileReader(metadata?: grpc.Metadata): BidirectionalStream<webwindow_pb.FileReadRequest, webwindow_pb.FileReadResponse>;
}

export class BrowserIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  receiveMessage(requestMessage: webwindow_pb.IdMessageRequest, metadata?: grpc.Metadata): ResponseStream<webwindow_pb.StringRequest>;
  sendMessage(
    requestMessage: webwindow_pb.StringRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.EmptyRequest|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webwindow_pb.StringRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.EmptyRequest|null) => void
  ): UnaryResponse;
  getHeight(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
  getHeight(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IntMessageResponse|null) => void
  ): UnaryResponse;
}

