/**
 * gRPC client implementation for @sharpcoredb/client
 */

import * as grpc from '@grpc/grpc-js';
import * as protoLoader from '@grpc/proto-loader';
import { QueryResult, QueryParameters, ConnectionInfo } from './types';
import { ConnectionError, AuthenticationError, QueryError } from './errors';

// Mock protobuf message types (would be generated from sharpcoredb.proto)
interface ConnectRequest {
  databaseName: string;
  userName?: string;
  password?: string;
}

interface ConnectResponse {
  sessionId: string;
  serverVersion: string;
  status: number;
}

interface QueryRequest {
  sessionId: string;
  sql: string;
  parameters?: QueryParameters;
}

interface QueryResponse {
  columns: Array<{ name: string; type: string; nullable: boolean }>;
  rows: any[][];
  executionTimeMs: number;
}

interface NonQueryRequest {
  sessionId: string;
  sql: string;
  parameters?: QueryParameters;
}

interface NonQueryResponse {
  rowsAffected: number;
  executionTimeMs: number;
}

interface PingRequest {
  sessionId: string;
}

interface PingResponse {
  serverTime: number;
  activeConnections: number;
}

// Mock service client (would be generated)
interface DatabaseServiceClient {
  Connect(request: ConnectRequest): Promise<ConnectResponse>;
  ExecuteQuery(request: QueryRequest): Promise<QueryResponse>;
  ExecuteNonQuery(request: NonQueryRequest): Promise<NonQueryResponse>;
  Ping(request: PingRequest): Promise<PingResponse>;
  Disconnect(request: { sessionId: string }): Promise<void>;
}

/**
 * gRPC client for SharpCoreDB Server
 */
export class GrpcClient {
  private _host: string;
  private _port: number;
  private _tls: boolean;
  private _timeout: number;

  private _client: DatabaseServiceClient | undefined;
  private _sessionId: string | undefined;

  constructor(host: string, port: number, options: { tls?: boolean; timeout?: number } = {}) {
    this._host = host;
    this._port = port;
    this._tls = options.tls !== false;
    this._timeout = options.timeout || 30000;
  }

  /**
   * Establish gRPC connection and authenticate
   */
  async connect(database: string, username?: string, password?: string): Promise<ConnectionInfo> {
    const target = `${this._host}:${this._port}`;
    const credentials = this._tls
      ? grpc.credentials.createSsl()
      : grpc.credentials.createInsecure();

    // Load proto definition (mock for now)
    const packageDefinition = protoLoader.loadSync('', {
      keepCase: true,
      longs: String,
      enums: String,
      defaults: true,
      oneofs: true,
    });

    // Create client (mock implementation)
    this._client = grpc.loadPackageDefinition(packageDefinition) as any;

    // Mock connection
    const response: ConnectResponse = {
      sessionId: `grpc-session-${Date.now()}`,
      serverVersion: '1.5.0',
      status: 0 // SUCCESS
    };

    this._sessionId = response.sessionId;

    return {
      databaseName: database,
      sessionId: response.sessionId,
      serverVersion: response.serverVersion,
      connectedAt: new Date()
    };
  }

  /**
   * Close the gRPC connection
   */
  async disconnect(): Promise<void> {
    if (this._client) {
      // Close gRPC client
      // this._client.close();
      this._client = undefined;
    }
    this._sessionId = undefined;
  }

  /**
   * Execute a SELECT query
   */
  async executeQuery(sql: string, parameters?: QueryParameters): Promise<QueryResult> {
    if (!this._client || !this._sessionId) {
      throw new ConnectionError('Not connected');
    }

    const request: QueryRequest = {
      sessionId: this._sessionId,
      sql,
      ...(parameters && { parameters })
    };

    try {
      // Mock response
      const response: QueryResponse = {
        columns: [
          { name: 'id', type: 'INTEGER', nullable: false },
          { name: 'name', type: 'STRING', nullable: true }
        ],
        rows: [[1, 'Alice'], [2, 'Bob']],
        executionTimeMs: 1.5
      };

      return {
        columns: response.columns.map(col => ({
          name: col.name,
          type: col.type,
          nullable: col.nullable
        })),
        rows: response.rows.map(row => {
          const rowObj: any = {};
          response.columns.forEach((col, index) => {
            rowObj[col.name] = row[index];
            rowObj[index] = row[index];
          });
          return rowObj;
        }),
        rowCount: response.rows.length,
        executionTimeMs: response.executionTimeMs
      };
    } catch (error: any) {
      if (error.code === grpc.status.UNAUTHENTICATED) {
        throw new AuthenticationError('Authentication failed');
      } else if (error.code === grpc.status.INVALID_ARGUMENT) {
        throw new QueryError(error.details, sql, parameters);
      } else {
        throw new QueryError(`gRPC error: ${error.details}`, sql, parameters);
      }
    }
  }

  /**
   * Execute INSERT/UPDATE/DELETE
   */
  async executeNonQuery(sql: string, parameters?: QueryParameters): Promise<number> {
    if (!this._client || !this._sessionId) {
      throw new ConnectionError('Not connected');
    }

    const request: NonQueryRequest = {
      sessionId: this._sessionId,
      sql,
      // Conditional spread rather than `parameters`: exactOptionalPropertyTypes forbids
      // assigning an explicit `undefined` to the optional property (same form as
      // ws-client's message payloads).
      ...(parameters && { parameters })
    };

    try {
      // Mock response
      const response: NonQueryResponse = {
        rowsAffected: 1,
        executionTimeMs: 0.8
      };

      return response.rowsAffected;
    } catch (error: any) {
      if (error.code === grpc.status.UNAUTHENTICATED) {
        throw new AuthenticationError('Authentication failed');
      } else if (error.code === grpc.status.INVALID_ARGUMENT) {
        throw new QueryError(error.details, sql, parameters);
      } else {
        throw new QueryError(`gRPC error: ${error.details}`, sql, parameters);
      }
    }
  }

  /**
   * Ping the server
   */
  async ping(): Promise<number> {
    if (!this._client || !this._sessionId) {
      throw new ConnectionError('Not connected');
    }

    const startTime = Date.now();

    try {
      // Mock ping
      await new Promise(resolve => setTimeout(resolve, 1));
      return Date.now() - startTime;
    } catch (error: any) {
      throw new ConnectionError(`Ping failed: ${error.message}`);
    }
  }

  /**
   * Check if connected
   */
  get isConnected(): boolean {
    return this._client !== undefined && this._sessionId !== undefined;
  }
}
