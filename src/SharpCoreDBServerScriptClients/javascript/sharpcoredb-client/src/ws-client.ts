/**
 * WebSocket client implementation for @sharpcoredb/client
 */

import WebSocket from 'ws';
import { QueryResult, QueryParameters, ConnectionInfo } from './types';
import { ConnectionError, AuthenticationError, QueryError } from './errors';

/**
 * WebSocket client for SharpCoreDB Server streaming operations
 */
export class WebSocketClient {
  private _host: string;
  private _port: number;
  private _tls: boolean;
  private _timeout: number;

  // Declared `| undefined` rather than optional (`?`): with exactOptionalPropertyTypes
  // the optional form forbids an explicit `undefined` assignment, and disconnect()
  // releases the socket and session by assigning `undefined`.
  private _ws: WebSocket | undefined;
  private _sessionId: string | undefined;
  private _database: string | undefined;
  private _messageId = 0;

  private _responsePromises = new Map<number, {
    resolve: (value: any) => void;
    reject: (error: any) => void;
    timeout: NodeJS.Timeout;
  }>();

  constructor(host: string, port: number, options: { tls?: boolean; timeout?: number } = {}) {
    this._host = host;
    this._port = port;
    this._tls = options.tls !== false;
    this._timeout = options.timeout || 30000;
  }

  /**
   * Establish WebSocket connection and authenticate
   */
  async connect(database: string, username?: string, password?: string): Promise<ConnectionInfo> {
    this._database = database;

    const protocol = this._tls ? 'wss' : 'ws';
    const url = `${protocol}://${this._host}:${this._port}/ws`;

    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url, {
        headers: this._getAuthHeaders(username, password),
        timeout: this._timeout
      });

      const connectionTimeout = setTimeout(() => {
        ws.close();
        reject(new ConnectionError(`WebSocket connection timeout: ${this._host}:${this._port}`));
      }, this._timeout);

      ws.on('open', () => {
        clearTimeout(connectionTimeout);
        this._ws = ws;
        this._sessionId = `ws-session-${Date.now()}`;

        // Set up message handler
        ws.on('message', (data: Buffer) => {
          try {
            const message = JSON.parse(data.toString());
            this._handleMessage(message);
          } catch (error) {
            // Ignore invalid JSON
          }
        });

        ws.on('error', (error) => {
          // Connection errors will be handled by the promise
        });

        ws.on('close', () => {
          this._cleanup();
        });

        resolve({
          databaseName: database,
          sessionId: this._sessionId,
          serverVersion: '1.5.0',
          connectedAt: new Date()
        });
      });

      ws.on('error', (error) => {
        clearTimeout(connectionTimeout);
        reject(new ConnectionError(`WebSocket connection failed: ${error.message}`));
      });
    });
  }

  /**
   * Close the WebSocket connection
   */
  async disconnect(): Promise<void> {
    if (this._ws) {
      this._ws.close();
      this._ws = undefined;
    }
    this._sessionId = undefined;
    this._database = undefined;
    this._cleanup();
  }

  /**
   * Execute a SELECT query
   */
  async executeQuery(sql: string, parameters?: QueryParameters): Promise<QueryResult> {
    if (!this._ws || !this._sessionId || !this._database) {
      throw new ConnectionError('Not connected');
    }

    const messageId = this._getNextMessageId();
    const message = {
      id: messageId,
      type: 'query',
      sql,
      database: this._database,
      ...(parameters && { parameters })
    };

    const response = await this._sendMessage(message);

    if (response.error) {
      throw new QueryError(response.error, sql, parameters);
    }

    const data = response.data || {};

    return {
      columns: data.columns?.map((col: any) => ({
        name: col.name,
        type: col.type,
        nullable: col.nullable !== false
      })) || [],
      rows: data.rows?.map((row: any[], rowIndex: number) => {
        const rowObj: any = {};
        data.columns?.forEach((col: any, colIndex: number) => {
          rowObj[col.name] = row[colIndex];
          rowObj[colIndex] = row[colIndex];
        });
        return rowObj;
      }) || [],
      rowCount: data.rows?.length || 0,
      executionTimeMs: data.executionTimeMs || 0
    };
  }

  /**
   * Execute INSERT/UPDATE/DELETE
   */
  async executeNonQuery(sql: string, parameters?: QueryParameters): Promise<number> {
    if (!this._ws || !this._sessionId || !this._database) {
      throw new ConnectionError('Not connected');
    }

    const messageId = this._getNextMessageId();
    const message = {
      id: messageId,
      type: 'nonquery',
      sql,
      database: this._database,
      ...(parameters && { parameters })
    };

    const response = await this._sendMessage(message);

    if (response.error) {
      throw new QueryError(response.error, sql, parameters);
    }

    return response.data?.rowsAffected || 0;
  }

  /**
   * Ping the server
   */
  async ping(): Promise<number> {
    if (!this._ws || !this._sessionId) {
      throw new ConnectionError('Not connected');
    }

    const startTime = Date.now();
    const messageId = this._getNextMessageId();
    const message = {
      id: messageId,
      type: 'ping'
    };

    await this._sendMessage(message);
    return Date.now() - startTime;
  }

  /**
   * Send a message and wait for response
   */
  private _sendMessage(message: any): Promise<any> {
    return new Promise((resolve, reject) => {
      if (!this._ws) {
        reject(new ConnectionError('WebSocket not connected'));
        return;
      }

      const messageId = message.id;
      const timeout = setTimeout(() => {
        this._responsePromises.delete(messageId);
        reject(new QueryError('Request timeout'));
      }, this._timeout);

      this._responsePromises.set(messageId, { resolve, reject, timeout });

      try {
        this._ws.send(JSON.stringify(message));
      } catch (error) {
        clearTimeout(timeout);
        this._responsePromises.delete(messageId);
        reject(new ConnectionError(`Failed to send message: ${error}`));
      }
    });
  }

  /**
   * Handle incoming messages
   */
  private _handleMessage(message: any): void {
    const messageId = message.id;
    const promise = this._responsePromises.get(messageId);

    if (promise) {
      clearTimeout(promise.timeout);
      this._responsePromises.delete(messageId);

      if (message.error) {
        promise.reject(new Error(message.error));
      } else {
        promise.resolve(message);
      }
    }
  }

  /**
   * Clean up pending promises
   */
  private _cleanup(): void {
    for (const [messageId, promise] of this._responsePromises) {
      clearTimeout(promise.timeout);
      promise.reject(new ConnectionError('Connection closed'));
    }
    this._responsePromises.clear();
  }

  /**
   * Get next message ID
   */
  private _getNextMessageId(): number {
    this._messageId = (this._messageId + 1) % 0xFFFFFFFF;
    return this._messageId;
  }

  /**
   * Get authentication headers
   */
  private _getAuthHeaders(username?: string, password?: string): Record<string, string> {
    const headers: Record<string, string> = {
      'User-Agent': '@sharpcoredb/client/1.5.0'
    };

    // TODO: Add authentication headers if needed
    if (username && password) {
      // Could add basic auth or JWT
    }

    return headers;
  }

  /**
   * Check if connected
   */
  get isConnected(): boolean {
    return this._ws !== undefined &&
           this._ws.readyState === WebSocket.OPEN &&
           this._sessionId !== undefined;
  }
}
