// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.FSharp.Collections;
using MessagePack.Formatters;

namespace MessagePack.FSharp.Formatters
{
    public sealed class FSharpMapFormatter<TKey, TValue> : DictionaryFormatterBase<TKey, TValue, Tuple<TKey, TValue>[], IEnumerator<KeyValuePair<TKey, TValue>>, FSharpMap<TKey, TValue>>
    {
        protected override void Add(Tuple<TKey, TValue>[] collection, int index, TKey key, TValue value, MessagePackSerializerOptions options)
        {
            collection[index] = Tuple.Create(key, value);
        }

        protected override FSharpMap<TKey, TValue> Complete(Tuple<TKey, TValue>[] intermediateCollection)
        {
            return MapModule.OfArray(intermediateCollection);
        }

        protected override Tuple<TKey, TValue>[] Create(int count, MessagePackSerializerOptions options)
        {
            return new Tuple<TKey, TValue>[count];
        }

        protected override IEnumerator<KeyValuePair<TKey, TValue>> GetSourceEnumerator(FSharpMap<TKey, TValue> source)
        {
            return ((IEnumerable<KeyValuePair<TKey, TValue>>)source).GetEnumerator();
        }
    }

}