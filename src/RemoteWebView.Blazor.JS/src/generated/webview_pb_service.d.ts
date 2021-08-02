// package: webview
// file: webview.proto

import * as webview_pb from "./webview_pb";
import * as google_protobuf_empty_pb from "google-protobuf/google/protobuf/empty_pb";
import {grpc} from "@improbable-eng/grpc-web";

type RemoteWebViewSendMessage = {
  readonly methodName: string;
  readonly service: typeof RemoteWebView;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.SendMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebViewShutdown = {
  readonly methodName: string;
  readonly service: typeof RemoteWebView;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webview_pb.IdMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebViewCreateWebView = {
  readonly methodName: string;
  readonly service: typeof RemoteWebView;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.CreateWebViewRequest;
  readonly responseType: typeof webview_pb.WebMessageResponse;
};

type RemoteWebViewFileReader = {
  readonly methodName: string;
  readonly service: typeof RemoteWebView;
  readonly requestStream: true;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.FileReadRequest;
  readonly responseType: typeof webview_pb.FileReadResponse;
};

type RemoteWebViewGetIds = {
  readonly methodName: string;
  readonly service: typeof RemoteWebView;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof google_protobuf_empty_pb.Empty;
  readonly responseType: typeof webview_pb.IdArrayResponse;
};

export class RemoteWebView {
  static readonly serviceName: string;
  static readonly SendMessage: RemoteWebViewSendMessage;
  static readonly Shutdown: RemoteWebViewShutdown;
  static readonly CreateWebView: RemoteWebViewCreateWebView;
  static readonly FileReader: RemoteWebViewFileReader;
  static readonly GetIds: RemoteWebViewGetIds;
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
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

export class BrowserIPC {
  static readonly serviceName: string;
  static readonly ReceiveMessage: BrowserIPCReceiveMessage;
  static readonly SendMessage: BrowserIPCSendMessage;
}

type ClientIPCGetClients = {
  readonly methodName: string;
  readonly service: typeof ClientIPC;
  readonly requestStream: false;
  readonly responseStream: true;
  readonly requestType: typeof webview_pb.UserMessageRequest;
  readonly responseType: typeof webview_pb.ClientResponseList;
};

export class ClientIPC {
  static readonly serviceName: string;
  static readonly GetClients: ClientIPCGetClients;
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

export class RemoteWebViewClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  sendMessage(
    requestMessage: webview_pb.SendMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webview_pb.SendMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
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
}

export class BrowserIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  receiveMessage(requestMessage: webview_pb.IdMessageRequest, metadata?: grpc.Metadata): ResponseStream<webview_pb.StringRequest>;
  sendMessage(
    requestMessage: webview_pb.SendSequenceMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webview_pb.SendSequenceMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
}

export class ClientIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  getClients(requestMessage: webview_pb.UserMessageRequest, metadata?: grpc.Metadata): ResponseStream<webview_pb.ClientResponseList>;
}

