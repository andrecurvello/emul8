﻿//
// Copyright (c) Antmicro
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;

namespace Emul8.Robot
{
    internal class SmartParser
    {
        public static SmartParser Instance = new SmartParser();

        public object Parse(string input, Type outputType)
        {
            if(input.GetType() == outputType)
            {
                return input;
            }

            NumberStyles style;
            if(input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                style = NumberStyles.HexNumber;
                input = input.Substring(2);
            }
            else
            {
                style = NumberStyles.Integer;
            }

            Delegate parser;
            if(!cache.TryGetValue(outputType, out parser))
            {
                var types = new[] { typeof(string), typeof(NumberStyles) };
                var method = outputType.GetMethod("Parse", types);
                if(method == null)
                {
                    throw new ArgumentException(string.Format("{0} type does not have Parse method", outputType.Name));
                }

                var delegateType = Expression.GetDelegateType(types.Concat(new[] { method.ReturnType }).ToArray());
                parser = method.CreateDelegate(delegateType);
                cache.Add(outputType, parser);
            }

            return parser.DynamicInvoke(input, style);
        }

        public object[] Parse(string[] input, Type[] outputType)
        {
            if(input.Length != outputType.Length)
            {
                throw new ArgumentException();
            }

            var result = new object[input.Length];
            for(var i = 0; i < input.Length; i++)
            {
                result[i] = Parse(input[i], outputType[i]);
            }

            return result;
        }

        private SmartParser()
        {
            cache = new Dictionary<Type, Delegate>();
        }

        private readonly Dictionary<Type, Delegate> cache;
    }
}

