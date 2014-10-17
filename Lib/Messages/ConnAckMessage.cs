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
    public class ConnAckMessage: MqttMessage
    {
        private readonly byte returnCode;
        private readonly bool sessionPresent;

        public ConnAckMessage(byte returnCode, bool sessionPresent):base(MessageType.ConnAck,2,0)
        {
            this.returnCode = returnCode;
            this.sessionPresent = sessionPresent;
        }

        internal ConnAckMessage(int size, byte flags, Stream stream)
            : base(MessageType.ConnAck, size, flags, stream)
        {
            sessionPresent = stream.ReadByte() != 0;
            returnCode = (byte) stream.ReadByte();
        }

        protected override Task WriteMessage()
        {
            Write((byte)(sessionPresent ? 0 : 1));
            Write(returnCode);
            return base.WriteMessage(); 
        }
    }
}
