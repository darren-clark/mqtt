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
    public class SessionProvider: IMqttSessionProvider
    {
        private class SubscriptionNode{
            public Dictionary<String,SubscriptionNode> children;
            public Dictionary<IMqttSession,QoS> subscribers;
        }   

        private SubscriptionNode subscriptionRoot = new SubscriptionNode(){ children = new Dictionary<string,SubscriptionNode>() };

        Task<IMqttSession> IMqttSessionProvider.NewSession(string clientId)
        {
            return Util.RunSynchronously<IMqttSession>(() => new Session(clientId));
        }

        Task IMqttSessionProvider.CloseSession(IMqttSession session)
        {
            //TODO: Clean up subscriptions if cleansession
            return Util.CompletedTask;
        }

        Task<IEnumerable<Tuple<IMqttSession, QoS>>> IMqttSessionProvider.GetSubscriptions(string topic)
        {
            List<SubscriptionNode> current = new List<SubscriptionNode>(new SubscriptionNode[] { subscriptionRoot});
            List<Tuple<IMqttSession, QoS>> results = new List<Tuple<IMqttSession, QoS>>();
            foreach (string segment in topic.Split('/'))
            {
                if (current.Count == 0) break;
                List<SubscriptionNode> next = new List<SubscriptionNode>();
                foreach(SubscriptionNode test in current){
                    SubscriptionNode node;
                    if (test.children.TryGetValue("#", out node))
                    {
                        foreach (KeyValuePair<IMqttSession, QoS> subscriber in node.subscribers)
                        {
                            results.Add(new Tuple<IMqttSession, QoS>(subscriber.Key, subscriber.Value));
                        }
                    }
                    if (test.children.TryGetValue("+", out node))
                    {
                        next.Add(node);
                    }
                    if (test.children.TryGetValue(segment, out node))
                    {
                        next.Add(node);
                    }
                }
                current = next;
            }
            foreach (SubscriptionNode node in current)
            {
                foreach (KeyValuePair<IMqttSession, QoS> subscriber in node.subscribers)
                {
                    results.Add(new Tuple<IMqttSession, QoS>(subscriber.Key, subscriber.Value));
                }
            }
            return Util.RunSynchronously<IEnumerable<Tuple<IMqttSession,QoS>>>(() => results);
        }


        private SubscriptionNode LookupNode(string topicFilter)
        {
            SubscriptionNode node = subscriptionRoot;
            foreach (string segment in topicFilter.Split('/'))
            {
                if (node.children == null) node.children = new Dictionary<string, SubscriptionNode>();
                SubscriptionNode next;
                if (!node.children.TryGetValue(segment, out next))
                {   
                    next = new SubscriptionNode();
                    node.children.Add(segment, next);
                }
                node = next;
            }
            return node;
        }

        Task<IReadOnlyCollection<QoS>> IMqttSessionProvider.AddSubscriptions(IMqttSession session, IReadOnlyDictionary<string, QoS> subscriptions)
        {
            List<QoS> result = new List<QoS>();
            foreach(KeyValuePair<string,QoS> subscription in subscriptions){
                SubscriptionNode node = LookupNode(subscription.Key);
                if (node.subscribers == null) node.subscribers = new Dictionary<IMqttSession, QoS>();
                node.subscribers[session] = subscription.Value;                 
                result.Add(subscription.Value);
            }
            return Util.RunSynchronously<IReadOnlyCollection<QoS>>(() => result);
        }


        Task IMqttSessionProvider.RemoveSubscriptions(IMqttSession session, IEnumerable<string> topicFilters)
        {
            foreach(string topicFilter in topicFilters)
            {
                SubscriptionNode node = LookupNode(topicFilter);
                if (node.subscribers != null && node.subscribers.Remove(session))
                {
                //TODO: clean up dead branches, recursively.
                //    if (node.subscribers.Count == 0 && node.children.Count == 0)
                //    {
                //        int lastSlash = topicFilter.LastIndexOf('/')
                //        string parentTopic = topicFilter.Substring(0, topicFilter.LastIndexOf('/'));
                //        SubscriptionNode parentNode = LookupNode(parentTopic);
                //        parentNode.children.Remove()
                //    }
                }
            }
            return Util.CompletedTask;
        }
    }
}
