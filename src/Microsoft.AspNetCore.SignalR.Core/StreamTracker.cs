﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR
{
    internal class StreamTracker
    {
        private static readonly MethodInfo _buildConverterMethod = typeof(StreamTracker).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(m => m.Name.Equals("BuildStream"));
        private ConcurrentDictionary<string, IStreamConverter> _lookup = new ConcurrentDictionary<string, IStreamConverter>();

        /// <summary>
        /// Creates a new stream and returns the ChannelReader for it as an object.
        /// </summary>
        public object AddStream(string streamId, Type itemType)
        {
            var newConverter = (IStreamConverter)_buildConverterMethod.MakeGenericMethod(itemType).Invoke(null, Array.Empty<object>());
            _lookup[streamId] = newConverter;
            return newConverter.GetReaderAsObject();
        }

        public Task ProcessItem(StreamDataMessage message)
        {
            return _lookup[message.StreamId].WriteToStream(message.Item);
        }
        
        public Type GetStreamItemType(string streamId)
        {
            return _lookup[streamId].GetItemType();
        }

        public void Complete(StreamCompleteMessage message)
        {
            _lookup.TryRemove(message.StreamId, out var converter);
            converter.TryComplete(message.HasError ? new Exception(message.Error) : null);
        }

        private static IStreamConverter BuildStream<T>()
        {
            return new ChannelConverter<T>();
        }

        private interface IStreamConverter
        {
            Type GetItemType();
            object GetReaderAsObject();
            Task WriteToStream(object item);
            void TryComplete(Exception ex);
        }

        private class ChannelConverter<T> : IStreamConverter
        {
            private Channel<T> _channel;

            public ChannelConverter()
            {
                _channel = Channel.CreateUnbounded<T>();
            }

            public Type GetItemType()
            {
                return typeof(T);
            }

            public object GetReaderAsObject()
            {
                return _channel.Reader;
            }

            public Task WriteToStream(object o)
            {
                return _channel.Writer.WriteAsync((T)o).AsTask();
            }

            public void TryComplete(Exception ex)
            {
                _channel.Writer.TryComplete(ex);
            }
        }
    }
}
