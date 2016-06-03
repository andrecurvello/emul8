﻿//
// Copyright (c) Antmicro
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using Emul8.Peripherals.Network;
using Emul8.Peripherals;
using Emul8.Core;
using System.IO;
using System.Threading;
using Emul8.Logging;
using System.Threading.Tasks;
using Emul8.Core.Structure;
using Emul8.Network;
using System.Net.NetworkInformation;
using System.Linq;
using Emul8.Utilities;
using Mono.Unix;
using Emul8.Exceptions;

namespace Emul8.HostInterfaces.Network
{
    public sealed class OsXTapInterface : ITapInterface, IHasOwnLife, IDisposable
    {
        public OsXTapInterface(string interfaceNameOrPath)
        {
            Link = new NetworkLink(this);

            if(!Directory.Exists("/Library/Extensions/tap.kext/"))
            {
                this.Log(LogLevel.Warning, "No TUNTAP kernel extension found, running in dummy mode.");
                MAC = EmulationManager.Instance.CurrentEmulation.MACRepository.GenerateUniqueMAC();
                return;
            }
            if(!File.Exists(interfaceNameOrPath))
            {
                var tapDevicePath = ConfigurationManager.Instance.Get<string>("tap", "tap-device-path", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                interfaceNameOrPath = Path.Combine(tapDevicePath, interfaceNameOrPath);
            }

            deviceFile = File.Open(interfaceNameOrPath, FileMode.Open, FileAccess.ReadWrite);

            // let's find out to what interface the character device file belongs
            var deviceType = new UnixFileInfo(interfaceNameOrPath).DeviceType;
            var majorNumber = deviceType >> 24;
            var minorNumber = deviceType & 0xFFFFFF;
            if(majorNumber != ExpectedMajorNumber)
            {
                throw new ConstructionException(string.Format("Unexpected major device number for OS X's tap: {0}.", majorNumber));
            }
            networkInterface = NetworkInterface.GetAllNetworkInterfaces().Single(x => x.Name == "tap" + minorNumber);
            MAC = (MACAddress)networkInterface.GetPhysicalAddress();
        }

        public void ReceiveFrame(EthernetFrame frame)
        {
            if(deviceFile == null)
            {
                return;
            }
            var bytes = frame.Bytes;
            try
            {
                // since the file reader operations are buffered, we have to immediately flush writes
                deviceFile.Write(bytes, 0, bytes.Length);
                deviceFile.Flush();
            }
            catch(IOException)
            {
                if(networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    this.DebugLog("Interface is not up during write, frame dropped.");
                }
                else
                {
                    throw;
                }
            }
            this.NoisyLog("Frame of length {0} sent to host.", frame.Length);
        }

        public void Start()
        {
            Resume();
        }

        public void Pause()
        {
            if(deviceFile == null)
            {
                return;
            }
            cts.Cancel();
            readerTask.Wait();
        }

        public void Resume()
        {
            if(deviceFile == null)
            {
                return;
            }
            cts = new CancellationTokenSource();
            readerTask = Task.Run(ReadPacketAsync);
            readerTask.ContinueWith(x => 
                this.Log(LogLevel.Error, "Exception happened on reader task ({0}). Task stopped.", x.Exception.InnerException.GetType().Name), TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Dispose()
        {
            if(deviceFile != null)
            {
                deviceFile.Close();
            }
        }

        public NetworkLink Link { get; private set; }

        public MACAddress MAC { get; set; }

        public string InterfaceName 
        {
            get
            {
                return networkInterface.Name;
            }
        }

        private async Task ReadPacketAsync()
        {
            var buffer = new byte[Mtu];
            while(!cts.IsCancellationRequested)
            {
                try
                {
                    if(await deviceFile.ReadAsync(buffer, 0, buffer.Length, cts.Token) > 0)
                    {
                        var frame = new EthernetFrame(buffer, true);
                        Link.TransmitFrameFromInterface(frame);
                        this.NoisyLog("Frame of length {0} received from host.", frame.Length);
                    }
                }
                catch(IOException)
                {
                    if(networkInterface.OperationalStatus != OperationalStatus.Up)
                    {
                        this.NoisyLog("I/O exception while interface is not up, waiting {0}s.", Misc.NormalizeDecimal(GracePeriod.TotalSeconds));
                        // probably the interface is not opened yet
                        await Task.Delay(GracePeriod);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private Task readerTask;
        private CancellationTokenSource cts;
        private readonly FileStream deviceFile;
        private readonly NetworkInterface networkInterface;

        private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(1);
        private const int Mtu = 1500;
        private const int ExpectedMajorNumber = 20;
    }
}

