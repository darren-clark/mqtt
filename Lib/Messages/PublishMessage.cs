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
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DClark.MQTT.Messages
{
    public class PublishMessage: MqttMessage
    {
        public readonly QoS QoS;
        public readonly bool Duplicate;
        public readonly bool Retain;

        public readonly String Topic;
        public readonly Nullable<short> PacketId;

        private byte[] payload;

        public override string ToString()
        {
            return String.Format("{0},{1},{2},{3},{4},{5}", PacketId, QoS, Duplicate, Retain, Topic, payload == null ? RemainingSize : payload.Length);
        }

        public byte[] Payload
        {
            get
            {
                if (payload == null) throw new InvalidOperationException("Payload has not been read");
                return payload;
            }
        }
        internal PublishMessage(int size, byte flags, Stream stream):base(MessageType.Publish,size,flags,stream)
        {
            Duplicate = (flags & 0x08) == 0x08;
            Retain = (flags & 0x01) == 0x01;
            QoS = (QoS)((flags >> 1) & 0x03);
            if (QoS == QoS.Reserved) throw new MqttProtocolException("Invalid QoS");
            if (Duplicate && QoS == QoS.BestEffort) throw new MqttProtocolException("Duplicate with QoS 0.");
            Topic = ReadString();
            if (QoS != QoS.BestEffort)
            {
                PacketId = ReadInt16();
            }
        }

        public PublishMessage(bool duplicate, bool retain, QoS qos, String topic, byte[] message, short? packetId):
            base(MessageType.Publish,(packetId.HasValue ? 2: 0)+GetSize(topic)+GetSize(message,false),(byte) ((duplicate ? 0x08 : 0) | (retain ? 0x01 : 0) | (byte) qos << 1))
        {
            Duplicate = duplicate;
            Retain = retain;
            QoS = qos;
            Topic = topic;
            payload = message;
            PacketId = packetId;
        }

        protected override Task WriteMessage()
        {
            Write(Topic);
            if (PacketId.HasValue) Write(PacketId.Value);
            return WriteAsync(payload, false);
        }

        public Task ReadPayloadAsync()
        {
            payload = new byte[RemainingSize];
            if (RemainingSize == 0) return Util.CompletedTask;
            return ReadBufferAsync(payload);
        }
    }
}
