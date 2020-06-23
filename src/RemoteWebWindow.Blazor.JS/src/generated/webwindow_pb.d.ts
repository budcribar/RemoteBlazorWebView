// package: webwindow
// file: webwindow.proto

import * as jspb from "google-protobuf";
import * as google_protobuf_empty_pb from "google-protobuf/google/protobuf/empty_pb";

export class SendMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getMessage(): string;
  setMessage(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): SendMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: SendMessageRequest): SendMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: SendMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): SendMessageRequest;
  static deserializeBinaryFromReader(message: SendMessageRequest, reader: jspb.BinaryReader): SendMessageRequest;
}

export namespace SendMessageRequest {
  export type AsObject = {
    id: string,
    message: string,
  }
}

export class ShowMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getTitle(): string;
  setTitle(value: string): void;

  getBody(): string;
  setBody(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): ShowMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: ShowMessageRequest): ShowMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: ShowMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): ShowMessageRequest;
  static deserializeBinaryFromReader(message: ShowMessageRequest, reader: jspb.BinaryReader): ShowMessageRequest;
}

export namespace ShowMessageRequest {
  export type AsObject = {
    id: string,
    title: string,
    body: string,
  }
}

export class FileMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getPath(): string;
  setPath(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): FileMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: FileMessageRequest): FileMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: FileMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): FileMessageRequest;
  static deserializeBinaryFromReader(message: FileMessageRequest, reader: jspb.BinaryReader): FileMessageRequest;
}

export namespace FileMessageRequest {
  export type AsObject = {
    id: string,
    path: string,
  }
}

export class IdMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): IdMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: IdMessageRequest): IdMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: IdMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): IdMessageRequest;
  static deserializeBinaryFromReader(message: IdMessageRequest, reader: jspb.BinaryReader): IdMessageRequest;
}

export namespace IdMessageRequest {
  export type AsObject = {
    id: string,
  }
}

export class CreateWebWindowRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getHtmlhostpath(): string;
  setHtmlhostpath(value: string): void;

  getHostname(): string;
  setHostname(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): CreateWebWindowRequest.AsObject;
  static toObject(includeInstance: boolean, msg: CreateWebWindowRequest): CreateWebWindowRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: CreateWebWindowRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): CreateWebWindowRequest;
  static deserializeBinaryFromReader(message: CreateWebWindowRequest, reader: jspb.BinaryReader): CreateWebWindowRequest;
}

export namespace CreateWebWindowRequest {
  export type AsObject = {
    id: string,
    htmlhostpath: string,
    hostname: string,
  }
}

export class WebMessageResponse extends jspb.Message {
  getResponse(): string;
  setResponse(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): WebMessageResponse.AsObject;
  static toObject(includeInstance: boolean, msg: WebMessageResponse): WebMessageResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: WebMessageResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): WebMessageResponse;
  static deserializeBinaryFromReader(message: WebMessageResponse, reader: jspb.BinaryReader): WebMessageResponse;
}

export namespace WebMessageResponse {
  export type AsObject = {
    response: string,
  }
}

export class FileReadRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getPath(): string;
  setPath(value: string): void;

  getData(): Uint8Array | string;
  getData_asU8(): Uint8Array;
  getData_asB64(): string;
  setData(value: Uint8Array | string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): FileReadRequest.AsObject;
  static toObject(includeInstance: boolean, msg: FileReadRequest): FileReadRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: FileReadRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): FileReadRequest;
  static deserializeBinaryFromReader(message: FileReadRequest, reader: jspb.BinaryReader): FileReadRequest;
}

export namespace FileReadRequest {
  export type AsObject = {
    id: string,
    path: string,
    data: Uint8Array | string,
  }
}

export class FileReadResponse extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getPath(): string;
  setPath(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): FileReadResponse.AsObject;
  static toObject(includeInstance: boolean, msg: FileReadResponse): FileReadResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: FileReadResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): FileReadResponse;
  static deserializeBinaryFromReader(message: FileReadResponse, reader: jspb.BinaryReader): FileReadResponse;
}

export namespace FileReadResponse {
  export type AsObject = {
    id: string,
    path: string,
  }
}

export class StringRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getRequest(): string;
  setRequest(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): StringRequest.AsObject;
  static toObject(includeInstance: boolean, msg: StringRequest): StringRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: StringRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): StringRequest;
  static deserializeBinaryFromReader(message: StringRequest, reader: jspb.BinaryReader): StringRequest;
}

export namespace StringRequest {
  export type AsObject = {
    id: string,
    request: string,
  }
}

