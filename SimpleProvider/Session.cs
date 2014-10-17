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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace DClark.MQTT.SimpleProvider
{
    public class Session: IMqttSession
    {
        private readonly String clientId;
        private HashSet<short> pendingQoS2 = new HashSet<short>();
        private Queue<PendingMessage> pendingMessages = new Queue<PendingMessage>();
        private TaskCompletionSource<PendingMessage> pendingMessageCompletionSource;

        internal Session(String clientId)
        {
            this.clientId = clientId;
        }

        Task IMqttSession.StoreQoS2(short packetId)
        {
            pendingQoS2.Add(packetId);
            return Util.CompletedTask;
        }

        Task<bool> IMqttSession.HasQoS2(short packetId)
        {
            return Util.RunSynchronously<bool>(() => pendingQoS2.Contains(packetId));
        }

        Task IMqttSession.RemoveQoS2(short packetId)
        {
            pendingQoS2.Remove(packetId);
            return Util.CompletedTask;
        }

        private PendingMessage lastMessage;
        private T DequeueMessage<T>(short packetId) where T : PendingMessage 
        {
            if (lastMessage is T && lastMessage.PacketId == packetId) return null;

            lastMessage = pendingMessages.Dequeue();
            Debug.Assert(lastMessage is T && lastMessage.PacketId == packetId);
            //Signal we still have messages if someone is waiting.
            if (pendingMessages.Count > 0 && pendingMessageCompletionSource != null)
            {
                TaskCompletionSource<PendingMessage> source = pendingMessageCompletionSource;
                pendingMessageCompletionSource = null;
                source.SetResult(pendingMessages.Peek());
            }
            return (T)lastMessage;
        }

        private Task<string> HandlePubResponse(short packetId)
        {

            PendingPublishMessage pendingMessage = DequeueMessage<PendingPublishMessage>(packetId);
            string result = pendingMessage == null ? null : ((PendingPublishMessage)pendingMessage).MessageId; 
            return Util.RunSynchronously<string>(() => result);
        }

        Task<string> IMqttSession.PublishAcknowledged(short packetId)
        {
            return HandlePubResponse(packetId);
        }

        Task<string> IMqttSession.PublishReceived(short packetId)
        {
            return HandlePubResponse(packetId);
        }

        Task IMqttSession.PublishCompleted(short packetId)
        {
            PendingPubRelMessage pendingMessage = DequeueMessage<PendingPubRelMessage>(packetId);
            Debug.Assert(1 == packetId);
            return Util.CompletedTask;
        }

        string IMqttSession.ClientId
        {
            get { return clientId; }
        }

        async Task<PendingMessage> IMqttSession.NextPending(PendingMessage lastMessage, int timeoutMilliseconds)
        {
            if (pendingMessages.Count > 0 && pendingMessages.Peek() != lastMessage)
            {
                //Console.WriteLine("New message pending: {0}", pendingMessages.Peek());
                return pendingMessages.Peek();
            }
            else
            {
                if (pendingMessageCompletionSource == null)
                    pendingMessageCompletionSource = new TaskCompletionSource<PendingMessage>();
                else
                    Debug.Assert(false);
                TaskCompletionSource<PendingMessage> currentCompletion = pendingMessageCompletionSource;
                if (pendingMessages.Count == 0)
                {
                    //Console.WriteLine("No pending messages.");
                    return await pendingMessageCompletionSource.Task;
                }
                else
                {
                    //Console.WriteLine("Duplicate message to send. Waiting.");
                    Task delayTask = Task.Delay(timeoutMilliseconds);
                    if (await Task.WhenAny(delayTask, pendingMessageCompletionSource.Task) == delayTask)
                    {
                        if (pendingMessages.Count > 0 && pendingMessages.Peek() == lastMessage)
                        {
                            pendingMessageCompletionSource = null;
                            return lastMessage;
                        }
                    }
                    return await pendingMessageCompletionSource.Task;
                }
            }
        }


        void IMqttSession.Publish(string messageId, QoS qos)
        {
            var publish = new PendingPublishMessage(1, messageId, qos);
            if (pendingMessages.Count == 0 && pendingMessageCompletionSource != null)
            {
                TaskCompletionSource<PendingMessage> source = pendingMessageCompletionSource;
                pendingMessageCompletionSource = null;
                source.SetResult(publish);
            }
            pendingMessages.Enqueue(publish);
            if (qos == QoS.AtMostOnce)
            {
                pendingMessages.Enqueue(new PendingPubRelMessage(1));
            }
        }
    }
}
