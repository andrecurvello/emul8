//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//

using System;
using Emul8.Core;
using Emul8.Core.Structure;
using Emul8.Peripherals.Network;
using System.Net.NetworkInformation;
using System.Linq;
using Emul8.TAPHelper;
using Emul8.Peripherals;
using System.Threading;
using Antmicro.Migrant.Hooks;
using System.IO;
using Emul8.Logging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Emul8.Utilities;
using Emul8.Exceptions;
using Mono.Unix;
using Emul8.Network;
using Antmicro.Migrant;

namespace Emul8.HostInterfaces.Network
{

    public class LinuxTapInterface : IMACInterface, IHasOwnLife, IDisposable, IHostMachineElement
    {
        public LinuxTapInterface(string name, bool persistent)
        {
            backupMAC = EmulationManager.Instance.CurrentEmulation.MACRepository.GenerateUniqueMAC();
            Link = new NetworkLink(this);
            deviceName = name ?? "";
            this.persistent = persistent;
            Init();
        }

        public void Dispose()
        {
            if(stream != null)
            {
                stream.Close();
            }

            if(tapFileDescriptor != -1)
            {
                LibC.Close(tapFileDescriptor);
                tapFileDescriptor = -1;
            }
        }

        public void Pause()
        {
            if(active)
            {
                lock(lockObject)
                {
                    var token = cts;
                    token.Cancel();
                    thread.Join();
                    thread = null;         
                }
            }
        }

        public void ReceiveFrame(EthernetFrame frame)
        {
            // TODO: non blocking
            if(stream == null)
            {
                return;
            }
            var handle = GCHandle.Alloc(frame.Bytes, GCHandleType.Pinned);
            try
            {
                var result = LibC.WriteData(stream.Handle, handle.AddrOfPinnedObject(), frame.Length);
                if(result == 0)
                {
                    this.Log(LogLevel.Error,
                        "Error while writing to TUN interface: {0}.", result);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        public void Resume()
        {
            if(active)
            {
                lock(lockObject)
                {
                    cts = new CancellationTokenSource();
                    thread = new Thread(() => TransmitLoop(cts.Token)) {
                        Name = this.GetType().Name,
                        IsBackground = true
                    };
                    thread.Start();
                }
            }
        }

        public void Start()
        {
            Resume();
        }

        public string InterfaceName { get; private set; }

        public NetworkLink Link { get; private set; }

        public MACAddress MAC
        {
            get
            {
                var ourInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.Name == InterfaceName);
                if(ourInterface == null)
                {
                    return backupMAC;
                }
                var mac = (MACAddress)ourInterface.GetPhysicalAddress();
                return mac;
            }
            set
            {
                throw new NotSupportedException("Cannot change the MAC of the host machine.");
            }
        }

        [PostDeserialization]
        private void Init()
        {
            active = false;
            // if there is no /dev/net/tun, run in a "dummy" mode
            if(!File.Exists("/dev/net/tun"))
            {
                this.Log(LogLevel.Warning, "No TUN device found, running in dummy mode.");
                return;
            }

            IntPtr devName;
            if(deviceName != "")
            {
                // non-anonymous mapping
                devName = Marshal.StringToHGlobalAnsi(deviceName);
            }
            else
            {
                devName = Marshal.AllocHGlobal(DeviceNameBufferSize);
                Marshal.WriteByte(devName, 0); // null termination
            }
            try
            {
                tapFileDescriptor = LibC.OpenTAP(devName, persistent);
                if(tapFileDescriptor < 0)
                {
                    var process = new Process();
                    var output = string.Empty;
                    process.StartInfo.FileName = "mono";
                    process.StartInfo.Arguments = string.Format("{0} {1} true", DynamicModuleSpawner.GetTAPHelper(), deviceName);

                    try
                    {
                        SudoTools.EnsureSudoProcess(process, "TAP creator");
                    }
                    catch(Exception ex)
                    {
                        throw new RecoverableException("Process elevation failed: " + ex.Message);
                    }

                    process.EnableRaisingEvents = true;
                    process.StartInfo.CreateNoWindow = false;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardOutput = true;

                    var started = process.Start();
                    if(started)
                    {
                        output = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                    }
                    if(!started || process.ExitCode != 0)
                    {
                        this.Log(LogLevel.Warning, "Could not create TUN/TAP interface, running in dummy mode.");
                        this.Log(LogLevel.Debug, "Error {0} while opening tun device '{1}': {2}", process.ExitCode, deviceName, output);
                        return;
                    }
                    Init();
                    return;
                }
                stream = new UnixStream(tapFileDescriptor, true);
                InterfaceName = Marshal.PtrToStringAnsi(devName);
                this.Log(LogLevel.Info,
                    "Opened interface {0}.", InterfaceName);
            }
            finally
            {
                Marshal.FreeHGlobal(devName);
            }
            active = true;
        }

        private void TransmitLoop(CancellationToken token)
        {
            while(true)
            {
                byte[] buffer = null;
                if(stream == null)
                {
                    return;
                }
                try
                {
                    buffer = LibC.ReadDataWithTimeout(stream.Handle, MTU, 1000, () => token.IsCancellationRequested);
                }
                catch(ArgumentException)
                {
                    // stream was closed
                    return;
                }
                catch(ObjectDisposedException)
                {
                    return;
                }

                if(token.IsCancellationRequested)
                {
                    return;
                }
                if(buffer == null || buffer.Length == 0)
                {
                    continue;
                }
                var ethernetFrame = new EthernetFrame(buffer, true);
                Link.TransmitFrameFromInterface(ethernetFrame);
            }
        }

        private const int DeviceNameBufferSize = 8192;
        private const int MTU = 1514;

        [Transient]
        private bool active;
        private MACAddress backupMAC;
        [Transient]
        private CancellationTokenSource cts;
        private readonly string deviceName;
        private readonly object lockObject = new object();
        private readonly bool persistent;
        [Transient]
        private UnixStream stream;
        [Transient]
        private int tapFileDescriptor;
        [Transient]
        private Thread thread;
    }
}
