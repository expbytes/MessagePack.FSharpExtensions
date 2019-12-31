// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace MessagePack.FSharp.Internal {
    
    internal class UnionSerializationInfo
    {
        public bool IsIntKey { get; set; }
        public bool IsStringKey { get { return !IsIntKey; } }
        public bool IsClass { get; set; }
        public bool IsStruct { get { return !IsClass; } }
        public MethodInfo NewMethod { get; set; }
        public EmittableMember[] MethodParameters { get; set; }
        public EmittableMember[] Members { get; set; }

        UnionSerializationInfo() { }

        public static UnionSerializationInfo CreateOrNull(Type type, Microsoft.FSharp.Reflection.UnionCaseInfo caseInfo)
        {
            type = caseInfo.DeclaringType;

            var ti = type.GetTypeInfo();
            var isClass = ti.IsClass;

            var contractAttr = ti.GetCustomAttribute<MessagePackObjectAttribute>();

            var isIntKey = true;
            var intMembers = new Dictionary<int, EmittableMember>();
            var stringMembers = new Dictionary<string, EmittableMember>();

            if (contractAttr == null || contractAttr.KeyAsPropertyName)
            {
                isIntKey = false;

                var hiddenIntKey = 0;
                foreach (var item in caseInfo.GetFields())
                {
                    var member = new EmittableMember
                    {
                        PropertyInfo = item,
                        StringKey = item.Name,
                        IntKey = hiddenIntKey++
                    };
                    stringMembers.Add(member.StringKey, member);
                }
            }
            else
            {
                var hiddenIntKey = 0;
                foreach (var item in caseInfo.GetFields())
                {

                    var member = new EmittableMember
                    {
                        PropertyInfo = item,
                        IntKey = hiddenIntKey++
                    };
                    intMembers.Add(member.IntKey, member);
                }
            }

            MethodInfo method;
            var methodParameters = new List<EmittableMember>();

            if (caseInfo.GetFields().Any())
            {
                method = ti.GetMethod("New" + caseInfo.Name, BindingFlags.Static | BindingFlags.Public);
                if (method == null) throw new MessagePackDynamicUnionResolverException("can't find public method. case:" + caseInfo.Name);

                var methodLookupDictionary = stringMembers.ToLookup(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);

                var methodParamIndex = 0;
                foreach (var item in method.GetParameters())
                {
                    EmittableMember paramMember;
                    if (isIntKey)
                    {
                        if (intMembers.TryGetValue(methodParamIndex, out paramMember))
                        {
                            if (item.ParameterType == paramMember.Type)
                            {
                                methodParameters.Add(paramMember);
                            }
                            else
                            {
                                throw new MessagePackDynamicUnionResolverException("can't find matched method parameter, parameterType mismatch. case:" + caseInfo.Name + " parameterIndex:" + methodParamIndex + " paramterType:" + item.ParameterType.Name);
                            }
                        }
                        else
                        {
                            throw new MessagePackDynamicUnionResolverException("can't find matched method parameter, index not found. case:" + caseInfo.Name + " parameterIndex:" + methodParamIndex);
                        }
                    }
                    else
                    {
                        var hasKey = methodLookupDictionary[item.Name];
                        var len = hasKey.Count();
                        if (len != 0)
                        {
                            if (len != 1)
                            {
                                throw new MessagePackDynamicUnionResolverException("duplicate matched method parameter name:" + caseInfo.Name + " parameterName:" + item.Name + " paramterType:" + item.ParameterType.Name);
                            }

                            paramMember = hasKey.First().Value;
                            if (item.ParameterType == paramMember.Type)
                            {
                                methodParameters.Add(paramMember);
                            }
                            else
                            {
                                throw new MessagePackDynamicUnionResolverException("can't find matched method parameter, parameterType mismatch. case:" + caseInfo.Name + " parameterName:" + item.Name + " paramterType:" + item.ParameterType.Name);
                            }
                        }
                        else
                        {
                            throw new MessagePackDynamicUnionResolverException("can't find matched method parameter, index not found. case:" + caseInfo.Name + " parameterName:" + item.Name);
                        }
                    }
                    methodParamIndex++;
                }
            }
            else
            {
                method = ti.GetProperty(caseInfo.Name, BindingFlags.Public | BindingFlags.Static).GetGetMethod();
            }

            return new UnionSerializationInfo
            {
                IsClass = isClass,
                NewMethod = method,
                MethodParameters = methodParameters.ToArray(),
                IsIntKey = isIntKey,
                Members = (isIntKey) ? intMembers.Values.ToArray() : stringMembers.Values.ToArray()
            };
        }

        public class EmittableMember
        {
            public int IntKey { get; set; }
            public string StringKey { get; set; }
            public Type Type { get { return PropertyInfo.PropertyType; } }
            public PropertyInfo PropertyInfo { get; set; }
            public bool IsValueType
            {
                get
                {
                    return ((MemberInfo)PropertyInfo).DeclaringType.GetTypeInfo().IsValueType;
                }
            }

            public void EmitLoadValue(ILGenerator il)
            {
                il.EmitCall(PropertyInfo.GetGetMethod());
            }
        }
    }
}