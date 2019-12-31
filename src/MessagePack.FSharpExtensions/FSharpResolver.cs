// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using MessagePack.Formatters;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Collections;
using MessagePack.FSharp.Formatters;

namespace MessagePack.FSharp
{
    public sealed class FSharpResolver : IFormatterResolver
    {
        public static readonly IFormatterResolver Instance = new FSharpResolver();

        FSharpResolver() { }

        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;

            static FormatterCache()
            {
                Formatter = (IMessagePackFormatter<T>)FSharpGetFormatterHelper.GetFormatter(typeof(T));

                if (Formatter == null)
                {
                    var f = DynamicDiscriminatedUnionResolver.Instance.GetFormatter<T>();

                    if (f != null)
                    {
                        Formatter = f;
                    }
                }
            }
        }
    }

    internal static class FSharpGetFormatterHelper
    {
        static readonly Dictionary<Type, Type> formatterMap = new Dictionary<Type, Type>()
        {
              {typeof(FSharpList<>), typeof(FSharpListFormatter<>)},
              {typeof(FSharpMap<,>), typeof(FSharpMapFormatter<,>)},
              {typeof(FSharpSet<>), typeof(FSharpSetFormatter<>)},
              {typeof(FSharpAsync<>), typeof(FSharpAsyncFormatter<>)}
        };

        internal static object GetFormatter(Type t)
        {
            var ti = t.GetTypeInfo();

            if (t == typeof(Unit))
            {
                return new UnitFormatter();
            }

            if (ti.IsGenericType)
            {
                var genericType = ti.GetGenericTypeDefinition();

                Type formatterType;
                
                if (formatterMap.TryGetValue(genericType, out formatterType))
                {
                    return CreateInstance(formatterType, ti.GenericTypeArguments);
                }
                else if (genericType.GetTypeInfo().IsFSharpOption())
                {
                    return CreateInstance(typeof(FSharpOptionFormatter<>), new[] { ti.GenericTypeArguments[0] });
                }
            }

            return null;
        }

        static object CreateInstance(Type genericType, Type[] genericTypeArguments, params object[] arguments)
        {
            return Activator.CreateInstance(genericType.MakeGenericType(genericTypeArguments), arguments);
        }
    }

    internal static class ReflectionExtensions
    {
        public static bool IsFSharpOption(this TypeInfo type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FSharpOption<>);
        }
    }
}
