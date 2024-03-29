// package: webview
// file: webview.proto

import * as webview_pb from "./webview_pb";
import * as google_protobuf_empty_pb from "google-protobuf/google/protobuf/empty_pb";
import {grpc} from "@improbable-eng/grpc-web";

type WebViewIPCSendMessage = {
  readonly methodName: string;
  readonly service: typeof WebViewIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.SendMessageRequest;
  readonly responseType: typeof webview_pb.SendMessageResponse;
};

type WebViewIPCShutdown = {
  readonly methodName: string;
  readonly service: typeof WebViewIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.IdMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type WebViewIPCCreateWebView = {
  readonly methodName: string;
  readonly service: typeof WebViewIPC;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.CreateWebViewRequest;
  readonly responseType: typeof webview_pb.WebMessageResponse;
};

type WebViewIPCFileReader = {
  readonly methodName: string;
  readonly service: typeof WebViewIPC;
  readonly requestStream: true;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.FileReadRequest;
  readonly responseType: typeof webview_pb.FileReadResponse;
};

type WebViewIPCGetIds = {
  readonly methodName: string;
  readonly service: typeof WebViewIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof google_protobuf_empty_pb.Empty;
  readonly responseType: typeof webview_pb.IdArrayResponse;
};

type WebViewIPCPing = {
  readonly methodName: string;
  readonly service: typeof WebViewIPC;
  readonly requestStream: true;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.PingMessageRequest;
  readonly responseType: typeof webview_pb.PingMessageResponse;
};

export class WebViewIPC {
  static readonly serviceName: string;
  static readonly SendMessage: WebViewIPCSendMessage;
  static readonly Shutdown: WebViewIPCShutdown;
  static readonly CreateWebView: WebViewIPCCreateWebView;
  static readonly FileReader: WebViewIPCFileReader;
  static readonly GetIds: WebViewIPCGetIds;
  static readonly Ping: WebViewIPCPing;
}

type BrowserIPCReceiveMessage = {
  readonly methodName: string;
  readonly service: typeof BrowserIPC;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.IdMessageRequest;
  readonly responseType: typeof webview_pb.StringRequest;
};

type BrowserIPCSendMessage = {
  readonly methodName: string;
  readonly service: typeof BrowserIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.SendSequenceMessageRequest;
  readonly responseType: typeof webview_pb.SendMessageResponse;
};

type BrowserIPCGetClientId = {
  readonly methodName: string;
  readonly service: typeof BrowserIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.IdMessageRequest;
  readonly responseType: typeof webview_pb.ClientIdMessageRequest;
};

export class BrowserIPC {
  static readonly serviceName: string;
  static readonly ReceiveMessage: BrowserIPCReceiveMessage;
  static readonly SendMessage: BrowserIPCSendMessage;
  static readonly GetClientId: BrowserIPCGetClientId;
}

type ClientIPCGetClients = {
  readonly methodName: string;
  readonly service: typeof ClientIPC;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.UserMessageRequest;
  readonly responseType: typeof webview_pb.ClientResponseList;
};

type ClientIPCGetUserGroups = {
  readonly methodName: string;
  readonly service: typeof ClientIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.UserRequest;
  readonly responseType: typeof webview_pb.UserResponse;
};

type ClientIPCGetServerStatus = {
  readonly methodName: string;
  readonly service: typeof ClientIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof google_protobuf_empty_pb.Empty;
  readonly responseType: typeof webview_pb.ServerResponse;
};

type ClientIPCGetLoggedEvents = {
  readonly methodName: string;
  readonly service: typeof ClientIPC;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof google_protobuf_empty_pb.Empty;
  readonly responseType: typeof webview_pb.LoggedEventResponse;
};

export class ClientIPC {
  static readonly serviceName: string;
  static readonly GetClients: ClientIPCGetClients;
  static readonly GetUserGroups: ClientIPCGetUserGroups;
  static readonly GetServerStatus: ClientIPCGetServerStatus;
  static readonly GetLoggedEvents: ClientIPCGetLoggedEvents;
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

export class WebViewIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  sendMessage(
    requestMessage: webview_pb.SendMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.SendMessageResponse|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webview_pb.SendMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webview_pb.SendMessageResponse|null) => void
  ): UnaryResponse;
  shutdown(
    requestMessage: webview_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  shutdown(
    requestMessage: webview_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  createWebView(requestMessage: webview_pb.CreateWebViewRequest, metadata?: grpc.Metadata): ResponseStream<webview_pb.WebMessageResponse>;
  fileReader(metadata?: grpc.Metadata): BidirectionalStream<webview_pb.FileReadRequest, webview_pb.FileReadResponse>;
  getIds(
    requestMessage: google_protobuf_empty_pb.Empty,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.IdArrayResponse|null) => void
  ): UnaryResponse;
  getIds(
    requestMessage: google_protobuf_empty_pb.Empty,
    callback: (error: ServiceError|null, responseMessage: webview_pb.IdArrayResponse|null) => void
  ): UnaryResponse;
  ping(metadata?: grpc.Metadata): BidirectionalStream<webview_pb.PingMessageRequest, webview_pb.PingMessageResponse>;
}

export class BrowserIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  receiveMessage(requestMessage: webview_pb.IdMessageRequest, metadata?: grpc.Metadata): ResponseStream<webview_pb.StringRequest>;
  sendMessage(
    requestMessage: webview_pb.SendSequenceMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.SendMessageResponse|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webview_pb.SendSequenceMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webview_pb.SendMessageResponse|null) => void
  ): UnaryResponse;
  getClientId(
    requestMessage: webview_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.ClientIdMessageRequest|null) => void
  ): UnaryResponse;
  getClientId(
    requestMessage: webview_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: webview_pb.ClientIdMessageRequest|null) => void
  ): UnaryResponse;
}

export class ClientIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  getClients(requestMessage: webview_pb.UserMessageRequest, metadata?: grpc.Metadata): ResponseStream<webview_pb.ClientResponseList>;
  getUserGroups(
    requestMessage: webview_pb.UserRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.UserResponse|null) => void
  ): UnaryResponse;
  getUserGroups(
    requestMessage: webview_pb.UserRequest,
    callback: (error: ServiceError|null, responseMessage: webview_pb.UserResponse|null) => void
  ): UnaryResponse;
  getServerStatus(
    requestMessage: google_protobuf_empty_pb.Empty,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.ServerResponse|null) => void
  ): UnaryResponse;
  getServerStatus(
    requestMessage: google_protobuf_empty_pb.Empty,
    callback: (error: ServiceError|null, responseMessage: webview_pb.ServerResponse|null) => void
  ): UnaryResponse;
  getLoggedEvents(
    requestMessage: google_protobuf_empty_pb.Empty,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webview_pb.LoggedEventResponse|null) => void
  ): UnaryResponse;
  getLoggedEvents(
    requestMessage: google_protobuf_empty_pb.Empty,
    callback: (error: ServiceError|null, responseMessage: webview_pb.LoggedEventResponse|null) => void
  ): UnaryResponse;
}

