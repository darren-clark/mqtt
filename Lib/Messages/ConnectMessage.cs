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
    public class ConnectMessage: MqttMessage
    {
        //Connect header.
        private readonly bool cleanSession;
        private readonly QoS willQoS;
        private readonly bool willRetain;
        private readonly short keepAlive;

        //Connect paylod
        private readonly String clientId;
        private readonly String willTopic;
        private readonly byte[] willMessage;
        private readonly String username;
        private readonly byte[] password;

        public bool CleanSession { get { return cleanSession; } }
        public short KeepAlive { get { return keepAlive; } }
        public String ClientId { get { return clientId;}}

        public String WillTopic { get { return willTopic; } }
        public QoS WillQos { get { return willQoS; } }
        public byte[] WillMessage { get { return willMessage; } }
        public String Username { get { return username; } }
        public byte[] Password { get { return password; } }


        internal ConnectMessage(int size, byte flags, Stream stream):base(MessageType.Connect,size,flags, stream)
        {
            if (flags != 0)
                throw new MqttProtocolException("Invalid connect flags");
            String protocolName = ReadString();
            byte protoLevel = ReadByte();
            byte connectFlags = ReadByte();
            cleanSession = (connectFlags & 0x02) == 0x02;
            bool willFlag = (connectFlags & 0x04) == 0x04;
            willQoS = (QoS)((connectFlags >> 3) & 0x03);
            willRetain = (connectFlags & 0x20) == 0x20;
            if (!willFlag && (willQoS != 0 || willRetain)) throw new MqttProtocolException("Invalid connect flags");
            bool passwordFlag = (connectFlags & 0x40) == 0x40;
            bool userFlag = (connectFlags & 0x80) == 0x80;
            keepAlive = ReadInt16();
            clientId = ReadString();
            willTopic = null;
            willMessage = null;
            if (willFlag)
            {
                willTopic = ReadString();
                willMessage = ReadBytes();
            }
            username = null;
            password = null;
            if (userFlag)
            {
                username = ReadString();
                if (passwordFlag)
                {
                    password = ReadBytes();
                }
            }
        }
        
        public ConnectMessage(string clientId, bool cleanSession, short keepAlive, string willTopic, QoS willQoS, bool willRetain, byte[] willMessage, string username, byte[] password):
            base(MessageType.Connect,
                10+ //Protocol name,level, connect flags, keepalive
                GetSize(clientId)+
                (willTopic != null ? (GetSize(willTopic)+GetSize(willMessage,true)) : 0)+
                (username != null ? GetSize(username) : 0)+
                (password != null ? GetSize(password,true) : 0),0)
        {
            this.clientId = clientId;
            this.cleanSession = cleanSession;
            this.keepAlive = keepAlive;
            this.willTopic = willTopic;
            this.willQoS = willQoS;
            this.willMessage = willMessage;
            this.willRetain = willRetain;
            this.username = username;
            this.password = password;
        }

        protected override async Task WriteMessage()
        {
            Write("MQTT"); //Protocol name 
            Write((byte) 4); //Protocol level indicates 3.1.1
            Write((byte)((username != null ? 1 << 7 : 0) | (password != null ? 1 << 6 : 0) | (willRetain ? 1 << 5 : 0) | ((byte)willQoS) << 3 | (willTopic != null ? 1 << 2 : 0) | (cleanSession ? 1 << 1 : 0)));
            Write(keepAlive);
            Write(clientId);
            if (willTopic != null)
            {
                Write(willTopic);
                await WriteAsync(willMessage, true);
            }
            if (username != null)
            {
                Write(username);
            }
            if (password != null)
            {
                await WriteAsync(password, true);
            }
        }
    }
}
