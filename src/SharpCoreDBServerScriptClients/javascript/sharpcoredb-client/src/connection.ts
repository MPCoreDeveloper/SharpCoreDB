/**
 * Connection management for @sharpcoredb/client
 */

import { EventEmitter } from 'events';
import { GrpcClient } from './grpc-client';
import { HttpClient } from './http-client';
import { WebSocketClient } from './ws-client';
import {
  ConnectionOptions,
  QueryResult,
  ConnectionInfo,
  QueryParameters,
  Protocol,
  ConnectionState
} from './types';
import { ConnectionError, ConfigurationError } from './errors';

/**
 * Database connection to SharpCoreDB Server
 *
 * Supports automatic protocol selection (gRPC preferred, falls back to HTTP/WebSocket).
 */
export class Connection extends EventEmitter {
  private _host: string;
  private _port: number;
  private _database: string;
  private _username?: string;
  private _password?: string;
  private _tls: boolean;
  private _timeout: number;
  private _preferredProtocols: Protocol[];

  private _state: ConnectionState = 'disconnected';
  private _connectionInfo?: ConnectionInfo;

  private _grpcClient?: GrpcClient;
  private _httpClient?: HttpClient;
  private _wsClient?: WebSocketClient;

  /**
   * Create a new database connection
   */
  constructor(
    host: string,
    port: number,
    options: ConnectionOptions = {}
  ) {
    super();

    this._host = host;
    this._port = port;
    this._database = options.database || 'default';
    if (options.username !== undefined) {
      this._username = options.username;
    }
    if (options.password !== undefined) {
      this._password = options.password;
    }
    this._tls = options.tls !== false; // Default to true
    this._timeout = options.timeout || 30000;
    this._preferredProtocols = options.preferredProtocols || ['grpc', 'http', 'websocket'];
  }

  /**
   * Get the current connection state
   */
  get state(): ConnectionState {
    return this._state;
  }

  /**
   * Get connection information
   */
  get connectionInfo(): ConnectionInfo | undefined {
    return this._connectionInfo;
  }

  /**
   * Check if the connection is active
   */
  get isConnected(): boolean {
    return this._state === 'connected';
  }

  /**
   * Establish connection to the server
   */
  async connect(): Promise<void> {
    if (this._state === 'connected') {
      return;
    }

    this._state = 'connecting';
    this.emit('connecting');

    // Try protocols in order of preference
    for (const protocol of this._preferredProtocols) {
      try {
        if (protocol === 'grpc') {
          await this._connectGrpc();
        } else if (protocol === 'http') {
          await this._connectHttp();
        } else if (protocol === 'websocket') {
          await this._connectWebSocket();
        }

        this._state = 'connected';
        this.emit('connected', this._connectionInfo);
        return;
      } catch (error) {
        this.emit('protocolFailed', protocol, error);
        continue;
      }
    }

    this._state = 'error';
    this.emit('error', new ConnectionError(`Failed to connect to ${this._host}:${this._port}`));
    throw new ConnectionError(`Failed to connect to ${this._host}:${this._port}`);
  }

  /**
   * Connect using gRPC protocol
   */
  private async _connectGrpc(): Promise<void> {
    const port = this._port || 5001;
    this._grpcClient = new GrpcClient(this._host, port, {
      tls: this._tls,
      timeout: this._timeout
    });

    this._connectionInfo = await this._grpcClient.connect(
      this._database,
      this._username,
      this._password
    );
  }

  /**
   * Connect using HTTP REST API
   */
  private async _connectHttp(): Promise<void> {
    const port = this._port || 8443;
    this._httpClient = new HttpClient(this._host, port, {
      tls: this._tls,
      timeout: this._timeout
    });

    this._connectionInfo = await this._httpClient.connect(
      this._database,
      this._username,
      this._password
    );
  }

  /**
   * Connect using WebSocket protocol
   */
  private async _connectWebSocket(): Promise<void> {
    const port = this._port || 8443;
    this._wsClient = new WebSocketClient(this._host, port, {
      tls: this._tls,
      timeout: this._timeout
    });

    this._connectionInfo = await this._wsClient.connect(
      this._database,
      this._username,
      this._password
    );
  }

  /**
   * Close the connection
   */
  async close(): Promise<void> {
    if (this._state === 'disconnected') {
      return;
    }

    const previousState = this._state;
    this._state = 'disconnected';

    // Close all clients
    const closePromises: Promise<void>[] = [];

    if (this._grpcClient) {
      closePromises.push(this._grpcClient.disconnect());
    }

    if (this._httpClient) {
      closePromises.push(this._httpClient.disconnect());
    }

    if (this._wsClient) {
      closePromises.push(this._wsClient.disconnect());
    }

    try {
      await Promise.all(closePromises);
    } catch (error) {
      this.emit('error', error);
    }

    delete this._grpcClient;
    delete this._httpClient;
    delete this._wsClient;
    delete this._connectionInfo;

    this.emit('disconnected', previousState);
  }

  /**
   * Execute a SELECT query
   */
  async execute(sql: string, parameters?: QueryParameters): Promise<QueryResult> {
    if (!this.isConnected) {
      throw new ConnectionError('Not connected to server');
    }

    if (this._grpcClient) {
      return this._grpcClient.executeQuery(sql, parameters);
    } else if (this._httpClient) {
      return this._httpClient.executeQuery(sql, parameters);
    } else if (this._wsClient) {
      return this._wsClient.executeQuery(sql, parameters);
    }

    throw new ConnectionError('No active connection protocol');
  }

  /**
   * Execute a non-query SQL statement (INSERT/UPDATE/DELETE)
   */
  async executeNonQuery(sql: string, parameters?: QueryParameters): Promise<number> {
    if (!this.isConnected) {
      throw new ConnectionError('Not connected to server');
    }

    if (this._grpcClient) {
      return this._grpcClient.executeNonQuery(sql, parameters);
    } else if (this._httpClient) {
      return this._httpClient.executeNonQuery(sql, parameters);
    } else if (this._wsClient) {
      return this._wsClient.executeNonQuery(sql, parameters);
    }

    throw new ConnectionError('No active connection protocol');
  }

  /**
   * Ping the server and return round-trip time in milliseconds
   */
  async ping(): Promise<number> {
    if (!this.isConnected) {
      throw new ConnectionError('Not connected to server');
    }

    if (this._grpcClient) {
      return this._grpcClient.ping();
    } else if (this._httpClient) {
      return this._httpClient.ping();
    } else if (this._wsClient) {
      return this._wsClient.ping();
    }

    throw new ConnectionError('No active connection protocol');
  }
}

/**
 * Connect to a SharpCoreDB Server
 *
 * @param url Connection URL (e.g., "grpc://localhost:5001", "https://localhost:8443")
 * @param options Connection options
 * @returns Promise that resolves to a Connection instance
 */
export async function connect(url: string, options: ConnectionOptions = {}): Promise<Connection> {
  // Parse URL
  const urlPattern = /^(grpc|http|ws)s?:\/\/([^:\/]+)(?::(\d+))?(?:\/(.*))?$/;
  const match = url.match(urlPattern);

  if (!match) {
    throw new ConfigurationError(`Invalid connection URL: ${url}`);
  }

  const [, protocol, host, portStr] = match;
  if (!protocol || !host) {
    throw new ConfigurationError(`Invalid connection URL: ${url}`);
  }
  const port = portStr ? parseInt(portStr, 10) : undefined;

  // Determine default port based on protocol
  let defaultPort: number;
  if (protocol === 'grpc' || protocol === 'grpcs') {
    defaultPort = 5001;
  } else if (protocol === 'http' || protocol === 'https') {
    defaultPort = protocol === 'https' ? 443 : 80;
  } else if (protocol === 'ws' || protocol === 'wss') {
    defaultPort = protocol === 'wss' ? 443 : 80;
  } else {
    throw new ConfigurationError(`Unsupported protocol: ${protocol}`);
  }

  const connection = new Connection(host, port || defaultPort, {
    ...options,
    tls: protocol.includes('s') || options.tls !== false
  });

  await connection.connect();
  return connection;
}
