/**
 * HTTP REST client implementation for @sharpcoredb/client
 */

import axios, { AxiosInstance, AxiosResponse } from 'axios';
import { QueryResult, QueryParameters, ConnectionInfo } from './types';
import { ConnectionError, AuthenticationError, QueryError } from './errors';

/**
 * HTTP REST client for SharpCoreDB Server
 */
export class HttpClient {
  private _host: string;
  private _port: number;
  private _tls: boolean;
  private _timeout: number;

  private _httpClient: AxiosInstance;
  // `| undefined` rather than optional (`?`) for the same reason as ws-client: with
  // exactOptionalPropertyTypes the optional form forbids an explicit `undefined`
  // assignment, and disconnect() clears both fields that way.
  private _authToken: string | undefined;
  private _database: string | undefined;

  constructor(host: string, port: number, options: { tls?: boolean; timeout?: number } = {}) {
    this._host = host;
    this._port = port;
    this._tls = options.tls !== false;
    this._timeout = options.timeout || 30000;

    const baseURL = `${this._tls ? 'https' : 'http'}://${host}:${port}/api`;

    this._httpClient = axios.create({
      baseURL,
      timeout: this._timeout,
      headers: {
        'Content-Type': 'application/json',
        'User-Agent': '@sharpcoredb/client/1.5.0'
      }
    });

    // Add response interceptor for error handling
    this._httpClient.interceptors.response.use(
      (response) => response,
      (error) => {
        if (error.response) {
          const status = error.response.status;
          const message = error.response.data?.message || error.message;

          if (status === 401) {
            throw new AuthenticationError(message);
          } else if (status === 400) {
            throw new QueryError(message);
          } else {
            throw new QueryError(`HTTP ${status}: ${message}`);
          }
        } else if (error.code === 'ECONNREFUSED') {
          throw new ConnectionError(`Connection refused: ${host}:${port}`);
        } else {
          throw new ConnectionError(`HTTP request failed: ${error.message}`);
        }
      }
    );
  }

  /**
   * Establish HTTP connection and authenticate
   */
  async connect(database: string, username?: string, password?: string): Promise<ConnectionInfo> {
    this._database = database;

    // For HTTP, authentication is done per request
    // We'll store credentials for future requests
    if (username && password) {
      // TODO: Implement JWT authentication
      // For now, we'll use basic auth or API key
      this._authToken = btoa(`${username}:${password}`);
    }

    // Test connection with health check
    try {
      const response = await this._httpClient.get('/health');
      const healthData = response.data;

      return {
        databaseName: database,
        serverVersion: healthData.version || '1.5.0',
        connectedAt: new Date()
      };
    } catch (error) {
      throw new ConnectionError(`Failed to connect to HTTP endpoint: ${error}`);
    }
  }

  /**
   * Close the HTTP connection
   */
  async disconnect(): Promise<void> {
    this._authToken = undefined;
    this._database = undefined;
  }

  /**
   * Execute a SELECT query
   */
  async executeQuery(sql: string, parameters?: QueryParameters): Promise<QueryResult> {
    if (!this._database) {
      throw new ConnectionError('Not connected');
    }

    const payload = {
      sql,
      database: this._database,
      ...(parameters && { parameters })
    };

    const response = await this._httpClient.post('/query', payload, {
      headers: this._getAuthHeaders()
    });

    const data = response.data;

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
    if (!this._database) {
      throw new ConnectionError('Not connected');
    }

    const payload = {
      sql,
      database: this._database,
      ...(parameters && { parameters })
    };

    const response = await this._httpClient.post('/nonquery', payload, {
      headers: this._getAuthHeaders()
    });

    return response.data.rowsAffected || 0;
  }

  /**
   * Ping the server
   */
  async ping(): Promise<number> {
    const startTime = Date.now();

    await this._httpClient.get('/health');

    return Date.now() - startTime;
  }

  /**
   * Get authentication headers
   */
  private _getAuthHeaders(): Record<string, string> {
    const headers: Record<string, string> = {};

    if (this._authToken) {
      headers['Authorization'] = `Basic ${this._authToken}`;
    }

    return headers;
  }

  /**
   * Check if connected
   */
  get isConnected(): boolean {
    return this._database !== undefined;
  }
}
