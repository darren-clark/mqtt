﻿/*******************************************************************************
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
using DClark.MQTT.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DClark.MQTT
{
    public class MqttClient
    {
        private IMqttStorageProvider storageProvider;
        private IMqttSession session;
        private Stream stream;

        public MqttClient(IMqttStorageProvider storageProvider, IMqttSession session)
        {
            this.storageProvider = storageProvider;
            this.session = session;
        }

        public async Task Connect(TcpClient client, String clientId)
        {
            stream = client.GetStream();
//            ConnectMessage connect = new ConnectMessage();
        }

        public async Task Process()
        {

        }
    }
}
