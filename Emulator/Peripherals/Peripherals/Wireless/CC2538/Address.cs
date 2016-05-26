﻿//
// Copyright (c) Antmicro
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emul8.Peripherals.Wireless.CC2538
{
    internal class Address
    {
        public Address(AddressingMode mode)
        {
            if(mode != AddressingMode.ShortAddress && mode != AddressingMode.ExtendedAddress)
            {
                throw new ArgumentException("Unsupported addressing mode");
            }

            Bytes = new byte[mode.GetBytesLength()];
        }

        public Address(ArraySegment<byte> source)
        {
            if(source.Count != 2 && source.Count != 8)
            {
                throw new ArgumentException("Unsupported address length");
            }

            Bytes = source;
        }

        public Address(byte[] source)
        {
            if(source.Length != 2 && source.Length != 8)
            {
                throw new ArgumentException("Unsupported address length");
            }

            Bytes = source;
        }

        public void SetByte(byte value, int offset)
        {
            if(offset < 0 || offset >= Bytes.Count)
            {
                throw new ArgumentOutOfRangeException();
            }

            Bytes[offset] = value;
        }

        public override int GetHashCode()
        {
            return Bytes.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var objAsAddress = obj as Address;
            if(objAsAddress == null)
            {
                return false;
            }

            return objAsAddress.Bytes.SequenceEqual(Bytes);
        }

        public ulong GetValue()
        {
            switch(Bytes.Count)
            {
                case 2:
                    return (ulong)BitConverter.ToUInt16(Bytes.ToArray(), 0);
                case 8:
                    return BitConverter.ToUInt64(Bytes.ToArray(), 0);
                default:
                    throw new ArgumentException();
            }
        }

        public IList<byte> Bytes { get; private set; }
        public bool IsShortBroadcast { get { return Bytes.Count == 2 && Bytes.All(x => x == 0xFF); } }
    }
}

