// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.FSharp.Reflection;
using MessagePack.Formatters;
using MessagePack.Internal;
using MessagePack.FSharp.Internal;
using System.Buffers;

namespace MessagePack.FSharp
{
    /// <summary>
    /// DynamicDiscriminatedUnionResolver by dynamic code generation.
    /// </summary>
    public sealed class DynamicDiscriminatedUnionResolver : IFormatterResolver
    {
        #region Properties

        private const string ModuleName = "MessagePack.FSharp.DynamicDiscriminatedUnionResolver";

        /// <summary>
        /// The singleton instance that can be used.
        /// </summary>
        public static readonly DynamicDiscriminatedUnionResolver Instance = new DynamicDiscriminatedUnionResolver();

        /// <summary>
        /// A <see cref="MessagePackSerializerOptions"/> instance with this formatter pre-configured.
        ///
        public static readonly MessagePackSerializerOptions StandardOptions = MessagePackSerializerOptions.Standard.WithResolver(Instance);

        private static readonly DynamicAssembly assembly;

        private static readonly Regex SubtractFullNameRegex = new Regex(@", Version=\d+.\d+.\d+.\d+, Culture=\w+, PublicKeyToken=\w+");

        private static int nameSequence = 0;

        #endregion

        static DynamicDiscriminatedUnionResolver()
        {
            assembly = new DynamicAssembly(ModuleName);
        }

        private DynamicDiscriminatedUnionResolver() { }

        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;

            static FormatterCache()
            {
                if (!FSharpType.IsUnion(typeof(T), null)) {
                    return;
                }

                var formatterTypeInfo = BuildType(typeof(T));

                if (formatterTypeInfo == null) {
                    return;
                }

                Formatter = (IMessagePackFormatter<T>)Activator.CreateInstance(formatterTypeInfo.AsType());
            }
        }

        private static TypeInfo BuildType(Type type)
        {
            // order by key(important for use jump-table of switch)
            var unionCases = FSharpType.GetUnionCases(type, null).OrderBy(x => x.Tag).ToArray();

            var formatterType = typeof(IMessagePackFormatter<>).MakeGenericType(type);
            var typeBuilder = assembly.DefineType("MessagePack.FSharp.Formatters." + SubtractFullNameRegex.Replace(type.FullName, "").Replace(".", "_") + "Formatter" + +Interlocked.Increment(ref nameSequence), TypeAttributes.Public | TypeAttributes.Sealed, null, new[] { formatterType });

            var stringByteKeysFields = new FieldBuilder[unionCases.Length];

            // create map dictionary
            {
                var method = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);

                foreach(var unionCase in unionCases)
                {
                  stringByteKeysFields[unionCase.Tag] = typeBuilder.DefineField("stringByteKeysField" + unionCase.Tag, typeof(byte[][]), FieldAttributes.Private | FieldAttributes.InitOnly);
                }

                var il = method.GetILGenerator();
                
                BuildConstructor(il, type, unionCases, stringByteKeysFields);
            }

            {
                var method = typeBuilder.DefineMethod(
                    "Serialize", 
                    MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual, // | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                    returnType: null,
                    parameterTypes: new Type[] { refMessagePackWriter, type, typeof(MessagePackSerializerOptions) });

                method.DefineParameter(1, ParameterAttributes.None, "writer");
                method.DefineParameter(2, ParameterAttributes.None, "value");
                method.DefineParameter(3, ParameterAttributes.None, "options");

                var il = method.GetILGenerator();

                BuildSerialize(il, type, unionCases, stringByteKeysFields);
            }

            {
                var method = typeBuilder.DefineMethod(
                    "Deserialize", 
                    MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual, // | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                    returnType: type,
                    parameterTypes: new Type[] { refMessagePackReader, typeof(MessagePackSerializerOptions) });

                method.DefineParameter(1, ParameterAttributes.None, "reader");
                method.DefineParameter(2, ParameterAttributes.None, "options");

                var il = method.GetILGenerator();

                BuildDeserialize(il, type, unionCases, stringByteKeysFields);
            }

            return typeBuilder.CreateTypeInfo();
        }

        private static void BuildConstructor(ILGenerator il, Type type, UnionCaseInfo[] infos, FieldBuilder[] stringByteKeysFields)
        {            
            il.EmitLoadThis();
            il.Emit(OpCodes.Call, objectCtor);

            foreach (var info in infos)
            {
                var fields = info.GetFields();

                il.EmitLoadThis();
                il.EmitLdc_I4(fields.Length);
                il.Emit(OpCodes.Newarr, typeof(byte[]));

                var index = 0;
                foreach (var field in fields)
                {
                    il.Emit(OpCodes.Dup);
                    il.EmitLdc_I4(index);
                    il.Emit(OpCodes.Ldstr, field.Name);
                    il.EmitCall(GetEncodedStringBytes);
                    il.Emit(OpCodes.Stelem_Ref);

                    index++;
                }

                il.Emit(OpCodes.Stfld, stringByteKeysFields[info.Tag]);
            }

            il.Emit(OpCodes.Ret);
        }

        // void Serialize([arg:1]MessagePackWriter writer, [arg:2]T value, [arg:3]MessagePackSerializerOptions options)
        private static void BuildSerialize(ILGenerator il, Type type, UnionCaseInfo[] infos, FieldBuilder[] stringByteKeysFields)
        {
            var tag = getTag(type);

            var writer = new ArgumentField(il, 1);
            var value = new ArgumentField(il, 2, type);
            var options = new ArgumentField(il, 3);

            il.EmitCall(BreakDebugger);

            // check possible null value
            var notNullValueLabel = il.DefineLabel();

            value.EmitLoad();
            il.Emit(OpCodes.Brtrue_S, notNullValueLabel);
            
            // value is null, writer.WriteNil() and return
            writer.EmitLoad();
            il.EmitCall(MessagePackWriterTypeInfo.WriteNil);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notNullValueLabel);

            // IFormatterResolver resolver = options.Resolver            
            var localResolver = il.DeclareLocal(typeof(IFormatterResolver));

            options.EmitLdarg();
            il.EmitCall(getResolverFromOptions);
            il.EmitStloc(localResolver);

            // writer.WriteArrayHeader(2, false)
            writer.EmitLoad();
            il.EmitLdc_I4(2);
            il.EmitCall(MessagePackWriterTypeInfo.WriteArrayHeader);

            // writer.Write(value.Tag)
            writer.EmitLoad();
            value.EmitLoad();
            il.EmitCall(tag);
            il.EmitCall(MessagePackWriterTypeInfo.WriteInt32);
           
            // switch-case
            var defaultCase = il.DefineLabel();
            var endOfSwitch = il.DefineLabel();
 
            var switchLabels = infos.Select(x => new { Label = il.DefineLabel(), Info = x }).ToArray();

            value.EmitLoad();
            il.EmitCall(tag);
            il.Emit(OpCodes.Switch, switchLabels.Select(x => x.Label).ToArray());

            // default    
            il.Emit(OpCodes.Br, endOfSwitch);

            foreach (var item in switchLabels)
            {
                il.MarkLabel(item.Label);
                EmitSerializeUnionCase(il, type, UnionSerializationInfo.CreateOrNull(type, item.Info), stringByteKeysFields[item.Info.Tag], writer, value, options, localResolver);
                il.Emit(OpCodes.Br, endOfSwitch);
            }
            
            il.MarkLabel(endOfSwitch);
            
            // return;
            il.Emit(OpCodes.Ret);
        }

        // void Serialize([arg:1]MessagePackWriter writer, [arg:2]T value, [arg:3]MessagePackSerializerOptions options)
        private static void EmitSerializeUnionCase(ILGenerator il, Type type, UnionSerializationInfo info, FieldBuilder stringByteKeysField, ArgumentField writer, ArgumentField value, ArgumentField options, LocalBuilder localResolver)
        {
            var ti = type.GetTypeInfo();

            // IMessagePackSerializationCallbackReceiver.OnBeforeSerialize()            
            if (ti.ImplementedInterfaces.Any(x => x == typeof(IMessagePackSerializationCallbackReceiver)))
            {
                // call directly
                var runtimeMethods = type.GetRuntimeMethods().Where(x => x.Name == "OnBeforeSerialize").ToArray();

                if (runtimeMethods.Length == 1)
                {
                    value.EmitLoad();
                    il.Emit(OpCodes.Call, runtimeMethods[0]); // don't use EmitCall helper(must use 'Call')
                }
                else
                {
                    value.EmitLdarg();

                    if (info.IsStruct)
                    {
                        il.Emit(OpCodes.Box, type);
                    }

                    il.EmitCall(onBeforeSerialize);
                }
            }

            if (info.IsIntKey)
            {
                // use Array
                var maxKey = info.Members.Select(x => x.IntKey).DefaultIfEmpty(0).Max();
                var intKeyMap = info.Members.ToDictionary(x => x.IntKey);

                var len = maxKey + 1;
                
                writer.EmitLoad();
                il.EmitLdc_I4(len);
                il.EmitCall(MessagePackWriterTypeInfo.WriteArrayHeader);

                for (int i = 0; i <= maxKey; i++)
                {
                    UnionSerializationInfo.EmittableMember member;

                    if (intKeyMap.TryGetValue(i, out member))
                    {                        
                        EmitSerializeValue(il, ti, member, writer, value, options, localResolver);
                    }
                    else
                    {
                        // Write Nil as Blanc
                        writer.EmitLoad();
                        il.EmitCall(MessagePackWriterTypeInfo.WriteNil);
                    }
                }
            }
            else
            {
                // use Map
                var writeCount = info.Members.Count();

                writer.EmitLoad();
                il.EmitLdc_I4(writeCount);
                il.EmitCall(MessagePackWriterTypeInfo.WriteMapHeader);

                var index = 0;
                foreach (var item in info.Members)
                {
                    writer.EmitLoad();                    
                    il.EmitLoadThis();
                    il.EmitLdfld(stringByteKeysField);
                    il.EmitLdc_I4(index);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Call, ReadOnlySpanFromByteArray); // convert byte[] to ReadOnlySpan<byte>

                    var valueLen = CodeGenHelpers.GetEncodedStringBytes(item.StringKey).Length;

                    if (valueLen <= MessagePackRange.MaxFixStringLength)
                    {
                        if (UnsafeMemory.Is32Bit)
                        {
                            il.EmitCall(typeof(UnsafeMemory32).GetRuntimeMethod("WriteRaw" + valueLen, new[] { refMessagePackWriter, typeof(ReadOnlySpan<byte>) }));
                        }
                        else
                        {
                            il.EmitCall(typeof(UnsafeMemory64).GetRuntimeMethod("WriteRaw" + valueLen, new[] { refMessagePackWriter, typeof(ReadOnlySpan<byte>) }));
                        }
                    }
                    else
                    {
                        il.EmitCall(MessagePackWriterTypeInfo.WriteRaw);
                    }
                    
                    EmitSerializeValue(il, ti, item, writer, value, options, localResolver);
                    index++;
                }                
            }
        }

        private static void EmitSerializeValue(ILGenerator il, TypeInfo type, UnionSerializationInfo.EmittableMember member, ArgumentField writer, ArgumentField value, ArgumentField options, LocalBuilder localResolver)
        {
            Type t = member.Type;

            if (IsOptimizeTargetType(t))
            {
                writer.EmitLoad();
                value.EmitLoad();
                member.EmitLoadValue(il);

                if (t == typeof(byte[]))
                {
                    il.EmitCall(ReadOnlySpanFromByteArray);
                    il.EmitCall(MessagePackWriterTypeInfo.WriteBytes);
                }
                else
                {
                    il.EmitCall(typeof(MessagePackWriter).GetRuntimeMethod("Write", new Type[] { t }));         
                }
            }
            else
            {
                il.EmitLdloc(localResolver);
                il.Emit(OpCodes.Call, getFormatterWithVerify.MakeGenericMethod(t));

                writer.EmitLoad();
                value.EmitLoad();
                member.EmitLoadValue(il);
                options.EmitLoad();
                il.EmitCall(getSerialize(t));
            }
        }

        // T Deserialize([arg:1]ref MessagePackReader reader, [arg:2]MessagePackSerializerOptions options)
        private static void BuildDeserialize(ILGenerator il, Type type, UnionCaseInfo[] infos, FieldBuilder[] stringByteKeysFields)
        {            
            var ti = type.GetTypeInfo();            

            var reader = new ArgumentField(il, 1, @ref: true);
            var options = new ArgumentField(il, 2);

            // if(reader.TryReadNil()) { return null; } 
            var notNullLabel = il.DefineLabel();

            reader.EmitLdarg();
            il.EmitCall(MessagePackReaderTypeInfo.TryReadNil);
            il.Emit(OpCodes.Brfalse_S, notNullLabel);

            if (ti.IsClass)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(OpCodes.Ldstr, "Typecode is null, struct not supported");
                il.Emit(OpCodes.Newobj, messagePackSerializationExceptionMessageOnlyConstructor);
                il.Emit(OpCodes.Throw);
            }

            il.MarkLabel(notNullLabel);

            // IFormatterResolver resolver = options.Resolver             
            var localResolver = il.DeclareLocal(typeof(IFormatterResolver));
            
            options.EmitLoad();
            il.EmitCall(getResolverFromOptions);
            il.EmitStloc(localResolver);

            // if (reader.ReadArrayHeader() != 2) { throw }; 
            var validArrayHeaderLabel = il.DefineLabel();
           
            reader.EmitLdarg();
            il.EmitCall(MessagePackReaderTypeInfo.ReadArrayHeader);

            il.EmitLdc_I4(2);
            il.Emit(OpCodes.Beq_S, validArrayHeaderLabel);
            il.Emit(OpCodes.Ldstr, "Invalid Union data was detected. Type: " + type.FullName);
            il.Emit(OpCodes.Newobj, messagePackSerializationExceptionMessageOnlyConstructor);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(validArrayHeaderLabel);

            // int key = reader.ReadInt32() 
            var key = il.DeclareLocal(typeof(int));
            
            reader.EmitLdarg();
            il.EmitCall(MessagePackReaderTypeInfo.ReadInt32);
            il.EmitStloc(key);

            // switch -> read result 
            var result = il.DeclareLocal(type);
            
            var defaultCase = il.DefineLabel();
            var endOfSwitch = il.DefineLabel();

            if (ti.IsClass)
            {
                il.Emit(OpCodes.Ldnull);
                il.EmitStloc(result);
            }

            var switchLabels = infos.Select(x => new { Label = il.DefineLabel(), Info = x }).ToArray();

            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Switch, switchLabels.Select(x => x.Label).ToArray());

            il.Emit(OpCodes.Br, defaultCase);

            foreach (var item in switchLabels)
            {
                il.MarkLabel(item.Label);

                EmitDeserializeUnionCase(il, type, UnionSerializationInfo.CreateOrNull(type, item.Info), key, stringByteKeysFields[item.Info.Tag], reader, options, localResolver);

                il.Emit(OpCodes.Stloc, result);
                il.Emit(OpCodes.Br, endOfSwitch);
            }

            // default
            il.MarkLabel(defaultCase);

            reader.EmitLdarg();
            il.EmitCall(MessagePackReaderTypeInfo.Skip);

            il.MarkLabel(endOfSwitch);

            // return;
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
        }

        // T Deserialize([arg:1]ref MessagePackReader reader, [arg:2]MessagePackSerializerOptions options)
        static void EmitDeserializeUnionCase(ILGenerator il, Type type, UnionSerializationInfo info, LocalBuilder unionKey, FieldBuilder stringByteKeysField, ArgumentField reader, ArgumentField options, LocalBuilder localResolver)
        {
            // int length = info.IsIntKey ? reader.ReadArrayHeader() : reader.ReadMapHeader();            
            var length = il.DeclareLocal(typeof(int));

            reader.EmitLdarg();

            if (info.IsIntKey)
            {
                il.EmitCall(MessagePackReaderTypeInfo.ReadArrayHeader);
            }
            else
            {
                il.EmitCall(MessagePackReaderTypeInfo.ReadMapHeader);
            }

            il.EmitStloc(length);                      

            // make local fields 
            Label? gotoDefault = null;
            DeserializeInfo[] infoList;

            if (info.IsIntKey)
            {
                var maxKey = info.Members.Select(x => x.IntKey).DefaultIfEmpty(-1).Max();
                var len = maxKey + 1;
                var intKeyMap = info.Members.ToDictionary(x => x.IntKey);

                infoList = Enumerable.Range(0, len)
                    .Select(x =>
                    {
                        UnionSerializationInfo.EmittableMember member;

                        if (intKeyMap.TryGetValue(x, out member))
                        {
                            return new DeserializeInfo
                            {
                                MemberInfo = member,
                                LocalField = il.DeclareLocal(member.Type),
                                SwitchLabel = il.DefineLabel()
                            };
                        }
                        else
                        {
                            // return null MemberInfo, should filter null
                            if (gotoDefault == null)
                            {
                                gotoDefault = il.DefineLabel();
                            }

                            return new DeserializeInfo
                            {
                                MemberInfo = null,
                                LocalField = null,
                                SwitchLabel = gotoDefault.Value,
                            };
                        }
                    })
                    .ToArray();
            }
            else
            {
                infoList = info.Members
                    .Select(item => new DeserializeInfo
                    {
                        MemberInfo = item,
                        LocalField = il.DeclareLocal(item.Type),
                        SwitchLabel = il.DefineLabel()
                    })
                    .ToArray();
            }

            // read Loop(for var i = 0; i< length; i++)
            if (info.IsStringKey)
            {
                var automata = new AutomataDictionary();

                for (int i = 0; i < info.Members.Length; i++)
                { 
                    automata.Add(info.Members[i].StringKey, i);
                }

                var buffer = il.DeclareLocal(typeof(ReadOnlySpan<byte>));           
                var longKey = il.DeclareLocal(typeof(ulong));
  
                // for (int i = 0; i < len; i++)
                il.EmitIncrementFor(length, forILocal =>
                {
                    var readNext = il.DefineLabel();
                    var loopEnd = il.DefineLabel();

                    reader.EmitLdarg();
                    il.EmitCall(ReadStringSpan);
                    il.EmitStloc(buffer);

                    // gen automata name lookup
                    automata.EmitMatch(
                        il, 
                        buffer, 
                        longKey, 
                        x => 
                        {
                            var i = x.Value;
                            
                            if (infoList[i].MemberInfo != null)
                            {
                                EmitDeserializeValue(il, infoList[i], reader, options, localResolver);
                                il.Emit(OpCodes.Br, loopEnd);
                            }
                            else
                            {
                                il.Emit(OpCodes.Br, readNext);
                            }
                        }, 
                        () =>
                        {
                            il.Emit(OpCodes.Br, readNext);
                        });

                    il.MarkLabel(readNext);

                    reader.EmitLdarg();
                    il.EmitCall(MessagePackReaderTypeInfo.Skip);

                    il.MarkLabel(loopEnd);
                });
            }
            else
            {
                var key = il.DeclareLocal(typeof(int));
                
                il.EmitIncrementFor(length, forILocal =>
                {
                    il.EmitLdloc(forILocal);
                    il.EmitStloc(key);

                    // switch... local = Deserialize
                    var defaultCase = il.DefineLabel();
                    var endOfSwitch = il.DefineLabel();

                    il.EmitLdloc(key);
                    il.Emit(OpCodes.Switch, infoList.Select(x => x.SwitchLabel).ToArray());

                    il.Emit(OpCodes.Br, defaultCase);          

                    if (gotoDefault != null)
                    {
                        il.MarkLabel(gotoDefault.Value);
                        
                        il.Emit(OpCodes.Br, defaultCase);
                    }

                    foreach (var item in infoList)
                    {
                        if (item.MemberInfo != null)
                        {
                            il.MarkLabel(item.SwitchLabel);

                            EmitDeserializeValue(il, item, reader, options, localResolver);

                            il.Emit(OpCodes.Br, endOfSwitch);
                        }
                    }

                    // default, only read. reader.ReadNextBlock()
                    il.MarkLabel(defaultCase);

                    reader.EmitLdarg();
                    il.EmitCall(MessagePackReaderTypeInfo.Skip);

                    il.MarkLabel(endOfSwitch);
                });
            }

            // create result union case
            var structLocal = EmitNewObject(il, type, info, infoList);

            // IMessagePackSerializationCallbackReceiver.OnAfterDeserialize()
            if (type.GetTypeInfo().ImplementedInterfaces.Any(x => x == typeof(IMessagePackSerializationCallbackReceiver)))
            {
                // call directly
                var runtimeMethods = type.GetRuntimeMethods().Where(x => x.Name == "OnAfterDeserialize").ToArray();

                if (runtimeMethods.Length == 1)
                {
                    if (info.IsClass)
                    {
                        il.Emit(OpCodes.Dup);
                    }
                    else
                    {
                        il.EmitLdloca(structLocal);
                    }

                    il.Emit(OpCodes.Call, runtimeMethods[0]); // don't use EmitCall helper(must use 'Call')
                }
                else
                {
                    if (info.IsStruct)
                    {
                        il.EmitLdloc(structLocal);
                        il.Emit(OpCodes.Box, type);
                    }
                    else
                    {
                        il.Emit(OpCodes.Dup);
                    }

                    il.EmitCall(onAfterDeserialize);
                }
            }

            if (info.IsStruct)
            {
                il.Emit(OpCodes.Ldloc, structLocal);
            }
        }

        static void EmitDeserializeValue(ILGenerator il, DeserializeInfo info, ArgumentField reader, ArgumentField options, LocalBuilder localResolver)
        {
            var member = info.MemberInfo;
            var t = member.Type;
            
            if (IsOptimizeTargetType(t))
            {
                reader.EmitLdarg();

                if (t == typeof(byte[]))
                {
                    LocalBuilder local = il.DeclareLocal(typeof(ReadOnlySequence<byte>?));

                    il.EmitCall(MessagePackReaderTypeInfo.ReadBytes);
                    il.EmitStloc(local);
                    il.EmitLdloca(local);
                    il.EmitCall(ArrayFromNullableReadOnlySequence);
                }
                else
                {                   
                    il.EmitCall(MessagePackReaderTypeInfo.TypeInfo.GetDeclaredMethods("Read" + t.Name).First(x => x.GetParameters().Length == 0));
                }
            }
            else
            {
                il.EmitLdloc(localResolver);
                il.EmitCall(getFormatterWithVerify.MakeGenericMethod(t));
                reader.EmitLdarg();
                options.EmitLoad();
                il.EmitCall(getDeserialize(t));
            }
            
            il.EmitStloc(info.LocalField);
        }

        static LocalBuilder EmitNewObject(ILGenerator il, Type type, UnionSerializationInfo info, DeserializeInfo[] members)
        {
            if (info.IsClass)
            {
                foreach (var item in info.MethodParameters)
                {
                    var local = members.First(x => x.MemberInfo == item);

                    il.EmitLdloc(local.LocalField);
                }

                il.Emit(OpCodes.Call, info.NewMethod);

                return null;
            }
            else
            {
                var result = il.DeclareLocal(type);
             
                foreach (var item in info.MethodParameters)
                {
                    var local = members.First(x => x.MemberInfo == item);

                    il.EmitLdloc(local.LocalField);
                }

                il.Emit(OpCodes.Call, info.NewMethod);
                il.Emit(OpCodes.Stloc, result);

                return result; // struct returns local result field
            }
        }
        
        static bool IsOptimizeTargetType(Type type)
        {
            return type == typeof(Int16)
                || type == typeof(Int32)
                || type == typeof(Int64)
                || type == typeof(UInt16)
                || type == typeof(UInt32)
                || type == typeof(UInt64)
                || type == typeof(Single)
                || type == typeof(Double)
                || type == typeof(bool)
                || type == typeof(byte)
                || type == typeof(sbyte)
                || type == typeof(char);
                //|| type == typeof(byte[]);
        }

        #region EmitInfos...

        private static readonly Type refMessagePackWriter = typeof(MessagePackWriter).MakeByRefType();
        private static readonly Type refMessagePackReader = typeof(MessagePackReader).MakeByRefType();

        private static readonly MethodInfo ReadOnlySpanFromByteArray = typeof(ReadOnlySpan<byte>).GetRuntimeMethod("op_Implicit", new[] { typeof(byte[]) });
        private static readonly MethodInfo ReadStringSpan = typeof(CodeGenHelpers).GetRuntimeMethod(nameof(CodeGenHelpers.ReadStringSpan), new[] { typeof(MessagePackReader).MakeByRefType() });
        private static readonly MethodInfo ArrayFromNullableReadOnlySequence = typeof(CodeGenHelpers).GetRuntimeMethod(nameof(CodeGenHelpers.GetArrayFromNullableSequence), new[] { typeof(ReadOnlySequence<byte>?).MakeByRefType() });
        private static readonly MethodInfo GetEncodedStringBytes = typeof(CodeGenHelpers).GetRuntimeMethod(nameof(CodeGenHelpers.GetEncodedStringBytes), new[] { typeof(string) });       

        private static readonly MethodInfo getFormatterWithVerify = typeof(FormatterResolverExtensions).GetRuntimeMethods().First(x => x.Name == nameof(FormatterResolverExtensions.GetFormatterWithVerify));
        private static readonly MethodInfo getResolverFromOptions = typeof(MessagePackSerializerOptions).GetRuntimeProperty(nameof(MessagePackSerializerOptions.Resolver)).GetMethod;

        private static readonly Func<Type, MethodInfo> getSerialize = t => typeof(IMessagePackFormatter<>).MakeGenericType(t).GetRuntimeMethod("Serialize", new[] { refMessagePackWriter, t, typeof(MessagePackSerializerOptions) });
        private static readonly Func<Type, MethodInfo> getDeserialize = t => typeof(IMessagePackFormatter<>).MakeGenericType(t).GetRuntimeMethod("Deserialize", new[] { refMessagePackReader, typeof(MessagePackSerializerOptions) });

        private static readonly ConstructorInfo messagePackSerializationExceptionMessageOnlyConstructor = typeof(MessagePackSerializationException).GetTypeInfo().DeclaredConstructors.First(x =>
        {
            ParameterInfo[] p = x.GetParameters();
            return p.Length == 1 && p[0].ParameterType == typeof(string);
        });

        private static readonly Func<Type, MethodInfo> getTag = type => type.GetTypeInfo().GetProperty("Tag").GetGetMethod();

        private static readonly MethodInfo onBeforeSerialize = typeof(IMessagePackSerializationCallbackReceiver).GetRuntimeMethod(nameof(IMessagePackSerializationCallbackReceiver.OnBeforeSerialize), Type.EmptyTypes);
        private static readonly MethodInfo onAfterDeserialize = typeof(IMessagePackSerializationCallbackReceiver).GetRuntimeMethod(nameof(IMessagePackSerializationCallbackReceiver.OnAfterDeserialize), Type.EmptyTypes);

        private static readonly ConstructorInfo objectCtor = typeof(object).GetTypeInfo().DeclaredConstructors.First(x => x.GetParameters().Length == 0);

        private static MethodInfo BreakDebugger = typeof(System.Diagnostics.Debugger).GetRuntimeMethod(nameof(System.Diagnostics.Debugger.Break), Type.EmptyTypes);

        #endregion

        private static class MessagePackReaderTypeInfo
        {
            internal static TypeInfo TypeInfo = typeof(MessagePackReader).GetTypeInfo();

            internal static MethodInfo ReadArrayHeader = typeof(MessagePackReader).GetRuntimeMethod(nameof(MessagePackReader.ReadArrayHeader), Type.EmptyTypes);
            internal static MethodInfo ReadMapHeader = typeof(MessagePackReader).GetRuntimeMethod(nameof(MessagePackReader.ReadMapHeader), Type.EmptyTypes);

            internal static MethodInfo ReadBytes = typeof(MessagePackReader).GetRuntimeMethod(nameof(MessagePackReader.ReadBytes), Type.EmptyTypes);
            internal static MethodInfo ReadInt32 = typeof(MessagePackReader).GetRuntimeMethod(nameof(MessagePackReader.ReadInt32), Type.EmptyTypes);
            internal static MethodInfo TryReadNil = typeof(MessagePackReader).GetRuntimeMethod(nameof(MessagePackReader.TryReadNil), Type.EmptyTypes);           
            internal static MethodInfo Skip = typeof(MessagePackReader).GetRuntimeMethod(nameof(MessagePackReader.Skip), Type.EmptyTypes);
        }

        private static class MessagePackWriterTypeInfo
        {
            internal static TypeInfo TypeInfo = typeof(MessagePackWriter).GetTypeInfo();

            internal static MethodInfo WriteArrayHeader = typeof(MessagePackWriter).GetRuntimeMethod(nameof(MessagePackWriter.WriteArrayHeader), new[] { typeof(int) });
            internal static MethodInfo WriteMapHeader = typeof(MessagePackWriter).GetRuntimeMethod(nameof(MessagePackWriter.WriteMapHeader), new[] { typeof(int) });            
            
            internal static MethodInfo WriteBytes = typeof(MessagePackWriter).GetRuntimeMethod(nameof(MessagePackWriter.Write), new [] { typeof(ReadOnlySpan<byte>) });
            internal static readonly MethodInfo WriteInt32 = typeof(MessagePackWriter).GetRuntimeMethod(nameof(MessagePackWriter.Write), new[] { typeof(int) });            
            internal static MethodInfo WriteNil = typeof(MessagePackWriter).GetRuntimeMethod(nameof(MessagePackWriter.WriteNil), Type.EmptyTypes);
            internal static MethodInfo WriteRaw = typeof(MessagePackWriter).GetRuntimeMethod(nameof(MessagePackWriter.WriteRaw), new[] { typeof(ReadOnlySpan<byte>) });     
        }

        private class DeserializeInfo
        {
            public UnionSerializationInfo.EmittableMember MemberInfo { get; set; }
            public LocalBuilder LocalField { get; set; }
            public Label SwitchLabel { get; set; }
        }
    }
}
