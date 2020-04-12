﻿using iMobileDevice;
using iMobileDevice.iDevice;
using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Brokenithm_Evolved_iOS
{
    class Program
    {
        public static Dictionary<string, iDeviceHandle> device_map = new Dictionary<string, iDeviceHandle>();
        public static Dictionary<string, iDeviceConnectionHandle> connection_map = new Dictionary<string, iDeviceConnectionHandle>();
        public static IiDeviceApi idevice;
        public static MemoryMappedFile sharedBuffer;
        public static MemoryMappedViewAccessor sharedBufferAccessor;
        public static bool exiting = false;
        public static iDeviceEventCallBack _eventCallback;

        
        public static byte[] ledRgb;
        public static byte[] input = new byte[32];

        static void Main(string[] args)
        {
            Console.Title = "Brokenithm-Evolved-iOS";
            Console.WriteLine("=================================================");
            Console.WriteLine("=             Brokenithm-Evolved-iOS:           =");
            Console.WriteLine("=  Brokenithm with full IO and USB connection   =");
            Console.WriteLine("=                v0.3 by esterTion              =");
            Console.WriteLine("=              Original: thebit.link            =");
            Console.WriteLine("=================================================");
            Console.WriteLine("");

            NativeLibraries.Load();
            idevice = LibiMobileDevice.Instance.iDevice;
            iDeviceError status;
            _eventCallback = new iDeviceEventCallBack(eventCallback);
            status = LibiMobileDevice.Instance.iDevice.idevice_event_subscribe(_eventCallback, IntPtr.Zero);

            MemoryMappedFileSecurity CustomSecurity = new MemoryMappedFileSecurity();
            SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var acct = sid.Translate(typeof(NTAccount)) as NTAccount;
            CustomSecurity.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(acct.ToString(), MemoryMappedFileRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            sharedBuffer = MemoryMappedFile.CreateOrOpen("Local\\BROKENITHM_SHARED_BUFFER", 1024, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, CustomSecurity, System.IO.HandleInheritability.Inheritable);
            sharedBufferAccessor = sharedBuffer.CreateViewAccessor();

            {
                Thread ledThread = new Thread(new ThreadStart(outputLed));
                ledThread.Start();
            }

            Console.WriteLine("Waiting for device...");

#if DEBUG
            //var form = new Form1();
            //form.Show();
#endif

            while (Console.ReadKey().Key != ConsoleKey.Q) { }
            exiting = true;
        }

        public static void eventCallback(ref iDeviceEvent e, IntPtr user_data)
        {
            iDeviceError status;
            string udid = e.udidString;
            switch (e.@event)
            {
                case iDeviceEventType.DeviceAdd:
                    {
                        Console.WriteLine(string.Format("device add\tudid: {0}", udid));
                        if (device_map.ContainsKey(udid))
                        {
                            return;
                        }
                        iDeviceHandle device;
                        status = idevice.idevice_new(out device, e.udidString);
                        if (status != iDeviceError.Success)
                        {
                            Console.WriteLine("Create device failed: {0}", status);
                            return;
                        }
                        device_map[udid] = device;

                        Thread thread = new Thread(new ParameterizedThreadStart(connectDevice));
                        thread.Start(udid);
                        break;
                    }
                case iDeviceEventType.DevicePaired:
                    {
                        Console.WriteLine(string.Format("device paired\tudid: {0}", udid));
                        break;
                    }
                case iDeviceEventType.DeviceRemove:
                    {
                        Console.WriteLine(string.Format("device remove\tudid: {0}", udid));
                        if (device_map.ContainsKey(udid))
                        {
                            iDeviceHandle device = device_map[udid];
                            device.Dispose();
                            device_map.Remove(udid);
                        }
                        break;
                    }
            }
        }

        public static void connectDevice(object arg)
        {
            string udid = (string)arg;
            if (!device_map.ContainsKey(udid))
            {
                return;
            }
            iDeviceHandle device = device_map[udid];
            iDeviceConnectionHandle conn;
            iDeviceError status;
            status = idevice.idevice_connect(device, 24864, out conn);
            if (status != iDeviceError.Success)
            {
                //Console.WriteLine(string.Format("connect failed: {0}", status));
                Thread.Sleep(1000);
                Thread thread = new Thread(new ParameterizedThreadStart(connectDevice));
                thread.Start(udid);
                return;
            }

            byte[] buf = new byte[256];
            uint read = 0;
            status = idevice.idevice_connection_receive(conn, buf, 4, ref read);
            if (status != iDeviceError.Success)
            {
                Console.WriteLine(string.Format("receive data failed: {0}", status));
                return;
            }
            if (
                buf[0] != 3 ||
                buf[1] != 'W' ||
                buf[2] != 'E' ||
                buf[3] != 'L'
                )
            {
                // welcome message
                Console.WriteLine("received invalid data");
                conn.Dispose();
                return;
            }
            Console.WriteLine("connected to device");
            connection_map[udid] = conn;
            {
                Thread thread = new Thread(new ParameterizedThreadStart(readInputFromDevice));
                thread.Start(udid);
            }
            return;
        }
        public static void readInputFromDevice(object arg)
        {
            string udid = (string)arg;
            if (!connection_map.ContainsKey(udid))
            {
                return;
            }
            iDeviceConnectionHandle conn = connection_map[udid];
            iDeviceError status;
            bool airEnabled = true;

            byte[] buf = new byte[256];
            uint read = 0;
            while (true)
            {
                if (exiting) break;
                status = idevice.idevice_connection_receive_timeout(conn, buf, 1, ref read, 5);
                if (status != iDeviceError.Success)
                {
                    if (status == iDeviceError.Timeout)
                    {
                        continue;
                    }
                    break;
                }
                byte len = buf[0];
                status = idevice.idevice_connection_receive_timeout(conn, buf, len, ref read, 5);
                if (status != iDeviceError.Success)
                {
                    break;
                }
                if (len >= 3+6+32 &&
                    buf[0] == 'I' &&
                    buf[1] == 'N' &&
                    buf[2] == 'P')
                {
                    // key input
                    if (airEnabled)
                    {
                        Array.Copy(buf, 9, input, 0, 32);
                        //sharedBufferAccessor.WriteArray<byte>(0, buf, 3, 6 + 32);
                    }
                    else
                    {
                        Array.Copy(buf, 9, input, 0, 32);
                        //sharedBufferAccessor.WriteArray<byte>(6, buf, 3 + 6, 32);
                    }
                    if (len > 3 + 6 + 32)
                    {
                        Array.Copy(buf, 3+6+32, input, 0, 32);
                        //sharedBufferAccessor.WriteArray<byte>(6+32+96, buf, 3+6+32, len - (3 + 6 + 32));
                    }
                }
                else if (len >= 4 &&
                  buf[0] == 'A' &&
                  buf[1] == 'I' &&
                  buf[2] == 'R')
                {
                    // air input control
                    airEnabled = buf[3] != 0;
                    Console.WriteLine(string.Format("Air input {0}", airEnabled ? "enabled" : "disabled"));
                }
                else if (len >= 4 &&
                    buf[0] == 'F' &&
                    buf[1] == 'N' &&
                    buf[2] == 'C')
                {
                    // function key
                    if (buf[3] == (byte)BNI_FUNCTION_KEYS.COIN)
                    {
                        sharedBufferAccessor.Write(6 + 32 + 96 + 2, 1);
                    }
                    else if (buf[3] == (byte)BNI_FUNCTION_KEYS.CARD)
                    {
                        sharedBufferAccessor.Write(6 + 32 + 96 + 3, 1);
                    }
                }
                else
                {
                    if (len >= 3)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append((char)buf[0]);
                        sb.Append((char)buf[1]);
                        sb.Append((char)buf[2]);
                        Console.WriteLine(string.Format("unknown packet: {0}", sb.ToString()));
                    }
                    else
                    {
                        Console.WriteLine("unknown packet");
                    }
                }
#if DEBUG
                foreach (var a in input)
                {
                    if (a == 0) continue;
                    Console.Write((char)a);
                }
#endif
            }
            conn.Dispose();
            connection_map.Remove(udid);
            Console.WriteLine("disconnected");
            if (exiting) return;
            Thread.Sleep(1000);
            {
                Thread thread = new Thread(new ParameterizedThreadStart(connectDevice));
                thread.Start(udid);
            }
        }

        enum BNI_FUNCTION_KEYS {
            COIN = 1,
            CARD
        };

        private static byte[] prevLedRgb = new byte[32 * 3];
        private static int skipCount = 0;
        public static void outputLed()
        {
            if (ledRgb == null)
            {
                ledRgb = new byte[32 * 3];
                for(int i = 0; i != 32; i++)
                {
                    ledRgb[i * 3 + 0] = 0x99;//b
                    ledRgb[i * 3 + 1] = 0xCC;//r
                    ledRgb[i * 3 + 2] = 0xFF;//g
                }
                for(int i = 0; i != 3; i++)
                {
                    ledRgb[12 + i * 12 * 3 + 2] = 0xCC;//g
                    ledRgb[12 + i * 12 * 3 + 0] = 0xFF;//b
                }
            }
            while (true)
            {
                if (exiting) break;
                //byte[] ledRgb = new byte[32 * 3];
                //sharedBufferAccessor.ReadArray<byte>(6 + 32, ledRgb, 0, 32 * 3);
                //CCFF99(204 255 153),CCCCFF(204 204 255)
                bool same = true;
                for (int i = 0; i < 32 * 3; i++)
                {
                    if (ledRgb[i] != prevLedRgb[i])
                    {
                        same = false;
                        break;
                    }
                }
                if (!same)
                {
                    BroadcastLEDStatus(ledRgb);
                    prevLedRgb = ledRgb;
                    skipCount = 0;
                }
                else
                {
                    if (++skipCount > 50)
                    {
                        BroadcastLEDStatus(prevLedRgb);
                        skipCount = 0;
                    }
                }
                Thread.Sleep(10);
            }
        }
        public static void BroadcastLEDStatus(byte[] led)
        {
            uint sent = 0;
            byte[] head = { 99, (byte)'L', (byte)'E', (byte)'D' };
            foreach (var conn in connection_map)
            {
                try
                {
                    idevice.idevice_connection_send(conn.Value, head, 4, ref sent);
                    idevice.idevice_connection_send(conn.Value, led, 96, ref sent);
                }
                catch (Exception e) { }
            }
        }
    }
}