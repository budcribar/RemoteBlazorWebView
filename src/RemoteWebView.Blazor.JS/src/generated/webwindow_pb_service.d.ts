// package: webwindow
// file: webwindow.proto

import * as webwindow_pb from "./webwindow_pb";
import * as google_protobuf_empty_pb from "google-protobuf/google/protobuf/empty_pb";
import {grpc} from "@improbable-eng/grpc-web";

type RemoteWebWindowSendMessage = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.SendMessageRequest;
  readonly responseType: typeof google_protobuf_empty_pb.Empty;
};

type RemoteWebWindowShutdown = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof webwindow_pb.IdMessageRequest;
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

type RemoteWebWindowGetIds = {
  readonly methodName: string;
  readonly service: typeof RemoteWebWindow;
  readonly requestStream: false;
  readonly responseStream: false;
  readonly requestType: typeof google_protobuf_empty_pb.Empty;
  readonly responseType: typeof webwindow_pb.IdArrayResponse;
};

export class RemoteWebWindow {
  static readonly serviceName: string;
  static readonly SendMessage: RemoteWebWindowSendMessage;
  static readonly Shutdown: RemoteWebWindowShutdown;
  static readonly CreateWebWindow: RemoteWebWindowCreateWebWindow;
  static readonly FileReader: RemoteWebWindowFileReader;
  static readonly GetIds: RemoteWebWindowGetIds;
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
  readonly requestType: typeof webwindow_pb.SendSequenceMessageRequest;
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
  readonly requestType: typeof webwindow_pb.UserMessageRequest;
  readonly responseType: typeof webwindow_pb.ClientResponseList;
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

export class RemoteWebWindowClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  sendMessage(
    requestMessage: webwindow_pb.SendMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webwindow_pb.SendMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  shutdown(
    requestMessage: webwindow_pb.IdMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  shutdown(
    requestMessage: webwindow_pb.IdMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  createWebWindow(requestMessage: webwindow_pb.CreateWebWindowRequest, metadata?: grpc.Metadata): ResponseStream<webwindow_pb.WebMessageResponse>;
  fileReader(metadata?: grpc.Metadata): BidirectionalStream<webwindow_pb.FileReadRequest, webwindow_pb.FileReadResponse>;
  getIds(
    requestMessage: google_protobuf_empty_pb.Empty,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IdArrayResponse|null) => void
  ): UnaryResponse;
  getIds(
    requestMessage: google_protobuf_empty_pb.Empty,
    callback: (error: ServiceError|null, responseMessage: webwindow_pb.IdArrayResponse|null) => void
  ): UnaryResponse;
}

export class BrowserIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  receiveMessage(requestMessage: webwindow_pb.IdMessageRequest, metadata?: grpc.Metadata): ResponseStream<webwindow_pb.StringRequest>;
  sendMessage(
    requestMessage: webwindow_pb.SendSequenceMessageRequest,
    metadata: grpc.Metadata,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
  sendMessage(
    requestMessage: webwindow_pb.SendSequenceMessageRequest,
    callback: (error: ServiceError|null, responseMessage: google_protobuf_empty_pb.Empty|null) => void
  ): UnaryResponse;
}

export class ClientIPCClient {
  readonly serviceHost: string;

  constructor(serviceHost: string, options?: grpc.RpcOptions);
  getClients(requestMessage: webwindow_pb.UserMessageRequest, metadata?: grpc.Metadata): ResponseStream<webwindow_pb.ClientResponseList>;
}

