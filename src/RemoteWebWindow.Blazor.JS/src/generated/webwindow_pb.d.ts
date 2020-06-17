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

export class UrlMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getUrl(): string;
  setUrl(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): UrlMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: UrlMessageRequest): UrlMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: UrlMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): UrlMessageRequest;
  static deserializeBinaryFromReader(message: UrlMessageRequest, reader: jspb.BinaryReader): UrlMessageRequest;
}

export namespace UrlMessageRequest {
  export type AsObject = {
    id: string,
    url: string,
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

  getTitle(): string;
  setTitle(value: string): void;

  getHtmlhostpath(): string;
  setHtmlhostpath(value: string): void;

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
    title: string,
    htmlhostpath: string,
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

export class IntMessageResponse extends jspb.Message {
  getResponse(): number;
  setResponse(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): IntMessageResponse.AsObject;
  static toObject(includeInstance: boolean, msg: IntMessageResponse): IntMessageResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: IntMessageResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): IntMessageResponse;
  static deserializeBinaryFromReader(message: IntMessageResponse, reader: jspb.BinaryReader): IntMessageResponse;
}

export namespace IntMessageResponse {
  export type AsObject = {
    response: number,
  }
}

export class IntMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getMessage(): number;
  setMessage(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): IntMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: IntMessageRequest): IntMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: IntMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): IntMessageRequest;
  static deserializeBinaryFromReader(message: IntMessageRequest, reader: jspb.BinaryReader): IntMessageRequest;
}

export namespace IntMessageRequest {
  export type AsObject = {
    id: string,
    message: number,
  }
}

export class PointMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getX(): number;
  setX(value: number): void;

  getY(): number;
  setY(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): PointMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: PointMessageRequest): PointMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: PointMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): PointMessageRequest;
  static deserializeBinaryFromReader(message: PointMessageRequest, reader: jspb.BinaryReader): PointMessageRequest;
}

export namespace PointMessageRequest {
  export type AsObject = {
    id: string,
    x: number,
    y: number,
  }
}

export class PointMessageResponse extends jspb.Message {
  getX(): number;
  setX(value: number): void;

  getY(): number;
  setY(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): PointMessageResponse.AsObject;
  static toObject(includeInstance: boolean, msg: PointMessageResponse): PointMessageResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: PointMessageResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): PointMessageResponse;
  static deserializeBinaryFromReader(message: PointMessageResponse, reader: jspb.BinaryReader): PointMessageResponse;
}

export namespace PointMessageResponse {
  export type AsObject = {
    x: number,
    y: number,
  }
}

export class SizeMessageResponse extends jspb.Message {
  getHeight(): number;
  setHeight(value: number): void;

  getWidth(): number;
  setWidth(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): SizeMessageResponse.AsObject;
  static toObject(includeInstance: boolean, msg: SizeMessageResponse): SizeMessageResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: SizeMessageResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): SizeMessageResponse;
  static deserializeBinaryFromReader(message: SizeMessageResponse, reader: jspb.BinaryReader): SizeMessageResponse;
}

export namespace SizeMessageResponse {
  export type AsObject = {
    height: number,
    width: number,
  }
}

export class SizeMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getHeight(): number;
  setHeight(value: number): void;

  getWidth(): number;
  setWidth(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): SizeMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: SizeMessageRequest): SizeMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: SizeMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): SizeMessageRequest;
  static deserializeBinaryFromReader(message: SizeMessageRequest, reader: jspb.BinaryReader): SizeMessageRequest;
}

export namespace SizeMessageRequest {
  export type AsObject = {
    id: string,
    height: number,
    width: number,
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

export class RectangleResponse extends jspb.Message {
  getX(): number;
  setX(value: number): void;

  getY(): number;
  setY(value: number): void;

  getWidth(): number;
  setWidth(value: number): void;

  getHeight(): number;
  setHeight(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): RectangleResponse.AsObject;
  static toObject(includeInstance: boolean, msg: RectangleResponse): RectangleResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: RectangleResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): RectangleResponse;
  static deserializeBinaryFromReader(message: RectangleResponse, reader: jspb.BinaryReader): RectangleResponse;
}

export namespace RectangleResponse {
  export type AsObject = {
    x: number,
    y: number,
    width: number,
    height: number,
  }
}

export class BoolResponse extends jspb.Message {
  getResponse(): boolean;
  setResponse(value: boolean): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): BoolResponse.AsObject;
  static toObject(includeInstance: boolean, msg: BoolResponse): BoolResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: BoolResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): BoolResponse;
  static deserializeBinaryFromReader(message: BoolResponse, reader: jspb.BinaryReader): BoolResponse;
}

export namespace BoolResponse {
  export type AsObject = {
    response: boolean,
  }
}

export class StringResponse extends jspb.Message {
  getResponse(): string;
  setResponse(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): StringResponse.AsObject;
  static toObject(includeInstance: boolean, msg: StringResponse): StringResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: StringResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): StringResponse;
  static deserializeBinaryFromReader(message: StringResponse, reader: jspb.BinaryReader): StringResponse;
}

export namespace StringResponse {
  export type AsObject = {
    response: string,
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

export class BoolRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getRequest(): boolean;
  setRequest(value: boolean): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): BoolRequest.AsObject;
  static toObject(includeInstance: boolean, msg: BoolRequest): BoolRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: BoolRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): BoolRequest;
  static deserializeBinaryFromReader(message: BoolRequest, reader: jspb.BinaryReader): BoolRequest;
}

export namespace BoolRequest {
  export type AsObject = {
    id: string,
    request: boolean,
  }
}

export class UInt32Response extends jspb.Message {
  getResponse(): number;
  setResponse(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): UInt32Response.AsObject;
  static toObject(includeInstance: boolean, msg: UInt32Response): UInt32Response.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: UInt32Response, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): UInt32Response;
  static deserializeBinaryFromReader(message: UInt32Response, reader: jspb.BinaryReader): UInt32Response;
}

export namespace UInt32Response {
  export type AsObject = {
    response: number,
  }
}

export class MonitorResponse extends jspb.Message {
  clearInstancesList(): void;
  getInstancesList(): Array<MonitorResponse.Instance>;
  setInstancesList(value: Array<MonitorResponse.Instance>): void;
  addInstances(value?: MonitorResponse.Instance, index?: number): MonitorResponse.Instance;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): MonitorResponse.AsObject;
  static toObject(includeInstance: boolean, msg: MonitorResponse): MonitorResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: MonitorResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): MonitorResponse;
  static deserializeBinaryFromReader(message: MonitorResponse, reader: jspb.BinaryReader): MonitorResponse;
}

export namespace MonitorResponse {
  export type AsObject = {
    instancesList: Array<MonitorResponse.Instance.AsObject>,
  }

  export class Instance extends jspb.Message {
    hasMonitorarea(): boolean;
    clearMonitorarea(): void;
    getMonitorarea(): RectangleResponse | undefined;
    setMonitorarea(value?: RectangleResponse): void;

    hasWorkarea(): boolean;
    clearWorkarea(): void;
    getWorkarea(): RectangleResponse | undefined;
    setWorkarea(value?: RectangleResponse): void;

    serializeBinary(): Uint8Array;
    toObject(includeInstance?: boolean): Instance.AsObject;
    static toObject(includeInstance: boolean, msg: Instance): Instance.AsObject;
    static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
    static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
    static serializeBinaryToWriter(message: Instance, writer: jspb.BinaryWriter): void;
    static deserializeBinary(bytes: Uint8Array): Instance;
    static deserializeBinaryFromReader(message: Instance, reader: jspb.BinaryReader): Instance;
  }

  export namespace Instance {
    export type AsObject = {
      monitorarea?: RectangleResponse.AsObject,
      workarea?: RectangleResponse.AsObject,
    }
  }
}

export class EmptyRequest extends jspb.Message {
  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): EmptyRequest.AsObject;
  static toObject(includeInstance: boolean, msg: EmptyRequest): EmptyRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: EmptyRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): EmptyRequest;
  static deserializeBinaryFromReader(message: EmptyRequest, reader: jspb.BinaryReader): EmptyRequest;
}

export namespace EmptyRequest {
  export type AsObject = {
  }
}

