﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

namespace Utf8Json.Internal.Emit
{
    internal class MetaType
    {
        public Type Type { get; private set; }
        public bool IsClass { get; private set; }
        public bool IsStruct { get { return !IsClass; } }

        public ConstructorInfo BestmatchConstructor { get; private set; }
        public MetaMember[] ConstructorParameters { get; private set; }
        public MetaMember[] Members { get; private set; }

        public MetaType(Type type, Func<string, string> nameMutetor, bool allowPrivate)
        {
            var ti = type.GetTypeInfo();
            var isClass = ti.IsClass || ti.IsInterface || ti.IsAbstract;

            this.Type = type;
            var stringMembers = new Dictionary<string, MetaMember>();

            {
                // All public members are serialize target except [Ignore] member.
                foreach (var item in type.GetRuntimeProperties())
                {
                    if (item.GetCustomAttribute<IgnoreDataMemberAttribute>(true) != null) continue;

                    var dm = item.GetCustomAttribute<DataMemberAttribute>(true);
                    var name = (dm != null && dm.Name != null) ? dm.Name : nameMutetor(item.Name);

                    var member = new MetaMember(item, name, allowPrivate);
                    if (!member.IsReadable && !member.IsWritable) continue;

                    if (stringMembers.ContainsKey(member.Name))
                    {
                        throw new InvalidOperationException("same (custom)name is in type. Type:" + type.Name + " Name:" + member.Name);
                    }
                    stringMembers.Add(member.Name, member);
                }
                foreach (var item in type.GetRuntimeFields())
                {
                    if (item.GetCustomAttribute<IgnoreDataMemberAttribute>(true) != null) continue;
                    if (item.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>(true) != null) continue;
                    if (item.IsStatic) continue;
                    if (item.Name.StartsWith("<")) continue; // compiler generated field(anonymous type, etc...)

                    var dm = item.GetCustomAttribute<DataMemberAttribute>(true);
                    var name = (dm != null && dm.Name != null) ? dm.Name : nameMutetor(item.Name);

                    var member = new MetaMember(item, name, allowPrivate);
                    if (!member.IsReadable && !member.IsWritable) continue;

                    if (stringMembers.ContainsKey(member.Name))
                    {
                        throw new InvalidOperationException("same (custom)name is in type. Type:" + type.Name + " Name:" + member.Name);
                    }
                    stringMembers.Add(member.Name, member);
                }
            }

            // GetConstructor
            var ctor = ti.DeclaredConstructors.Where(x => x.IsPublic)
                .SingleOrDefault(x => x.GetCustomAttribute<SerializationConstructorAttribute>(false) != null);
            var constructorParameters = new List<MetaMember>();
            {
                IEnumerator<ConstructorInfo> ctorEnumerator = null;
                if (ctor == null)
                {
                    // descending.
                    ctorEnumerator = ti.DeclaredConstructors.Where(x => x.IsPublic).OrderByDescending(x => x.GetParameters().Length).GetEnumerator();
                    if (ctorEnumerator.MoveNext())
                    {
                        ctor = ctorEnumerator.Current;
                    }
                }

                if (ctor != null)
                {
                    var constructorLookupDictionary = stringMembers.ToLookup(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);
                    do
                    {
                        constructorParameters.Clear();
                        var ctorParamIndex = 0;
                        foreach (var item in ctor.GetParameters())
                        {
                            MetaMember paramMember;

                            var hasKey = constructorLookupDictionary[item.Name];
                            var len = hasKey.Count();
                            if (len != 0)
                            {
                                if (len != 1)
                                {
                                    if (ctorEnumerator != null)
                                    {
                                        ctor = null;
                                        continue;
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException("duplicate matched constructor parameter name:" + type.FullName + " parameterName:" + item.Name + " paramterType:" + item.ParameterType.Name);
                                    }
                                }

                                paramMember = hasKey.First().Value;
                                if (item.ParameterType == paramMember.Type && paramMember.IsReadable)
                                {
                                    constructorParameters.Add(paramMember);
                                }
                                else
                                {
                                    if (ctorEnumerator != null)
                                    {
                                        ctor = null;
                                        continue;
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException("can't find matched constructor parameter, parameterType mismatch. type:" + type.FullName + " parameterName:" + item.Name + " paramterType:" + item.ParameterType.Name);
                                    }
                                }
                            }
                            else
                            {
                                if (ctorEnumerator != null)
                                {
                                    ctor = null;
                                    continue;
                                }
                                else
                                {
                                    throw new InvalidOperationException("can't find matched constructor parameter, index not found. type:" + type.FullName + " parameterName:" + item.Name);
                                }
                            }
                            ctorParamIndex++;
                        }
                    } while (TryGetNextConstructor(ctorEnumerator, ref ctor));

                    if (ctor == null)
                    {
                        throw new InvalidOperationException("can't find matched constructor. type:" + type.FullName);
                    }
                }
            }

            this.IsClass = isClass;
            this.BestmatchConstructor = ctor;
            this.ConstructorParameters = constructorParameters.ToArray();
            this.Members = stringMembers.Values.ToArray();
        }

        static bool TryGetNextConstructor(IEnumerator<ConstructorInfo> ctorEnumerator, ref ConstructorInfo ctor)
        {
            if (ctorEnumerator == null || ctor != null)
            {
                return false;
            }

            if (ctorEnumerator.MoveNext())
            {
                ctor = ctorEnumerator.Current;
                return true;
            }
            else
            {
                ctor = null;
                return false;
            }
        }
    }
}
