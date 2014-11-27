/*******************************************************************************
 * Copyright 2014 Darren Clark
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 ******************************************************************************/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DClark.MQTT.Messages
{
    public enum MessageType : byte
    {
        Connect = 1,
        ConnAck = 2,
        Publish = 3,
        PubAck = 4,
        PubRec = 5,
        PubRel = 6,
        PubComp = 7,
        Subscribe = 8,
        SubAck = 9,
        Unsubscribe = 10,
        UnsubAck = 11,
        PingReq = 12,
        PingResp = 13, 
        Disconnect = 14
    }

    public enum QoS : byte
    {
        BestEffort = 0,
        AtLeastOnce = 1,
        AtMostOnce = 2,
        Reserved  = 3,
        Failure = 0x80 //used in suback
    }

    public abstract class MqttMessage
    {
        private readonly MessageType type;
        private readonly byte flags;
        private readonly int size;
        private Stream stream;

        private int remainingSize;
        protected int RemainingSize { get { return remainingSize; } }
        internal MqttMessage(MessageType type, int size, byte flags, Stream stream)
        {
            Debug.Assert((flags & 0xF0) == 0, "Invalid flag value");
            this.type = type;
            this.size = size;
            this.flags = flags;
            this.stream = stream;
            this.remainingSize = size;
        }

        internal MqttMessage(MessageType type, int size, byte flags)
        {
            this.type = type;
            this.size = size;
            this.flags = flags;
        }

        public MessageType Type { get { return type; } }

        private static async Task<MqttMessage> Read(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[2];
            int read = await stream.ReadAsync(buffer, 0, 2, cancellationToken);
            if (read != 2) throw new MqttException("Socket closed");
            MessageType type = (MessageType)(buffer[0] >> 4);
            int sizeDigit = buffer[1];
            int size = sizeDigit & 0x7F;
            int digitSize = 128;
            while ((sizeDigit & 0x80) != 0)
            {
                sizeDigit = stream.ReadByte();
                size += (sizeDigit & 0x7F) * digitSize;
                digitSize *= 128;
            }

            switch (type)
            {
                case MessageType.Connect:
                    return new ConnectMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.ConnAck:
                    return new ConnAckMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.Publish:
                    return new PublishMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.PubAck:
                    return new PubAckMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.PubRec:
                    return new PubRecMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.PubRel:
                    return new PubRelMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.PubComp:
                    return new PubCompMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.Subscribe:
                    return new SubscribeMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.SubAck:
                    return new SubAckMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.Unsubscribe:   
                    return new UnsubscribeMessage(size, (byte)(buffer[0] & 0x0F),stream);
                case MessageType.UnsubAck:
                    return new UnsubAckMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.PingReq:
                    return new PingReqMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.PingResp:
                    return new PingRespMessage(size, (byte)(buffer[0] & 0x0F), stream);
                case MessageType.Disconnect:
                    return new DisconnectMessage(size, (byte)(buffer[0] & 0x0F), stream);
                default:
                    throw new NotImplementedException("Unknown message type " + type.ToString());
            }
        }

        public static Task<MqttMessage> Read(Stream stream)
        {
            return Read(stream, CancellationToken.None);
        }

        public static Task<MqttMessage> Read(Stream stream, double timeoutSeconds)
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            return Read(stream,cts.Token);
        }

        protected byte ReadByte()
        {
            remainingSize--;
            return (byte) stream.ReadByte();
        }

        protected short ReadInt16()
        {
            remainingSize -= 2;
            return (short) ((stream.ReadByte() << 8) | stream.ReadByte());
        }

        protected String ReadString()
        {
            short length = ReadInt16();
            if (length == 0) return "";
            remainingSize -= (length);
            byte[] buffer = new byte[length];
            stream.Read(buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);            
        }

        protected byte[] ReadBytes()
        {
            short length = ReadInt16();
            byte[] result = new byte[length];
            stream.Read(result, 0, length);
            return result;
        }

        protected Task ReadBufferAsync(byte[] buffer)
        {
            return stream.ReadBufferAsync(buffer);
        }

        protected int ReadBuffer(byte[] buffer)
        {
            return stream.Read(buffer, 0, buffer.Length);
        }

        public Task Write(Stream stream)
        {
            this.stream = stream;
            byte[] header;
            
            //Size should've been validated by now.
            if (size <= 127) header = new byte[2];
            else if (size <= 16383) header = new byte[3];
            else if (size <= 2097151) header = new byte[4];
            else if (size <= 268435455) header = new byte[5];
            else throw new MqttProtocolException("Invalid packet size");

            header[0] = (byte) (((byte) type << 4) | flags);
            int left = size;
            for (int i=1;i<header.Length;i++){
                header[i] = (byte) (left % 128);
                left = left / 128;
            }
            stream.Write(header, 0, header.Length);
            remainingSize = size;
            return WriteMessage();
        }

        protected virtual Task WriteMessage()
        {
            return Util.CompletedTask;
        }

        private void VerifySize(int writing)
        {
            if (remainingSize < writing)
            {
                Debug.Assert(false);
            }
            Debug.Assert(remainingSize >= writing);
            remainingSize -= writing;
        }

        protected void Write(byte value)
        {
            VerifySize(1);
            stream.WriteByte(value);
        }

        protected void Write(short value)
        {
            VerifySize(2);
            stream.WriteByte((byte) (value >> 8));
            stream.WriteByte((byte)value);
        }

        protected void Write(string value)
        {
            if (value == null)
            {
                Write((short)0);
            }
            else
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                Write(bytes, true);
            }
        }

        protected void Write(byte[] data)
        {
            Write(data, false);
        }

        protected void Write(byte[] data, bool includeLength)
        {
            if (includeLength)
            {
                Write((short)data.Length);
            }
            VerifySize(data.Length);
            stream.Write(data, 0, data.Length);
        }

        protected Task WriteAsync(byte[] data, bool includeLength)
        {
            if (includeLength)
            {
                VerifySize(2);
                Write((short)data.Length);
            }
            VerifySize(data.Length);
            return stream.WriteAsync(data, 0, data.Length);
        }

        protected static int GetSize(byte[] value, bool includeLength)
        {
            return (includeLength ? 2 : 0) + value.Length;
        }

        protected static int GetSize(String value)
        {
            if (value == null) return 2;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return GetSize(bytes,true);
        }

        public override string ToString()
        {
            return String.Format("{0}({1} bytes) f:0x{2:X2}",GetType().Name,size,flags);
        }
    }
}
