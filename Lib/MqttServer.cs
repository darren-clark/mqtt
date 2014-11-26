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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DClark.MQTT
{
    public class Server
    {
        private TcpListener listener;
        private Dictionary<IMqttSession, TcpClient> connections = new Dictionary<IMqttSession, TcpClient>();
        private IMqttSessionProvider sessionProvider;
        private IMqttStorageProvider storageProvider;

        public Server(IMqttSessionProvider sessionProvider, IMqttStorageProvider storageProvider)
        {
            this.sessionProvider = sessionProvider;
            this.storageProvider = storageProvider;
        }


        private class SingleContext: SynchronizationContext{

            private readonly BlockingCollection<Tuple<SendOrPostCallback, object>> workQueue =
                 new BlockingCollection<Tuple<SendOrPostCallback, object>>();

            public override void Post(SendOrPostCallback d, object state)
            {
                workQueue.Add(new Tuple<SendOrPostCallback, object>(d, state));
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotImplementedException();
            }

            public void Run()
            {
                foreach (var workItem in workQueue.GetConsumingEnumerable())
                {
                    workItem.Item1(workItem.Item2);
                }
            }

            public void Complete()
            {
                workQueue.CompleteAdding();
            }
        }

        private SingleContext singleContext;

        public void Run()
        {
            var previousContext = SynchronizationContext.Current;
            try
            {
                singleContext = new SingleContext();
                SynchronizationContext.SetSynchronizationContext(singleContext);
                var execution = Start();
                execution.ContinueWith(delegate { singleContext.Complete(); }, TaskScheduler.Default);
                singleContext.Run();
                execution.Wait();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        private Boolean running;
        public void Stop()
        {
            running = false;
            singleContext.Complete();
        }
        private async Task Start()
        {
            running = true;
            listener = new TcpListener(IPAddress.Any, 1883);
            listener.Start();
            while (running)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                handleConnection(client);
            }
        }

        private async void handleConnection(TcpClient connection)
        {
            var ep = connection.Client.RemoteEndPoint;
            Console.WriteLine("New Conection from {0}", ep);
            NetworkStream stream = connection.GetStream();
            MqttMessage message = await MqttMessage.Read(stream, 5);
            if (message.Type != MessageType.Connect) throw new MqttProtocolException("First packet not connect");
            //TODO: Non-clean sessions
            ConnAckMessage connAck = new ConnAckMessage(0, false);
            await connAck.Write(stream);
            ConnectMessage connectMessage = (ConnectMessage) message;
            double keepalive = ((ConnectMessage)message).KeepAlive*1.5;
            String clientId = ((ConnectMessage)message).ClientId;
            //Console.WriteLine("Client {0} connected", clientId);
            IMqttSession session = await sessionProvider.NewSession(clientId);
            Console.WriteLine("Client {0} connected from {1} ({2},{3})", clientId, ep,connectMessage.CleanSession,connectMessage.KeepAlive);
            connections.Add(session, connection);
            Task<MqttMessage> incoming = MqttMessage.Read(stream);
            Task<PendingMessage> outgoing = session.NextPending(null,0);
            while (connection.Connected)
            {
                try
                {
                    if (await Task.WhenAny(incoming, outgoing) == incoming)
                    {
                        try
                        {
                            message = incoming.Result;
                            //Console.WriteLine("Received {0} from {1}", message,clientId);
                        }
                        catch (AggregateException e)
                        {
                            Console.WriteLine(e.InnerException.Message);
                            connections.Remove(session);
                            connection.Close();
                            continue;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            connections.Remove(session);
                            connection.Close();
                            continue;
                        }
                        switch (message.Type)
                        {
                            case MessageType.Publish:
                                Console.WriteLine("Publish received from {0} ({1})", clientId, message);
                                await PublishReceived(session, stream, (PublishMessage)message);
                                break;
                            case MessageType.Disconnect:
                                Console.WriteLine("Client {0} disconnected cleanly", clientId);
                                connections.Remove(session);
                                connection.Close();
                                //Skip reading the next message. Loop should exit.
                                continue;
                            case MessageType.Subscribe:
                                await SubscribeReceived(session, stream, (SubscribeMessage)message);
                                break;
                            case MessageType.Unsubscribe:
                                await sessionProvider.RemoveSubscriptions(session, ((UnsubscribeMessage)message).TopicFilters);
                                await new UnsubAckMessage(((UnsubscribeMessage)message).PacketId).Write(stream);
                                break;
                            case MessageType.PubAck:
                                string messageId = await session.PublishAcknowledged(((PubAckMessage)message).PacketId);
                                if (messageId != null)
                                    await storageProvider.ReleaseMessage(messageId);
                                break;
                            case MessageType.PubRec:
                                messageId = await session.PublishReceived(((PubRecMessage)message).PacketId);
                                if (messageId != null)
                                    await storageProvider.ReleaseMessage(messageId);
                                break;
                            case MessageType.PubRel:
                                await session.RemoveQoS2(((PubRelMessage)message).PacketId);
                                await new PubCompMessage(((PubRelMessage)message).PacketId).Write(stream);
                                break;
                            case MessageType.PubComp:
                                await session.PublishCompleted(((PubCompMessage)message).PacketId);
                                break;
                            case MessageType.PingReq:
                                await new PingRespMessage().Write(stream);
                                break;
                        }
                        incoming = MqttMessage.Read(stream);
                    }
                    else
                    {
                        PendingMessage pendingMessage = outgoing.Result;
                        //Console.WriteLine("Sending {0} to {1}", pendingMessage, clientId);
                        PendingPublishMessage pendingPublish = pendingMessage as PendingPublishMessage;
                        if (pendingPublish != null)
                        {
                            var messageInfo = await storageProvider.GetMessage(pendingPublish.MessageId);
                            message = new PublishMessage(pendingPublish.Duplicate, false, pendingPublish.QoS, messageInfo.Topic, messageInfo.Payload, pendingPublish.PacketId);
                            //Console.WriteLine("Sending publish message {0} on {1} to {2} with QoS {3}", Encoding.UTF8.GetString(messageInfo.Payload), messageInfo.Topic, clientId, pendingPublish.QoS);
                        }
                        else
                        {
                            message = new PubRelMessage(pendingMessage.PacketId);
                        }
                        await message.Write(stream);
                        outgoing = session.NextPending(pendingMessage, 5000);
                    }
                }
                catch (Exception e)
                {
                    Console.Out.WriteLine(e.Message);
                }
            }
            await sessionProvider.CloseSession(session);
        }

        private async Task SubscribeReceived(IMqttSession session, NetworkStream stream, SubscribeMessage subscribeMessage)
        {
            //TODO: Deal with subscriptions that are denied.
            SubAckMessage subAck = new SubAckMessage(subscribeMessage.PacketId, await sessionProvider.AddSubscriptions(session, subscribeMessage.Subscriptions));
            await subAck.Write(stream);
            await PublishMessages(session, stream, await storageProvider.GetRetained(subscribeMessage.Subscriptions),true);
        }


        internal async Task PublishReceived(IMqttSession session, Stream stream, PublishMessage publishMessage)
        {
            await publishMessage.ReadPayloadAsync();
            //Console.WriteLine("{0} published {1} to {2} with QoS {3}", session.ClientId, Encoding.UTF8.GetString(publishMessage.Payload), publishMessage.Topic, publishMessage.QoS);
            //If we've already received the QoS 2 message and forwarded it, then just send the PubRec
            if (publishMessage.QoS == QoS.AtMostOnce && await session.HasQoS2(publishMessage.PacketId.Value))
            {
                await new PubRecMessage(publishMessage.PacketId.Value).Write(stream);
                return;
            }
            if (publishMessage.Retain) await storageProvider.PutRetained(publishMessage.Topic, publishMessage.Payload);
            await PublishMessage(await sessionProvider.GetSubscriptions(publishMessage.Topic),publishMessage.Topic,publishMessage.Payload);
            switch(publishMessage.QoS){
                case QoS.AtLeastOnce:
                    await new PubAckMessage(publishMessage.PacketId.Value).Write(stream);
                    break;
                case QoS.AtMostOnce:
                    await session.StoreQoS2(publishMessage.PacketId.Value);
                    await new PubRecMessage(publishMessage.PacketId.Value).Write(stream);
                    break;
                case QoS.BestEffort:
                default:
                    break;
            }
            //Console.WriteLine("Received " + Encoding.UTF8.GetString(publishMessage.Payload) + " on topic " + publishMessage.Topic);
        }

        private async Task PublishMessages(IMqttSession session, Stream stream, IEnumerable<Tuple<string, QoS, byte[]>> messages, bool retained)
        {
            foreach (var message in messages)
            {
                string messageId = null;
                short? packetId = null;
                //QOS 1 or 2, store in storage, and in session.
                if (message.Item2 != QoS.BestEffort)
                {
                    messageId = await storageProvider.StoreMessage(new InFlightMessage(message.Item1, message.Item3));
                    session.Publish(messageId, message.Item2);
                }
                else
                {
                    //QoS 0 just publish, that way the session can keep a straight up queue and not block QoS 0 messages from
                    //intervening.
                    PublishMessage publishMessage = new PublishMessage(false, retained, message.Item2, message.Item1, message.Item3, packetId);
                    await publishMessage.Write(stream);
                }
            }
        }

        private async Task PublishMessage(IEnumerable<Tuple<IMqttSession,QoS>> subscriptions, String topic, byte[] payload)
        {
            String messageId = null; //Used to for non QoS 0 messages, to store message for sending.
            PublishMessage message = null; //Used for QoS 0 messages, to send the message. Can reuse since there is no message identifier.
            foreach (Tuple<IMqttSession, QoS> subscription in subscriptions)
            {
                //Persist a reference and queue if QoS > 0
                if (subscription.Item2 != QoS.BestEffort)
                {
                    //Save the message if we haven't already.
                    if (messageId == null)
                    {
                        messageId = await storageProvider.StoreMessage(new InFlightMessage(topic, payload));
                    }
                    else
                    {
                        await storageProvider.ReferenceMessage(messageId);
                    }
                    //Console.WriteLine("Publishing {0} on {1} to {2} with QoS {3}", Encoding.UTF8.GetString(payload), topic, subscription.Item1.ClientId, subscription.Item2);
                    subscription.Item1.Publish(messageId, subscription.Item2);
                }
                else
                {
                    TcpClient subscriber;
                    //Non-Clean subscribers won't have a TcpConnection. Currently we drop messages to QoS 0 subscriptions on non-clean connection.
                    if (connections.TryGetValue(subscription.Item1, out subscriber))
                    {
                        if (message == null) message = new PublishMessage(false, false, QoS.BestEffort, topic, payload, null);
                        try
                        {
                            await message.Write(subscriber.GetStream());
                        }
                        catch (Exception)
                        {
                            //Close socket?
                        }
                    }
                }
            }
        }
    }
}
