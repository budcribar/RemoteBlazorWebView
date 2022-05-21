// package: webview
// file: webview.proto

import * as jspb from "google-protobuf";
import * as google_protobuf_empty_pb from "google-protobuf/google/protobuf/empty_pb";
import * as google_protobuf_timestamp_pb from "google-protobuf/google/protobuf/timestamp_pb";

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

export class ClientIdMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getClientid(): string;
  setClientid(value: string): void;

  getIsprimary(): boolean;
  setIsprimary(value: boolean): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): ClientIdMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: ClientIdMessageRequest): ClientIdMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: ClientIdMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): ClientIdMessageRequest;
  static deserializeBinaryFromReader(message: ClientIdMessageRequest, reader: jspb.BinaryReader): ClientIdMessageRequest;
}

export namespace ClientIdMessageRequest {
  export type AsObject = {
    id: string,
    clientid: string,
    isprimary: boolean,
  }
}

export class CreateWebViewRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getHtmlhostpath(): string;
  setHtmlhostpath(value: string): void;

  getMarkup(): string;
  setMarkup(value: string): void;

  getGroup(): string;
  setGroup(value: string): void;

  getPid(): number;
  setPid(value: number): void;

  getProcessname(): string;
  setProcessname(value: string): void;

  getHostname(): string;
  setHostname(value: string): void;

  getEnablemirrors(): boolean;
  setEnablemirrors(value: boolean): void;

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
    markup: string,
    group: string,
    pid: number,
    processname: string,
    hostname: string,
    enablemirrors: boolean,
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

export class FileReadInitRequest extends jspb.Message {
  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): FileReadInitRequest.AsObject;
  static toObject(includeInstance: boolean, msg: FileReadInitRequest): FileReadInitRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: FileReadInitRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): FileReadInitRequest;
  static deserializeBinaryFromReader(message: FileReadInitRequest, reader: jspb.BinaryReader): FileReadInitRequest;
}

export namespace FileReadInitRequest {
  export type AsObject = {
  }
}

export class FileReadDataRequest extends jspb.Message {
  getPath(): string;
  setPath(value: string): void;

  getData(): Uint8Array | string;
  getData_asU8(): Uint8Array;
  getData_asB64(): string;
  setData(value: Uint8Array | string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): FileReadDataRequest.AsObject;
  static toObject(includeInstance: boolean, msg: FileReadDataRequest): FileReadDataRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: FileReadDataRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): FileReadDataRequest;
  static deserializeBinaryFromReader(message: FileReadDataRequest, reader: jspb.BinaryReader): FileReadDataRequest;
}

export namespace FileReadDataRequest {
  export type AsObject = {
    path: string,
    data: Uint8Array | string,
  }
}

export class FileReadLengthRequest extends jspb.Message {
  getPath(): string;
  setPath(value: string): void;

  getLength(): number;
  setLength(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): FileReadLengthRequest.AsObject;
  static toObject(includeInstance: boolean, msg: FileReadLengthRequest): FileReadLengthRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: FileReadLengthRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): FileReadLengthRequest;
  static deserializeBinaryFromReader(message: FileReadLengthRequest, reader: jspb.BinaryReader): FileReadLengthRequest;
}

export namespace FileReadLengthRequest {
  export type AsObject = {
    path: string,
    length: number,
  }
}

export class FileReadRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  hasInit(): boolean;
  clearInit(): void;
  getInit(): FileReadInitRequest | undefined;
  setInit(value?: FileReadInitRequest): void;

  hasLength(): boolean;
  clearLength(): void;
  getLength(): FileReadLengthRequest | undefined;
  setLength(value?: FileReadLengthRequest): void;

  hasData(): boolean;
  clearData(): void;
  getData(): FileReadDataRequest | undefined;
  setData(value?: FileReadDataRequest): void;

  getFilereadCase(): FileReadRequest.FilereadCase;
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
    init?: FileReadInitRequest.AsObject,
    length?: FileReadLengthRequest.AsObject,
    data?: FileReadDataRequest.AsObject,
  }

  export enum FilereadCase {
    FILEREAD_NOT_SET = 0,
    INIT = 2,
    LENGTH = 3,
    DATA = 4,
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

  getClientid(): string;
  setClientid(value: string): void;

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
    clientid: string,
  }
}

export class SendMessageResponse extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getSuccess(): boolean;
  setSuccess(value: boolean): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): SendMessageResponse.AsObject;
  static toObject(includeInstance: boolean, msg: SendMessageResponse): SendMessageResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: SendMessageResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): SendMessageResponse;
  static deserializeBinaryFromReader(message: SendMessageResponse, reader: jspb.BinaryReader): SendMessageResponse;
}

export namespace SendMessageResponse {
  export type AsObject = {
    id: string,
    success: boolean,
  }
}

export class PingMessageResponse extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getCancelled(): boolean;
  setCancelled(value: boolean): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): PingMessageResponse.AsObject;
  static toObject(includeInstance: boolean, msg: PingMessageResponse): PingMessageResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: PingMessageResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): PingMessageResponse;
  static deserializeBinaryFromReader(message: PingMessageResponse, reader: jspb.BinaryReader): PingMessageResponse;
}

export namespace PingMessageResponse {
  export type AsObject = {
    id: string,
    cancelled: boolean,
  }
}

export class PingMessageRequest extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getInitialize(): boolean;
  setInitialize(value: boolean): void;

  getPingintervalseconds(): number;
  setPingintervalseconds(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): PingMessageRequest.AsObject;
  static toObject(includeInstance: boolean, msg: PingMessageRequest): PingMessageRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: PingMessageRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): PingMessageRequest;
  static deserializeBinaryFromReader(message: PingMessageRequest, reader: jspb.BinaryReader): PingMessageRequest;
}

export namespace PingMessageRequest {
  export type AsObject = {
    id: string,
    initialize: boolean,
    pingintervalseconds: number,
  }
}

export class ClientResponse extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  getMarkup(): string;
  setMarkup(value: string): void;

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
    markup: string,
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

  getId(): string;
  setId(value: string): void;

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
    id: string,
  }
}

export class UserRequest extends jspb.Message {
  getOid(): string;
  setOid(value: string): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): UserRequest.AsObject;
  static toObject(includeInstance: boolean, msg: UserRequest): UserRequest.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: UserRequest, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): UserRequest;
  static deserializeBinaryFromReader(message: UserRequest, reader: jspb.BinaryReader): UserRequest;
}

export namespace UserRequest {
  export type AsObject = {
    oid: string,
  }
}

export class UserResponse extends jspb.Message {
  clearGroupsList(): void;
  getGroupsList(): Array<string>;
  setGroupsList(value: Array<string>): void;
  addGroups(value: string, index?: number): string;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): UserResponse.AsObject;
  static toObject(includeInstance: boolean, msg: UserResponse): UserResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: UserResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): UserResponse;
  static deserializeBinaryFromReader(message: UserResponse, reader: jspb.BinaryReader): UserResponse;
}

export namespace UserResponse {
  export type AsObject = {
    groupsList: Array<string>,
  }
}

export class TaskResponse extends jspb.Message {
  getName(): string;
  setName(value: string): void;

  getStatus(): TaskStatusMap[keyof TaskStatusMap];
  setStatus(value: TaskStatusMap[keyof TaskStatusMap]): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): TaskResponse.AsObject;
  static toObject(includeInstance: boolean, msg: TaskResponse): TaskResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: TaskResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): TaskResponse;
  static deserializeBinaryFromReader(message: TaskResponse, reader: jspb.BinaryReader): TaskResponse;
}

export namespace TaskResponse {
  export type AsObject = {
    name: string,
    status: TaskStatusMap[keyof TaskStatusMap],
  }
}

export class ConnectionResponse extends jspb.Message {
  getId(): string;
  setId(value: string): void;

  clearTaskresponsesList(): void;
  getTaskresponsesList(): Array<TaskResponse>;
  setTaskresponsesList(value: Array<TaskResponse>): void;
  addTaskresponses(value?: TaskResponse, index?: number): TaskResponse;

  getInuse(): boolean;
  setInuse(value: boolean): void;

  getHostname(): string;
  setHostname(value: string): void;

  getUsername(): string;
  setUsername(value: string): void;

  getMaxfilereadtime(): number;
  setMaxfilereadtime(value: number): void;

  getTotalbytesread(): number;
  setTotalbytesread(value: number): void;

  getTotalfilesread(): number;
  setTotalfilesread(value: number): void;

  getTotalreadtime(): number;
  setTotalreadtime(value: number): void;

  getTimeconnected(): number;
  setTimeconnected(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): ConnectionResponse.AsObject;
  static toObject(includeInstance: boolean, msg: ConnectionResponse): ConnectionResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: ConnectionResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): ConnectionResponse;
  static deserializeBinaryFromReader(message: ConnectionResponse, reader: jspb.BinaryReader): ConnectionResponse;
}

export namespace ConnectionResponse {
  export type AsObject = {
    id: string,
    taskresponsesList: Array<TaskResponse.AsObject>,
    inuse: boolean,
    hostname: string,
    username: string,
    maxfilereadtime: number,
    totalbytesread: number,
    totalfilesread: number,
    totalreadtime: number,
    timeconnected: number,
  }
}

export class ServerResponse extends jspb.Message {
  getWorkingset(): number;
  setWorkingset(value: number): void;

  getPeakworkingset(): number;
  setPeakworkingset(value: number): void;

  getThreads(): number;
  setThreads(value: number): void;

  getHandles(): number;
  setHandles(value: number): void;

  clearConnectionresponsesList(): void;
  getConnectionresponsesList(): Array<ConnectionResponse>;
  setConnectionresponsesList(value: Array<ConnectionResponse>): void;
  addConnectionresponses(value?: ConnectionResponse, index?: number): ConnectionResponse;

  getTotalprocessortime(): number;
  setTotalprocessortime(value: number): void;

  getUptime(): number;
  setUptime(value: number): void;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): ServerResponse.AsObject;
  static toObject(includeInstance: boolean, msg: ServerResponse): ServerResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: ServerResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): ServerResponse;
  static deserializeBinaryFromReader(message: ServerResponse, reader: jspb.BinaryReader): ServerResponse;
}

export namespace ServerResponse {
  export type AsObject = {
    workingset: number,
    peakworkingset: number,
    threads: number,
    handles: number,
    connectionresponsesList: Array<ConnectionResponse.AsObject>,
    totalprocessortime: number,
    uptime: number,
  }
}

export class EventResponse extends jspb.Message {
  hasTimestamp(): boolean;
  clearTimestamp(): void;
  getTimestamp(): google_protobuf_timestamp_pb.Timestamp | undefined;
  setTimestamp(value?: google_protobuf_timestamp_pb.Timestamp): void;

  clearMessagesList(): void;
  getMessagesList(): Array<string>;
  setMessagesList(value: Array<string>): void;
  addMessages(value: string, index?: number): string;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): EventResponse.AsObject;
  static toObject(includeInstance: boolean, msg: EventResponse): EventResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: EventResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): EventResponse;
  static deserializeBinaryFromReader(message: EventResponse, reader: jspb.BinaryReader): EventResponse;
}

export namespace EventResponse {
  export type AsObject = {
    timestamp?: google_protobuf_timestamp_pb.Timestamp.AsObject,
    messagesList: Array<string>,
  }
}

export class LoggedEventResponse extends jspb.Message {
  clearEventresponsesList(): void;
  getEventresponsesList(): Array<EventResponse>;
  setEventresponsesList(value: Array<EventResponse>): void;
  addEventresponses(value?: EventResponse, index?: number): EventResponse;

  serializeBinary(): Uint8Array;
  toObject(includeInstance?: boolean): LoggedEventResponse.AsObject;
  static toObject(includeInstance: boolean, msg: LoggedEventResponse): LoggedEventResponse.AsObject;
  static extensions: {[key: number]: jspb.ExtensionFieldInfo<jspb.Message>};
  static extensionsBinary: {[key: number]: jspb.ExtensionFieldBinaryInfo<jspb.Message>};
  static serializeBinaryToWriter(message: LoggedEventResponse, writer: jspb.BinaryWriter): void;
  static deserializeBinary(bytes: Uint8Array): LoggedEventResponse;
  static deserializeBinaryFromReader(message: LoggedEventResponse, reader: jspb.BinaryReader): LoggedEventResponse;
}

export namespace LoggedEventResponse {
  export type AsObject = {
    eventresponsesList: Array<EventResponse.AsObject>,
  }
}

export interface ClientStateMap {
  CONNECTED: 0;
  SHUTTINGDOWN: 1;
}

export const ClientState: ClientStateMap;

export interface TaskStatusMap {
  CREATED: 0;
  WAITINGFORACTIVATION: 1;
  WAITINGTORUN: 2;
  RUNNING: 3;
  WAITINGFORCHILDRENTOCOMPLETE: 4;
  RANTOCOMPLETION: 5;
  CANCELED: 6;
  FAULTED: 7;
}

export const TaskStatus: TaskStatusMap;

