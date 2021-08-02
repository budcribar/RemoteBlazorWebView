// package: webview
// file: webview.proto

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

export class CreateWebViewRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getHtmlhostpath(): string;
  setHtmlhostpath(value: string): void;

  getHostname(): string;
  setHostname(value: string): void;

  getGroup(): string;
  setGroup(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): CreateWebViewRequest.AsObject;
  static toObject(includeInstance: boolean, msg: CreateWebViewRequest): CreateWebViewRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: CreateWebViewRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): CreateWebViewRequest;
  static deserializeBinaryFromReader(message: CreateWebViewRequest, reader: jspb.BinaryReader): CreateWebViewRequest;
}

export namespace CreateWebViewRequest {
  export type AsObject = {
    id: string,
    htmlhostpath: string,
    hostname: string,
    group: string,
  }
}

export class WebMessageResponse extends jspb.Message {
  getResponse(): string;
  setResponse(value: string): void;

  getUrl(): string;
  setUrl(value: string): void;

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
    url: string,
  }
}

export class IdArrayResponse extends jspb.Message {
  clearResponsesList(): void;
  getResponsesList(): Array<string>;
  setResponsesList(value: Array<string>): void;
  addResponses(value: string, index?: number): string;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): IdArrayResponse.AsObject;
  static toObject(includeInstance: boolean, msg: IdArrayResponse): IdArrayResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: IdArrayResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): IdArrayResponse;
  static deserializeBinaryFromReader(message: IdArrayResponse, reader: jspb.BinaryReader): IdArrayResponse;
}

export namespace IdArrayResponse {
  export type AsObject = {
    responsesList: Array<string>,
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

export class SendSequenceMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getSequence(): number;
  setSequence(value: number): void;

  getMessage(): string;
  setMessage(value: string): void;

  getUrl(): string;
  setUrl(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): SendSequenceMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: SendSequenceMessageRequest): SendSequenceMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: SendSequenceMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): SendSequenceMessageRequest;
  static deserializeBinaryFromReader(message: SendSequenceMessageRequest, reader: jspb.BinaryReader): SendSequenceMessageRequest;
}

export namespace SendSequenceMessageRequest {
  export type AsObject = {
    id: string,
    sequence: number,
    message: string,
    url: string,
  }
}

export class ClientResponse extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getHostname(): string;
  setHostname(value: string): void;

  getUrl(): string;
  setUrl(value: string): void;

  getState(): ClientStateMap[keyof ClientStateMap];
  setState(value: ClientStateMap[keyof ClientStateMap]): void;

  getGroup(): string;
  setGroup(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): ClientResponse.AsObject;
  static toObject(includeInstance: boolean, msg: ClientResponse): ClientResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: ClientResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): ClientResponse;
  static deserializeBinaryFromReader(message: ClientResponse, reader: jspb.BinaryReader): ClientResponse;
}

export namespace ClientResponse {
  export type AsObject = {
    id: string,
    hostname: string,
    url: string,
    state: ClientStateMap[keyof ClientStateMap],
    group: string,
  }
}

export class ClientResponseList extends jspb.Message {
  clearClientresponsesList(): void;
  getClientresponsesList(): Array<ClientResponse>;
  setClientresponsesList(value: Array<ClientResponse>): void;
  addClientresponses(value?: ClientResponse, index?: number): ClientResponse;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): ClientResponseList.AsObject;
  static toObject(includeInstance: boolean, msg: ClientResponseList): ClientResponseList.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: ClientResponseList, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): ClientResponseList;
  static deserializeBinaryFromReader(message: ClientResponseList, reader: jspb.BinaryReader): ClientResponseList;
}

export namespace ClientResponseList {
  export type AsObject = {
    clientresponsesList: Array<ClientResponse.AsObject>,
  }
}

export class UserMessageRequest extends jspb.Message {
  getOid(): string;
  setOid(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): UserMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: UserMessageRequest): UserMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: UserMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): UserMessageRequest;
  static deserializeBinaryFromReader(message: UserMessageRequest, reader: jspb.BinaryReader): UserMessageRequest;
}

export namespace UserMessageRequest {
  export type AsObject = {
    oid: string,
  }
}

export interface ClientStateMap {
  CONNECTED: 0;
  SHUTTINGDOWN: 1;
}

export const ClientState: ClientStateMap;

