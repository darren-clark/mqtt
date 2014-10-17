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
using System.Text;
using System.Threading.Tasks;

namespace DClark.MQTT.Messages
{
    public abstract class PubWorkMessage : MqttMessage
    {
        public readonly short PacketId;

        internal PubWorkMessage(MessageType messageType, short packetId, byte flags):base(messageType,2,flags)
        {
            PacketId = packetId;
        }

        internal PubWorkMessage(MessageType messageType, int size, byte flags, Stream stream)
            : base(messageType, size, flags, stream)
        {
            PacketId = ReadInt16();
        }

        protected override Task WriteMessage()
        {
            Write(PacketId);
            return base.WriteMessage();
        }
    }

    public class PubAckMessage: PubWorkMessage
    {

        internal PubAckMessage(int size, byte flags, Stream stream):base(MessageType.PubAck,size,flags,stream){
            if (flags != 0) throw new MqttProtocolException("Invalid flags");
        }

        public PubAckMessage(short packetId)
            : base(MessageType.PubAck, packetId,0)
        {

        }

    }

    public class PubRecMessage : PubWorkMessage
    {
        internal PubRecMessage(int size, byte flags, Stream stream):base(MessageType.PubRec,size,flags,stream){
            if (flags != 0) throw new MqttProtocolException("Invalid flags");
        }

        public PubRecMessage(short packetId)
            : base(MessageType.PubRec, packetId,0)
        {

        }
    }

    public class PubRelMessage : PubWorkMessage
    {
        public PubRelMessage(short packetId)
            : base(MessageType.PubRel, packetId,2)
        {
        }

        internal PubRelMessage(int size, byte flags, Stream stream):base(MessageType.PubRel,size,flags,stream)
        {
  //          if (flags != 2) throw new MqttException("Invalid flags");
        }
    }

    public class PubCompMessage : PubWorkMessage
    {
        public PubCompMessage(short packetId):
            base(MessageType.PubComp, packetId,0)
        {
        }

        internal PubCompMessage(int size, byte flags, Stream stream):base(MessageType.PubComp,size,flags,stream){
            if (flags != 0) throw new MqttProtocolException("Invalid flags");
        }
    }
}
