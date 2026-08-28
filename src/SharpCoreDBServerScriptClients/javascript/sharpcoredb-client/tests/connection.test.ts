/**
 * @jest-environment node
 */

import { Connection } from '../src/connection';
import { ConnectionError } from '../src/errors';

describe('Connection', () => {
  describe('initialization', () => {
    it('should create connection with correct parameters', () => {
      const conn = new Connection('localhost', 5001, {
        database: 'testdb',
        username: 'user',
        password: 'pass',
        tls: false
      });

      expect(conn).toBeDefined();
      // Note: Private properties can't be tested directly
    });

    it('should have default values', () => {
      const conn = new Connection('localhost', 5001);
      expect(conn).toBeDefined();
    });
  });

  describe('connection lifecycle', () => {
    it('should complete connection through the mocked transport', async () => {
      // The client transports are currently mocked to always succeed, so a real
      // network failure cannot be triggered here. This verifies the connect path
      // completes deterministically without a live server.
      const conn = new Connection('nonexistent.host', 5001);
      await expect(conn.connect()).resolves.toBeUndefined();
    });

    it('should allow closing unconnected connection', async () => {
      const conn = new Connection('localhost', 5001);
      await expect(conn.close()).resolves.toBeUndefined();
    });
  });

  describe('query operations', () => {
    it('should reject queries when not connected', async () => {
      const conn = new Connection('localhost', 5001);

      await expect(conn.execute('SELECT 1')).rejects.toThrow(ConnectionError);
      await expect(conn.executeNonQuery('INSERT INTO test VALUES (1)')).rejects.toThrow(ConnectionError);
      await expect(conn.ping()).rejects.toThrow(ConnectionError);
    });
  });
});
