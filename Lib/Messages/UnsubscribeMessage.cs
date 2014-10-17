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
    public class UnsubscribeMessage: MqttMessage
    {
        public readonly short PacketId;
        public readonly IReadOnlyList<string> TopicFilters;

        internal UnsubscribeMessage(int size, byte flags, Stream stream):
            base(MessageType.Unsubscribe, size, flags, stream)
        {
            if (flags != 2) throw new MqttProtocolException("Invalid flags");
            PacketId = ReadInt16();
            var topicFilters = new List<string>();
            while (RemainingSize > 0)
            {
                topicFilters.Add(ReadString());
            }
            TopicFilters = topicFilters;
        }

        public UnsubscribeMessage(short packetId, IReadOnlyList<string> topicFilters):base(MessageType.Unsubscribe,GetSize(topicFilters),2)
        {
            this.PacketId = packetId;
            this.TopicFilters = topicFilters;
        }

        private static int GetSize(IReadOnlyList<string> topicFilters)
        {
            int size = 2; //packetId
            foreach (var topicFilter in topicFilters)
            {
                size += GetSize(topicFilter);
            }
            return size;
        }
    }
}
