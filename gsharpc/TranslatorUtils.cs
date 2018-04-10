using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using UvmCoreLib;

namespace gsharpc
{
    public class TranslatorUtils
    {

        public static bool IsMainClass(TypeDefinition typeDefinition)
        {
            if (typeDefinition.Methods.Count < 1)
            {
                return false;
            }
            foreach (var m in typeDefinition.Methods)
            {
                if (m.Name == "Main" && !m.IsStatic)
                {
                    return true;
                }
            }
            return false;
        }


        public static bool IsEventEmitterType(TypeDefinition typeDefinition)
        {
            var interfaces = typeDefinition.Interfaces;
            foreach (var item in interfaces)
            {
                if (item.FullName == typeof(IUvmEventEmitter).FullName)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsContractType(TypeDefinition typeDefinition)
        {
            return typeDefinition.BaseType != null
              && typeDefinition.BaseType.FullName.StartsWith("UvmCoreLib.UvmContract");
        }

        public static string GetEventNameFromEmitterMethodName(MethodReference method)
        {
            var methodName = method.Name;
            if (methodName.StartsWith("Emit") && methodName.Length > "Emit".Length)
            {
                // 方法名称符合 EmitXXXX
                if (method.HasThis)
                {
                    throw new Exception("Emit事件的方法必须是static的");
                }
                if (method.Parameters.Count != 1 || method.Parameters[0].ParameterType.FullName != typeof(string).FullName)
                {
                    throw new Exception("Emit事件的方法必须是有且只有一个字符串类型的参数");
                }
                var eventName = methodName.Substring("Emit".Length);
                return eventName;
            }
            else
            {
                return null;
            }
        }

        public static IList<string> GetEmitEventNamesFromEventEmitterType(TypeDefinition typeDefinition)
        {
            var result = new List<string>();
            foreach (var methodDefinition in typeDefinition.Methods)
            {
                var methodName = methodDefinition.Name;
                var eventName = GetEventNameFromEmitterMethodName(methodDefinition);
                if (eventName != null)
                {
                    result.Add(eventName);
                }
            }
            return result;
        }

        public static bool IsContructor(MethodReference method)
        {
            return method.Name == ".ctor";
        }

        public static bool IsContractApiMethod(MethodReference method)
        {
            if (IsContructor(method))
            {
                return false; ; // 构造函数不算API
            }
            if (method is MethodDefinition && !(method as MethodDefinition).IsPublic)
            {
                return false; ; // 非public方法不算API
            }
            // 要求公开且非构造函数的方法必须都是API
            return true;
        }

        public static void LoadContractTypeInfo(TypeDefinition contractType,
          IList<string> contractApiNames, IList<string> contractOfflineApiNames,
           Dictionary<string, IList<UvmTypeInfoEnum>> contractApiArgsTypes)
        {
            foreach (var methodDefinition in contractType.Methods)
            {
                if (!IsContractApiMethod(methodDefinition))
                {
                    continue;
                }
                var methodName = methodDefinition.Name;
                // 要求公开且非构造函数的方法必须都是API
                contractApiNames.Add(methodName);
                if (methodName.StartsWith("Offline") && methodName.Length > "Offline".Length)
                {
                    contractOfflineApiNames.Add(methodName);
                }
                // api的参数列表信息
                var methodParams = methodDefinition.Parameters;
                var apiArgs = new List<UvmTypeInfoEnum>();
                foreach (var methodParam in methodParams)
                {
                    if (methodParam.ParameterType is TypeReference)
                    {
                        apiArgs.Add(GetUvmTypeInfoFromType(methodParam.ParameterType as TypeReference));
                    }
                }
                contractApiArgsTypes[methodName] = apiArgs;
            }
        }

        public static string MakeProtoName(MethodReference method)
        {
            var protoName = method.DeclaringType.FullName + "__" + method.Name;
            protoName = protoName.Replace('.', '_');
            protoName = protoName.Replace('`', '_');
            protoName = protoName.Replace('<', '_');
            protoName = protoName.Replace('>', '_');
            return protoName;
        }

        public static string MakeProtoNameOfTypeConstructor(TypeReference typeRef)
        {
            var protoName = typeRef.FullName;
            protoName = protoName.Replace('.', '_');
            protoName = protoName.Replace('`', '_');
            protoName = protoName.Replace('<', '_');
            protoName = protoName.Replace('>', '_');
            return protoName;
        }


        public static StorageValueTypes GetStorageValueTypeFromType(TypeReference typeRef)
        {
            var typeFullName = typeRef.FullName;
            if (typeFullName == typeof(string).FullName)
            {
                return StorageValueTypes.storage_value_string;
            }
            if (typeFullName == typeof(int).FullName || typeFullName == typeof(long).FullName)
            {
                return StorageValueTypes.storage_value_int;
            }
            if (typeFullName == typeof(float).FullName || typeFullName == typeof(double).FullName)
            {
                return StorageValueTypes.storage_value_number;
            }
            if (typeFullName == typeof(bool).FullName)
            {
                return StorageValueTypes.storage_value_bool;
            }
            if (typeRef is GenericInstanceType)
            {
                var genericTypeRef = typeRef as GenericInstanceType;
                if (genericTypeRef.GenericArguments.Count == 1)
                {
                    var innerType = genericTypeRef.GenericArguments[0] as TypeReference;
                    var innerValueType = GetStorageValueTypeFromType(innerType);
                    string outType = null;
                    if (genericTypeRef.FullName.StartsWith("UvmCoreLib.UvmArray"))
                    {
                        outType = "Array";
                        switch (innerValueType)
                        {
                            case StorageValueTypes.storage_value_bool:
                                return StorageValueTypes.storage_value_bool_array;
                            case StorageValueTypes.storage_value_int:
                                return StorageValueTypes.storage_value_int_array;
                            case StorageValueTypes.storage_value_number:
                                return StorageValueTypes.storage_value_number_array;
                            case StorageValueTypes.storage_value_string:
                                return StorageValueTypes.storage_value_string_array;
                            default:
                                throw new Exception("合约storage不支持Array<非基本类型>");
                        }
                    }
                    else if (genericTypeRef.FullName.StartsWith("UvmCoreLib.UvmMap"))
                    {
                        outType = "Map";
                        switch (innerValueType)
                        {
                            case StorageValueTypes.storage_value_bool:
                                return StorageValueTypes.storage_value_bool_table;
                            case StorageValueTypes.storage_value_int:
                                return StorageValueTypes.storage_value_int_table;
                            case StorageValueTypes.storage_value_number:
                                return StorageValueTypes.storage_value_number_table;
                            case StorageValueTypes.storage_value_string:
                                return StorageValueTypes.storage_value_string_table;
                            default:
                                throw new Exception("合约storage不支持Map<非基本类型>");
                        }
                    }
                    else
                    {
                        throw new Exception("不支持合约的storage类型的属性是类型" + genericTypeRef);
                    }
                }
            }
            throw new Exception("not supported storage value type " + typeRef + " now");
        }

        public static UvmTypeInfoEnum GetUvmTypeInfoFromType(TypeReference typeRef)
        {
            var typeFullName = typeRef.FullName;
            if (typeFullName == typeof(string).FullName)
            {
                return UvmTypeInfoEnum.LTI_STRING;
            }
            if (typeFullName == typeof(int).FullName || typeFullName == typeof(long).FullName)
            {
                return UvmTypeInfoEnum.LTI_INT;
            }
            if (typeFullName == typeof(float).FullName || typeFullName == typeof(double).FullName)
            {
                return UvmTypeInfoEnum.LTI_NUMBER;
            }
            if (typeFullName == typeof(bool).FullName)
            {
                return UvmTypeInfoEnum.LTI_BOOL;
            }
            if (typeRef is GenericInstanceType)
            {
                var genericTypeRef = typeRef as GenericInstanceType;
                if (genericTypeRef.FullName.StartsWith("UvmCoreLib.UvmArray"))
                {
                    return UvmTypeInfoEnum.LTI_ARRAY;
                }
                else if (genericTypeRef.FullName.StartsWith("UvmCoreLib.UvmMap"))
                {
                    return UvmTypeInfoEnum.LTI_MAP;
                }
                else
                {
                    throw new Exception("不支持类型" + genericTypeRef);
                }
            }
            throw new Exception("not supported storage value type " + typeRef + " now");
        }

        private static char[] toEscape = "\0\x1\x2\x3\x4\x5\x6\a\b\t\n\v\f\r\xe\xf\x10\x11\x12\x13\x14\x15\x16\x17\x18\x19\x1a\x1b\x1c\x1d\x1e\x1f\"\\".ToCharArray();
        private static string[] literals = @"\0,\x0001,\x0002,\x0003,\x0004,\x0005,\x0006,\a,\b,\t,\n,\v,\f,\r,\x000e,\x000f,\x0010,\x0011,\x0012,\x0013,\x0014,\x0015,\x0016,\x0017,\x0018,\x0019,\x001a,\x001b,\x001c,\x001d,\x001e,\x001f".Split(new char[] { ',' });

        public static string Escape(string input)
        {
            int i = input.IndexOfAny(toEscape);
            if (i < 0) return input;

            var sb = new System.Text.StringBuilder(input.Length + 5);
            int j = 0;
            do
            {
                sb.Append(input, j, i - j);
                var c = input[i];
                if (c < 0x20) sb.Append(literals[c]); else sb.Append(@"\").Append(c);
            } while ((i = input.IndexOfAny(toEscape, j = ++i)) > 0);

            return sb.Append(input, j, input.Length - j).ToString();
        }

    }
}
