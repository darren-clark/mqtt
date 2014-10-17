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
using DClark.MQTT.SimpleProvider;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DClark.MQTT.Broker
{
    class Program
    {
        static void Main(string[] args)
        {
            int workerThreads;
            int completionPortThreads;
            Server server = new Server(new SessionProvider(), new StorageProvider());
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs eventArgs)
            {
                if (eventArgs.SpecialKey == ConsoleSpecialKey.ControlC) {
                    server.Stop();
                    eventArgs.Cancel = true;

                    ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);
                    Console.WriteLine("Avail Thread: {0} worker, {1} I/O", workerThreads, completionPortThreads);
                }
            };
//            ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);
            server.Run();
        }
    }
}
