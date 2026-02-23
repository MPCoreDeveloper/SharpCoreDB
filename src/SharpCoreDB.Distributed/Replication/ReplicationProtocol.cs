// <copyright file="ReplicationProtocol.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Text;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Defines the replication communication protocol for SharpCoreDB.
/// C# 14: Primary constructors, pattern matching, modern async patterns.
/// </summary>
public static class ReplicationProtocol
{
    /// <summary>Protocol version for compatibility checking.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Maximum message size in bytes.</summary>
    public const int MaxMessageSize = 64 * 1024 * 1024; // 64MB

    /// <summary>Heartbeat interval for connection monitoring.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>Connection timeout for replication links.</summary>
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Replication message types.</summary>
    public enum MessageType : byte
    {
        /// <summary>Handshake message to establish connection.</summary>
        Handshake = 1,

        /// <summary>Heartbeat to keep connection alive.</summary>
        Heartbeat = 2,

        /// <summary>WAL entry for replication.</summary>
        WalEntry = 3,

        /// <summary>Acknowledgment of received data.</summary>
        Acknowledgment = 4,

        /// <summary>Request for missing WAL entries.</summary>
        CatchupRequest = 5,

        /// <summary>Response with missing WAL entries.</summary>
        CatchupResponse = 6,

        /// <summary>Notification of role change (master/slave).</summary>
        RoleChange = 7,

        /// <summary>Error message.</summary>
        Error = 255
    }

    /// <summary>Replication roles.</summary>
    public enum ReplicationRole : byte
    {
        /// <summary>Master node that accepts writes.</summary>
        Master = 1,

        /// <summary>Slave node that replicates from master.</summary>
        Slave = 2,

        /// <summary>Standby node ready for promotion.</summary>
        Standby = 3
    }

    /// <summary>Replication states.</summary>
    public enum ReplicationState : byte
    {
        /// <summary>Replication is starting up.</summary>
        Starting = 1,

        /// <summary>Performing initial synchronization.</summary>
        CatchingUp = 2,

        /// <summary>Replication is active and in sync.</summary>
        Streaming = 3,

        /// <summary>Replication is paused.</summary>
        Paused = 4,

        /// <summary>Replication has stopped.</summary>
        Stopped = 5,

        /// <summary>Replication encountered an error.</summary>
        Error = 6
    }

    /// <summary>
    /// Base class for all replication messages.
    /// </summary>
    public abstract class ReplicationMessage
    {
        /// <summary>Gets the message type.</summary>
        public abstract MessageType Type { get; }

        /// <summary>Gets the message sequence number.</summary>
        public long SequenceNumber { get; set; }

        /// <summary>Gets the timestamp when the message was created.</summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Serializes the message to a byte array.
        /// </summary>
        /// <returns>Serialized message bytes.</returns>
        public abstract byte[] Serialize();

        /// <summary>
        /// Deserializes a message from a byte array.
        /// </summary>
        /// <param name="data">The serialized message data.</param>
        /// <returns>The deserialized message.</returns>
        public static ReplicationMessage Deserialize(ReadOnlySpan<byte> data)
        {
            if (data.Length < 1)
            {
                throw new InvalidOperationException("Message data too short");
            }

            var messageType = (MessageType)data[0];
            var sequenceNumber = BitConverter.ToInt64(data[1..9]);
            var timestampTicks = BitConverter.ToInt64(data[9..17]);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampTicks);

            var payload = data[17..];

            ReplicationMessage message = messageType switch
            {
                MessageType.Handshake => HandshakeMessage.Deserialize(payload),
                MessageType.Heartbeat => HeartbeatMessage.Deserialize(payload),
                MessageType.WalEntry => WalEntryMessage.Deserialize(payload),
                MessageType.Acknowledgment => AcknowledgmentMessage.Deserialize(payload),
                MessageType.CatchupRequest => CatchupRequestMessage.Deserialize(payload),
                MessageType.CatchupResponse => CatchupResponseMessage.Deserialize(payload),
                MessageType.RoleChange => RoleChangeMessage.Deserialize(payload),
                MessageType.Error => ErrorMessage.Deserialize(payload),
                _ => throw new InvalidOperationException($"Unknown message type: {messageType}")
            };

            message.SequenceNumber = sequenceNumber;
            message.Timestamp = timestamp;

            return message;
        }

        /// <summary>
        /// Creates the common message header.
        /// </summary>
        /// <param name="type">The message type.</param>
        /// <param name="payloadSize">The size of the message payload.</param>
        /// <returns>The message header bytes.</returns>
        protected static byte[] CreateHeader(MessageType type, int payloadSize)
        {
            var headerSize = 1 + 8 + 8; // type + sequence + timestamp
            var totalSize = headerSize + payloadSize;

            if (totalSize > MaxMessageSize)
            {
                throw new InvalidOperationException($"Message size {totalSize} exceeds maximum {MaxMessageSize}");
            }

            var buffer = new byte[totalSize];
            buffer[0] = (byte)type;

            // Sequence number and timestamp will be set by the sender
            return buffer;
        }
    }

    /// <summary>
    /// Handshake message for establishing replication connection.
    /// </summary>
    public sealed class HandshakeMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.Handshake;

        public required string NodeId { get; init; }
        public required ReplicationRole Role { get; init; }
        public required long LastWalPosition { get; init; }
        public required int ProtocolVersion { get; init; }

        public override byte[] Serialize()
        {
            var nodeIdBytes = Encoding.UTF8.GetBytes(NodeId);
            var payloadSize = 1 + 4 + 8 + 4 + nodeIdBytes.Length; // role + protocol + position + nodeIdLength + nodeId

            var buffer = CreateHeader(Type, payloadSize);
            var offset = 17; // Skip header

            buffer[offset++] = (byte)Role;
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), ProtocolVersion);
            offset += 4;
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), LastWalPosition);
            offset += 8;
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), nodeIdBytes.Length);
            offset += 4;
            nodeIdBytes.CopyTo(buffer.AsSpan(offset));

            return buffer;
        }

        public static HandshakeMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var offset = 0;
            var role = (ReplicationRole)payload[offset++];
            var protocolVersion = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;
            var lastWalPosition = BitConverter.ToInt64(payload[offset..(offset + 8)]);
            offset += 8;
            var nodeIdLength = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;
            var nodeId = Encoding.UTF8.GetString(payload[offset..(offset + nodeIdLength)]);

            return new HandshakeMessage
            {
                Role = role,
                ProtocolVersion = protocolVersion,
                LastWalPosition = lastWalPosition,
                NodeId = nodeId
            };
        }
    }

    /// <summary>
    /// Heartbeat message to keep connection alive.
    /// </summary>
    public sealed class HeartbeatMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.Heartbeat;

        public required long CurrentWalPosition { get; init; }

        public override byte[] Serialize()
        {
            var payloadSize = 8; // wal position
            var buffer = CreateHeader(Type, payloadSize);
            BitConverter.TryWriteBytes(buffer.AsSpan(17), CurrentWalPosition);
            return buffer;
        }

        public static HeartbeatMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var currentWalPosition = BitConverter.ToInt64(payload);
            return new HeartbeatMessage { CurrentWalPosition = currentWalPosition };
        }
    }

    /// <summary>
    /// WAL entry message for replication.
    /// </summary>
    public sealed class WalEntryMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.WalEntry;

        public required long WalPosition { get; init; }
        public required byte[] WalData { get; init; }

        public override byte[] Serialize()
        {
            var payloadSize = 8 + 4 + WalData.Length; // position + dataLength + data
            var buffer = CreateHeader(Type, payloadSize);
            var offset = 17;

            BitConverter.TryWriteBytes(buffer.AsSpan(offset), WalPosition);
            offset += 8;
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), WalData.Length);
            offset += 4;
            WalData.CopyTo(buffer.AsSpan(offset));

            return buffer;
        }

        public static WalEntryMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var offset = 0;
            var walPosition = BitConverter.ToInt64(payload[offset..(offset + 8)]);
            offset += 8;
            var dataLength = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;
            var walData = payload[offset..(offset + dataLength)].ToArray();

            return new WalEntryMessage
            {
                WalPosition = walPosition,
                WalData = walData
            };
        }
    }

    /// <summary>
    /// Acknowledgment message for received data.
    /// </summary>
    public sealed class AcknowledgmentMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.Acknowledgment;

        public required long LastReceivedPosition { get; init; }

        public override byte[] Serialize()
        {
            var payloadSize = 8; // last received position
            var buffer = CreateHeader(Type, payloadSize);
            BitConverter.TryWriteBytes(buffer.AsSpan(17), LastReceivedPosition);
            return buffer;
        }

        public static AcknowledgmentMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var lastReceivedPosition = BitConverter.ToInt64(payload);
            return new AcknowledgmentMessage { LastReceivedPosition = lastReceivedPosition };
        }
    }

    /// <summary>
    /// Request for missing WAL entries.
    /// </summary>
    public sealed class CatchupRequestMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.CatchupRequest;

        public required long StartPosition { get; init; }
        public required long EndPosition { get; init; }

        public override byte[] Serialize()
        {
            var payloadSize = 8 + 8; // start + end position
            var buffer = CreateHeader(Type, payloadSize);
            var offset = 17;

            BitConverter.TryWriteBytes(buffer.AsSpan(offset), StartPosition);
            offset += 8;
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), EndPosition);

            return buffer;
        }

        public static CatchupRequestMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var startPosition = BitConverter.ToInt64(payload[0..8]);
            var endPosition = BitConverter.ToInt64(payload[8..16]);

            return new CatchupRequestMessage
            {
                StartPosition = startPosition,
                EndPosition = endPosition
            };
        }
    }

    /// <summary>
    /// Response with missing WAL entries.
    /// </summary>
    public sealed class CatchupResponseMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.CatchupResponse;

        public required WalEntryMessage[] WalEntries { get; init; }

        public override byte[] Serialize()
        {
            var entriesData = new List<byte[]>();
            var totalSize = 0;

            foreach (var entry in WalEntries)
            {
                var entryData = entry.Serialize();
                entriesData.Add(entryData);
                totalSize += 4 + entryData.Length; // length prefix + data
            }

            var payloadSize = 4 + totalSize; // entryCount + entries
            var buffer = CreateHeader(Type, payloadSize);
            var offset = 17;

            BitConverter.TryWriteBytes(buffer.AsSpan(offset), WalEntries.Length);
            offset += 4;

            foreach (var entryData in entriesData)
            {
                BitConverter.TryWriteBytes(buffer.AsSpan(offset), entryData.Length);
                offset += 4;
                entryData.CopyTo(buffer.AsSpan(offset));
                offset += entryData.Length;
            }

            return buffer;
        }

        public static CatchupResponseMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var offset = 0;
            var entryCount = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;

            var walEntries = new WalEntryMessage[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                var entryLength = BitConverter.ToInt32(payload[offset..(offset + 4)]);
                offset += 4;
                var entryData = payload[offset..(offset + entryLength)];
                offset += entryLength;

                walEntries[i] = (WalEntryMessage)ReplicationMessage.Deserialize(entryData);
            }

            return new CatchupResponseMessage { WalEntries = walEntries };
        }
    }

    /// <summary>
    /// Notification of role change.
    /// </summary>
    public sealed class RoleChangeMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.RoleChange;

        public required string NodeId { get; init; }
        public required ReplicationRole NewRole { get; init; }

        public override byte[] Serialize()
        {
            var nodeIdBytes = Encoding.UTF8.GetBytes(NodeId);
            var payloadSize = 1 + 4 + nodeIdBytes.Length; // role + nodeIdLength + nodeId

            var buffer = CreateHeader(Type, payloadSize);
            var offset = 17;

            buffer[offset++] = (byte)NewRole;
            BitConverter.TryWriteBytes(buffer.AsSpan(offset), nodeIdBytes.Length);
            offset += 4;
            nodeIdBytes.CopyTo(buffer.AsSpan(offset));

            return buffer;
        }

        public static RoleChangeMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var offset = 0;
            var newRole = (ReplicationRole)payload[offset++];
            var nodeIdLength = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;
            var nodeId = Encoding.UTF8.GetString(payload[offset..(offset + nodeIdLength)]);

            return new RoleChangeMessage
            {
                NodeId = nodeId,
                NewRole = newRole
            };
        }
    }

    /// <summary>
    /// Error message.
    /// </summary>
    public sealed class ErrorMessage : ReplicationMessage
    {
        public override MessageType Type => MessageType.Error;

        public required string ErrorCode { get; init; }
        public required string Message { get; init; }

        public override byte[] Serialize()
        {
            var errorCodeBytes = Encoding.UTF8.GetBytes(ErrorCode);
            var errorMessageBytes = Encoding.UTF8.GetBytes(Message);
            var payloadSize = 4 + errorCodeBytes.Length + 4 + errorMessageBytes.Length;

            var buffer = CreateHeader(Type, payloadSize);
            var offset = 17;

            BitConverter.TryWriteBytes(buffer.AsSpan(offset), errorCodeBytes.Length);
            offset += 4;
            errorCodeBytes.CopyTo(buffer.AsSpan(offset));
            offset += errorCodeBytes.Length;

            BitConverter.TryWriteBytes(buffer.AsSpan(offset), errorMessageBytes.Length);
            offset += 4;
            errorMessageBytes.CopyTo(buffer.AsSpan(offset));

            return buffer;
        }

        public static ErrorMessage Deserialize(ReadOnlySpan<byte> payload)
        {
            var offset = 0;
            var errorCodeLength = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;
            var errorCode = Encoding.UTF8.GetString(payload[offset..(offset + errorCodeLength)]);
            offset += errorCodeLength;

            var errorMessageLength = BitConverter.ToInt32(payload[offset..(offset + 4)]);
            offset += 4;
            var errorMessage = Encoding.UTF8.GetString(payload[offset..(offset + errorMessageLength)]);

            return new ErrorMessage
            {
                ErrorCode = errorCode,
                Message = errorMessage
            };
        }
    }
}
