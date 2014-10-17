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

namespace DClark.MQTT
{
    public static class Util
    {
        public static async Task<int> ReadBufferAsync(this Stream stream, byte[] buffer)
        {
            return await ReadBufferAsync(stream, buffer, buffer.Length);
        }

        public static async Task<int> ReadBufferAsync(this Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, total, count-total);
                if (read == 0)
                {
                    return total;
                }
                total += read;
            }
            return total;
        }
        
        //Cute part is this is special cased in Delay to return an internal member called CompletedTask...
        public static Task CompletedTask = Task.Delay(0);

        public static Task RunSynchronously(Action action)
        {
            Task result = new Task(action);
            result.RunSynchronously();
            return result;
        }

        public static async Task<TResult> Delay<TResult>(int delayMilliseconds, TResult result)
        {
            //Console.WriteLine("Delaying {0} millis before returing {1}", delayMilliseconds, result);
            await Task.Delay(delayMilliseconds);
            //Console.WriteLine("Returing {0}", result);
            return result;
        }

        public static async Task<TResult> Delay<TResult>(int delayMilliseconds, Func<TResult> func)
        {
            //Console.WriteLine("Delaying {0} millis before executing {1}", delayMilliseconds, func);
            await Task.Delay(delayMilliseconds);
            TResult result = func();
            //Console.WriteLine("Returning {0}", result);
            return result;
        }

        public static Task<TResult> RunSynchronously<TResult>(Func<TResult> func)
        {
            Task<TResult> result = new Task<TResult>(func);
            result.RunSynchronously();
            return result;
        }
    }
}
