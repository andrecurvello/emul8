﻿//
// Copyright (c) Antmicro
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Emul8.Utilities.GDB
{
    internal abstract class Command : IAutoLoadType
    {
        public static PacketData Execute(Command command, Packet packet)
        {
            var executeMethod = GetExecutingMethod(command, packet);
            var mnemonic = executeMethod.GetCustomAttribute<ExecuteAttribute>().Mnemonic;
            var parsingContext = new ParsingContext(packet, mnemonic.Length);
            var parameters = executeMethod.GetParameters().Select(x => HandleArgumentNotResolved(parsingContext, x)).ToArray();

            return (PacketData)executeMethod.Invoke(command, parameters);
        }

        public static MethodInfo[] GetExecutingMethods(Type t)
        {
            if(t.GetConstructor(new[] { typeof(CommandsManager) }) == null)
            {
                return new MethodInfo[0];
            }
            
            return t.GetMethods().Where(x => 
                x.GetCustomAttribute<ExecuteAttribute>() != null &&
                x.GetParameters().All(y => y.GetCustomAttribute<ArgumentAttribute>() != null)).ToArray();
        }

        protected Command(CommandsManager manager)
        {
            this.manager = manager;
        }

        protected readonly CommandsManager manager;

        private static MethodInfo GetExecutingMethod(Command command, Packet packet)
        {
            var interestingMethods = GetExecutingMethods(command.GetType());
            if(!interestingMethods.Any())
            {
                return null;
            }

            return interestingMethods.SingleOrDefault(x => packet.Data.DataAsString.StartsWith(x.GetCustomAttribute<ExecuteAttribute>().Mnemonic, StringComparison.Ordinal));
        }

        private static object HandleArgumentNotResolved(ParsingContext context, ParameterInfo parameterInfo)
        {
            var attribute = parameterInfo.GetCustomAttribute<ArgumentAttribute>();
            if(attribute == null)
            {
                throw new ArgumentException(string.Format("Could not resolve argument: {0}", parameterInfo.Name));
            }

            var startPosition = context.CurrentPosition;
            var separatorPosition = attribute.Separator == '\0' ? -1 : context.Packet.Data.DataAsString.IndexOf(attribute.Separator, startPosition);
            var length = (separatorPosition == -1 ? context.Packet.Data.DataAsString.Length : separatorPosition) - startPosition;
            var valueToParse = context.Packet.Data.DataAsString.Substring(startPosition, length);

            context.CurrentPosition += length + 1;

            switch(attribute.Encoding)
            {
                case ArgumentAttribute.ArgumentEncoding.HexNumber:
                    return Parse(parameterInfo.ParameterType, valueToParse, NumberStyles.HexNumber);
                case ArgumentAttribute.ArgumentEncoding.DecimalNumber:
                    return Parse(parameterInfo.ParameterType, valueToParse);
                case ArgumentAttribute.ArgumentEncoding.BinaryBytes:
                    return context.Packet.Data.DataAsBinary.Skip(startPosition).ToArray();
                case ArgumentAttribute.ArgumentEncoding.HexBytesString:
                    return valueToParse.Split(2).Select(x => byte.Parse(x, NumberStyles.HexNumber)).ToArray();
                default:
                    throw new ArgumentException(string.Format("Unsupported argument type: {0}", parameterInfo.ParameterType.Name));
            }
        }

        private static object Parse(Type type, string input, NumberStyles style = NumberStyles.Integer)
        {
            if(type.IsEnum)
            {
                return Parse(type.GetEnumUnderlyingType(), input, style);
            }
            if(type == typeof(int))
            {   
                return int.Parse(input, style);
            }
            if(type == typeof(uint))
            {
                return uint.Parse(input, style);
            }

            throw new ArgumentException(string.Format("Unsupported type for parsing: {0}", type.Name));
        }

        private class ParsingContext
        {
            public ParsingContext(Packet packet, int currentPosition)
            {
                Packet = packet;
                CurrentPosition = currentPosition;
            }

            public int CurrentPosition { get; set; }
            public Packet Packet { get; set; }
        }
    }
}

