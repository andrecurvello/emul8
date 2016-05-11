//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
﻿using System;
using Emul8.Core;
using Emul8.Peripherals.Timers;
using Emul8.Logging;
using Emul8.Peripherals.IRQControllers;
using Emul8.Core.Structure.Registers;

namespace Emul8.Peripherals.Timers
{
    public sealed class CortexAGenericTimer : IPeripheral
    {
        public CortexAGenericTimer(Machine machine, GIC gic, long genericTimerCompareValue)
        {
            var receiver = gic.GetLocalReceiver(0);
            irq = new GPIO();
            irq.Connect(receiver, 0x01);
            physicalTimer1 = new CortexAGenericTimerUnit(machine, irq, genericTimerCompareValue);
            physicalTimer2 = new CortexAGenericTimerUnit(machine, irq, genericTimerCompareValue);
            virtualTimer = new CortexAGenericTimerUnit(machine, irq, genericTimerCompareValue);
            virtualTimer.Enabled = true;
        }

        public ulong ReadRegister(uint offset)
        {
            var mask = (offset & OperationMode64Bit) == 0 ? Mask64 : Mask32;
            switch((Registers) (offset & mask))
            {
            case Registers.CounterFrequency:
                return Frequency;
            case Registers.VirtualTimerControl:
                return virtualTimer.ControlRegister;
            case Registers.PL1PhysicalTimerControl:
                return physicalTimer1.ControlRegister;
            case Registers.PL2PhysicalTimerControl:
                return physicalTimer2.ControlRegister;
            case Registers.VirtualTimerValue:
                return (ulong) virtualTimer.Value;
            case Registers.PL1PhysicalTimerValue:
                return (ulong) physicalTimer1.Value;
            case Registers.PL2PhysicalTimerValue:
                return (ulong) physicalTimer2.Value;
            case Registers.VirtualTimerCompareValue:
                return (ulong) virtualTimer.Compare;
            case Registers.PL1PhysicalTimerCompareValue:
                return (ulong) physicalTimer1.Compare;
            case Registers.PL2PhysicalTimerCompareValue:
                return (ulong) physicalTimer2.Compare;
            case Registers.VirtualCount:
                irq.Unset();
                return (ulong) virtualTimer.Value;
            default:
                this.LogUnhandledRead(offset);
                return 0;
            }
        }

        public void WriteRegister(uint offset, ulong value)
        {
            var mask = (offset & OperationMode64Bit) == 0 ? Mask64 : Mask32;
            switch((Registers) (offset & mask))
            {
            case Registers.VirtualTimerControl:
                virtualTimer.Enabled = (value & 1) != 0;
                break;
            case Registers.PL1PhysicalTimerControl:
                physicalTimer1.Enabled = (value & 1) != 0;
                break;
            case Registers.PL2PhysicalTimerControl:
                physicalTimer2.Enabled = (value & 1) != 0;
                break;
            case Registers.VirtualTimerValue:
                checked
                {
                    virtualTimer.Value = (long)value;
                }
                break;
            case Registers.VirtualTimerCompareValue:
                checked
                {
                    virtualTimer.Compare = (long)value;
                }
                break;
            case Registers.PL1PhysicalTimerCompareValue:
                checked
                {
                    physicalTimer1.Compare = (long)value;
                }
                break;
            case Registers.PL2PhysicalTimerCompareValue:
                checked
                {
                    physicalTimer2.Compare = (long)value;
                }
                break;
            default:
                this.LogUnhandledWrite(offset, (long)value);
                break;
            }
        }

        public void Reset()
        {
            virtualTimer.Reset();
            physicalTimer1.Reset();
            physicalTimer2.Reset();
        }

        private CortexAGenericTimerUnit physicalTimer1, physicalTimer2;
        private readonly CortexAGenericTimerUnit virtualTimer;

        private const uint Mask32 = 0xef00ef;
        private const uint Mask64 = 0xff;
        private const long Frequency = 24000000;
        private const uint OperationMode64Bit = 1 << 25;
        private readonly GPIO irq;

        private enum Registers : uint
        {
            CounterFrequency = 0xe0000,  // CNTFRQ
            PhysicalCount = 0xe,  // CNTPCT
            PL1Control = 0xe0001,  // CNTKCTL                                                                                                     
            PL1PhysicalTimerValue = 0xe0002,  // CNTP_TVAL                                                                                           
            PL1PhysicalTimerControl = 0xe0022,  // CNTP_CTL                                                                                         
            VirtualTimerValue = 0xe0003,  // CNTV_TVAL                                                                                               
            VirtualTimerControl = 0xe0023,  // CNTV_CTL                                                                                             
            VirtualCount = 0x1e,  // CNTVCT                                                                                                       
            PL1PhysicalTimerCompareValue = 0x2e,  // CNTP_CVAL                                                                                       
            VirtualTimerCompareValue = 0x3e,  // CNTV_CVAL                                                                                           
            VirtualOffset = 0x4e,  // CNTVOFF                                                                                                      
            PL2Control = 0x8e0001,  // CNTHCTL                                                                                                     
            PL2PhysicalTimerValue = 0x8e0002,  // CNTHP_TVAL                                                                                          
            PL2PhysicalTimerControl = 0x8e0022,  // CNTHP_CTL                                                                                        
            PL2PhysicalTimerCompareValue = 0x6e  // CNTHP_CVAL
        };

        private sealed class CortexAGenericTimerUnit : ComparingTimer
        {
            public CortexAGenericTimerUnit(Machine machine, GPIO irq, long compareValue)
                : base(machine, Frequency, compareValue, long.MaxValue)
            {
                controlRegister = new DoubleWordRegister(this);
                controlRegister.DefineFlagField(0, writeCallback: OnEnabled);
                maskOutput = controlRegister.DefineFlagField(1);
                outputFlag = controlRegister.DefineFlagField(2, FieldMode.Read);
                this.irq = irq;
                compareValueDiff = compareValue;
            }

            public uint ControlRegister
            {
                get
                {
                    return controlRegister.Read();
                }
                set
                {
                    controlRegister.Write((long)Registers.VirtualTimerControl, value);
                }
            }

            protected override void OnCompare()
            {
                irq.Set();
                if(!maskOutput.Value)
                {
                    outputFlag.Value = true;
                }
                // TODO: overflow causes the emulator to crash.
                Compare += compareValueDiff;
            }

            private void OnEnabled(bool oldValue, bool newValue)
            {
                Enabled = newValue;
            }

            private readonly DoubleWordRegister controlRegister;
            private readonly IFlagRegisterField outputFlag, maskOutput;
            private readonly GPIO irq;
            private readonly long compareValueDiff;
        }
    }
}

