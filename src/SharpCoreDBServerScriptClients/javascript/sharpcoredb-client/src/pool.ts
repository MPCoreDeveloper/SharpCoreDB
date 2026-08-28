/**
 * Connection pooling for @sharpcoredb/client
 */

import { EventEmitter } from 'events';
import { Connection } from './connection';
import { PoolOptions, ConnectionOptions } from './types';
import { ConnectionError } from './errors';

/**
 * A pooled database connection
 */
export class PooledConnection extends EventEmitter {
  private _connection: Connection;
  private _pool: ConnectionPool;
  private _createdAt: number;
  private _lastUsed: number;
  private _closed: boolean;

  constructor(connection: Connection, pool: ConnectionPool) {
    super();
    this._connection = connection;
    this._pool = pool;
    this._createdAt = Date.now();
    this._lastUsed = Date.now();
    this._closed = false;

    // Forward connection events
    this._connection.on('connected', (...args) => this.emit('connected', ...args));
    this._connection.on('disconnected', (...args) => this.emit('disconnected', ...args));
    this._connection.on('error', (...args) => this.emit('error', ...args));
  }

  /**
   * Get the underlying connection
   */
  get connection(): Connection {
    return this._connection;
  }

  /**
   * Check if the pooled connection is closed
   */
  get isClosed(): boolean {
    return this._closed;
  }

  /**
   * Get creation timestamp
   */
  get createdAt(): number {
    return this._createdAt;
  }

  /**
   * Get last used timestamp
   */
  get lastUsed(): number {
    return this._lastUsed;
  }

  /**
   * Update last used timestamp
   */
  markUsed(): void {
    this._lastUsed = Date.now();
  }

  /**
   * Return connection to pool
   */
  async close(): Promise<void> {
    if (this._closed) {
      return;
    }
    this._closed = true;
    await this._pool._returnConnection(this);
  }

  /**
   * Check if connection is valid
   */
  isValid(maxIdleTime: number, maxLifetime: number): boolean {
    const now = Date.now();

    // Check lifetime
    if (now - this._createdAt > maxLifetime) {
      return false;
    }

    // Check idle time
    if (now - this._lastUsed > maxIdleTime) {
      return false;
    }

    // Check if underlying connection is still connected
    return this._connection.isConnected;
  }
}

/**
 * Connection pool for efficient connection reuse
 */
export class ConnectionPool extends EventEmitter {
  private _host: string;
  private _port: number;
  private _database: string;
  private _username?: string;
  private _password?: string;
  private _tls: boolean;

  private _minConnections: number;
  private _maxConnections: number;
  private _maxIdleTime: number;
  private _maxLifetime: number;
  private _acquireTimeout: number;

  private _available: PooledConnection[] = [];
  private _inUse: Map<Connection, PooledConnection> = new Map();
  private _closed: boolean = false;

  // Statistics
  private _createdCount: number = 0;
  private _destroyedCount: number = 0;

  constructor(
    host: string,
    port: number,
    options: PoolOptions = {}
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
    this._tls = options.tls !== false;

    this._minConnections = options.minConnections || 1;
    this._maxConnections = options.maxConnections || 10;
    this._maxIdleTime = options.maxIdleTime || 300000; // 5 minutes
    this._maxLifetime = options.maxLifetime || 3600000; // 1 hour
    this._acquireTimeout = options.acquireTimeout || 30000; // 30 seconds
  }

  /**
   * Get a connection from the pool
   */
  async getConnection(): Promise<PooledConnection> {
    if (this._closed) {
      throw new ConnectionError('Connection pool is closed');
    }

    // Try to get an available connection
    while (this._available.length > 0) {
      const pooledConn = this._available.pop()!;
      if (this._isConnectionValid(pooledConn)) {
        pooledConn.markUsed();
        this._inUse.set(pooledConn.connection, pooledConn);
        return pooledConn;
      } else {
        await this._destroyConnection(pooledConn);
      }
    }

    // Create new connection if under limit
    if (this._inUse.size < this._maxConnections) {
      const connectionOptions: ConnectionOptions = {
        database: this._database,
        tls: this._tls
      };
      if (this._username !== undefined) {
        connectionOptions.username = this._username;
      }
      if (this._password !== undefined) {
        connectionOptions.password = this._password;
      }
      const connection = new Connection(this._host, this._port, connectionOptions);

      await connection.connect();

      const pooledConn = new PooledConnection(connection, this);
      this._createdCount++;
      this._inUse.set(connection, pooledConn);
      return pooledConn;
    }

    // Wait for a connection to become available or timeout
    throw new ConnectionError(
      `Connection pool exhausted: ${this._inUse.size}/${this._maxConnections} connections in use`
    );
  }

  /**
   * Return a connection to the pool
   */
  async _returnConnection(pooledConn: PooledConnection): Promise<void> {
    if (this._inUse.has(pooledConn.connection)) {
      this._inUse.delete(pooledConn.connection);

      if (this._isConnectionValid(pooledConn)) {
        this._available.push(pooledConn);
      } else {
        await this._destroyConnection(pooledConn);
      }
    }
  }

  /**
   * Destroy a connection
   */
  private async _destroyConnection(pooledConn: PooledConnection): Promise<void> {
    try {
      await pooledConn.connection.close();
    } catch (error) {
      this.emit('error', error);
    }
    this._destroyedCount++;
  }

  /**
   * Check if a connection is valid
   */
  private _isConnectionValid(pooledConn: PooledConnection): boolean {
    return pooledConn.isValid(this._maxIdleTime, this._maxLifetime);
  }

  /**
   * Close the connection pool
   */
  async close(): Promise<void> {
    if (this._closed) {
      return;
    }

    this._closed = true;

    // Close all available connections
    const destroyPromises: Promise<void>[] = [];
    for (const pooledConn of this._available) {
      destroyPromises.push(this._destroyConnection(pooledConn));
    }

    // Close all in-use connections
    for (const pooledConn of this._inUse.values()) {
      destroyPromises.push(this._destroyConnection(pooledConn));
    }

    this._available = [];
    this._inUse.clear();

    await Promise.all(destroyPromises);
    this.emit('closed');
  }

  /**
   * Get pool statistics
   */
  get stats() {
    return {
      available: this._available.length,
      inUse: this._inUse.size,
      totalCreated: this._createdCount,
      totalDestroyed: this._destroyedCount,
      maxConnections: this._maxConnections
    };
  }

  /**
   * Check if pool is closed
   */
  get isClosed(): boolean {
    return this._closed;
  }

  /**
   * Get host
   */
  get host(): string {
    return this._host;
  }

  /**
   * Get port
   */
  get port(): number {
    return this._port;
  }

  /**
   * Get database
   */
  get database(): string {
    return this._database;
  }
}

/**
 * Create a connection pool
 */
export async function createPool(
  host: string,
  port: number,
  options: PoolOptions = {}
): Promise<ConnectionPool> {
  const pool = new ConnectionPool(host, port, options);
  // Optionally pre-warm the pool here
  return pool;
}
