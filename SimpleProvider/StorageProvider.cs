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
using DClark.MQTT;
using DClark.MQTT.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DClark.MQTT.SimpleProvider
{
    public class StorageProvider : IMqttStorageProvider
    {
        private class SimpleInFlightMessage:InFlightMessage
        {
            public int refCount;

            public SimpleInFlightMessage(string topic, byte[] payload):base(topic,payload)
            {
                refCount = 1;
            }
        }

        private class RetainedNode
        {
            public byte[] body;
            public Dictionary<String, RetainedNode> children;
        }

        RetainedNode retained = new RetainedNode() { children = new Dictionary<string, RetainedNode>() };
        Dictionary<Guid, SimpleInFlightMessage> inFlight = new Dictionary<Guid, SimpleInFlightMessage>();

        Task<string> IMqttStorageProvider.StoreMessage(InFlightMessage message)
        {
            Guid messageId = Guid.NewGuid();
            inFlight.Add(messageId, new SimpleInFlightMessage(message.Topic,message.Payload));
            return Util.RunSynchronously<string>(() => messageId.ToString());
        }

        Task IMqttStorageProvider.ReleaseMessage(string messageId)
        {
            Guid messageGuid = new Guid(messageId);
            SimpleInFlightMessage message = inFlight[messageGuid];
            if (--message.refCount == 0) inFlight.Remove(messageGuid);
            return Util.CompletedTask;
        }

        Task IMqttStorageProvider.ReferenceMessage(string messageId)
        {
            Guid messageGuid = new Guid(messageId);
            SimpleInFlightMessage message = inFlight[messageGuid];
            message.refCount++;
            return Util.CompletedTask;
        }


        Task IMqttStorageProvider.PutRetained(string topic, byte[] payload)
        {
            RetainedNode node = retained;
            foreach (string fragment in topic.Split('/'))
            {
                RetainedNode next;
                if (node.children == null)
                {
                    node.children = new Dictionary<string, RetainedNode>();
                    node.children.Add(fragment, next = new RetainedNode());
                }
                else
                {
                    if (!node.children.TryGetValue(fragment, out next))
                    {
                        node.children.Add(fragment, next = new RetainedNode());
                    }
                }
                node = next;
            }
            node.body = payload;
            return Util.CompletedTask;
        }

        Task<IEnumerable<Tuple<string, QoS, byte[]>>> IMqttStorageProvider.GetRetained(IEnumerable<KeyValuePair<string, DClark.MQTT.Messages.QoS>> subscriptions)
        {
            List<Tuple<string, QoS, byte[]>> result = new List<Tuple<string, QoS, byte[]>>();
            foreach (var subscription in subscriptions)
            {
                string topicFilter = subscription.Key;
                Dictionary<string, RetainedNode> possibles = new Dictionary<string, RetainedNode>();
                possibles.Add("", retained);
                RetainedNode node = retained;
                Boolean includeChildren = false;
                foreach (string fragment in topicFilter.Split('/'))
                {
                    Dictionary<string, RetainedNode> next = new Dictionary<string, RetainedNode>();
                    if (fragment == "#" || fragment == "+")
                    {
                        foreach (var possible in possibles)
                        {
                            foreach (var child in possible.Value.children)
                            {
                                next.Add(possible.Key.Length == 0 ? child.Key : possible.Key + "/" + child.Key , child.Value);
                            }
                        }
                        includeChildren = fragment == "#";
                    }
                    else
                    {
                        foreach (var possible in possibles)
                        {
                            RetainedNode child;
                            if (possible.Value.children.TryGetValue(fragment, out child))
                            {
                                next.Add(possible.Key.Length == 0 ? fragment : possible.Key + "/" + fragment, child);
                            }
                        }
                    }
                    possibles = next;
                }
                foreach (var possible in possibles)
                {
                    if (possible.Value.body != null)
                    {
                        result.Add(new Tuple<string, QoS, byte[]>(possible.Key, subscription.Value, possible.Value.body));
                    }
                    if (includeChildren)
                    {
                        AddRetainedRecursive(possible.Key, possible.Value, subscription.Value, result);
                    }
                }
            }
            return Util.RunSynchronously<IEnumerable<Tuple<string, QoS, byte[]>>>(() => result);
        }

        void AddRetainedRecursive(string topic, RetainedNode node, QoS qos, List<Tuple<string, QoS, byte[]>> result)
        {
            if (node.children == null) return;
            foreach (var child in node.children)
            {
                string childTopic = topic + "/" + child.Key;
                if (child.Value.body != null)
                {
                    result.Add(new Tuple<string, QoS, byte[]>(childTopic, qos, child.Value.body));
                }
                AddRetainedRecursive(childTopic, child.Value, qos, result);
            }
        }


        Task<InFlightMessage> IMqttStorageProvider.GetMessage(string messageId)
        {
            Guid messageGuid = Guid.Parse(messageId);
            return Util.RunSynchronously<InFlightMessage>(() => inFlight[messageGuid]);
            throw new NotImplementedException();
        }
    }
}
