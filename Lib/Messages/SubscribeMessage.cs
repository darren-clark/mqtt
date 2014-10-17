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
    public class SubscribeMessage: MqttMessage
    {
        public readonly short PacketId;
        public readonly IReadOnlyDictionary<String, QoS> Subscriptions;

        internal SubscribeMessage(int size, byte flags, Stream stream):base(MessageType.Subscribe,size,flags,stream)
        {
            if (flags != 2) throw new MqttProtocolException("Invalid flags");
            Dictionary<string, QoS> subscriptions = new Dictionary<string, QoS>();
            PacketId = ReadInt16();
            while(RemainingSize > 0){
                String topicFilter = ReadString();
                QoS qos = (QoS) ReadByte();
                if (qos > QoS.AtMostOnce) throw new MqttProtocolException("Invalid subscription QoS");
                subscriptions.Add(topicFilter, (QoS)qos);
            }
            this.Subscriptions = subscriptions;
        }

        private static int GetSize(IEnumerable<string> topics)
        {
            int size =2;//PacketId
            foreach (var topic in topics)
            {
                size += GetSize(topic);
                size += 1;//QoS
            }
            return size;
        }

        public SubscribeMessage(short packetId, IReadOnlyDictionary<string, QoS> subscriptions):base(MessageType.Subscribe,GetSize(subscriptions.Keys),2)
        {
            this.PacketId = packetId;
            this.Subscriptions = subscriptions;
        }

        protected override Task WriteMessage()
        {
            Write(PacketId);
            foreach (var subscription in Subscriptions)
            {
                Write(subscription.Key);
                Write((byte)subscription.Value);
            }
            return base.WriteMessage();
        }
    }
}
