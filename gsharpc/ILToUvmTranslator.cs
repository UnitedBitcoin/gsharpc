using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

namespace gsharpc
{
    public class ILToUvmTranslator
    {
        public IList<UvmInstruction> GeneratedInstructions { get; set; }

        public IList<string> EventNames { get; set; }

        public IList<string> ContractApiNames { get; set; }

        public IList<string> ContractOfflineApiNames { get; set; }

        // 合约的storage的属性的类型信息
        public Dictionary<string, StorageValueTypes> ContractStoragePropertiesTypes { get; set; }

        // 合约的API的参数列表类型信息
        public Dictionary<string, IList<UvmTypeInfoEnum>> ContractApiArgsTypes { get; set; }

        public TypeDefinition ContractType { get; set; }

        public IList<TypeDefinition> DefinedTypes { get; set; }

        public ILToUvmTranslator()
        {
            this.GeneratedInstructions = new List<UvmInstruction>();
            this.EventNames = new List<string>();
            this.ContractApiNames = new List<string>();
            this.ContractOfflineApiNames = new List<string>();
            this.ContractStoragePropertiesTypes = new Dictionary<string, StorageValueTypes>();
            this.ContractApiArgsTypes = new Dictionary<string, IList<UvmTypeInfoEnum>>();
            this.DefinedTypes = new List<TypeDefinition>();
        }

        /**
         * 获取一些元信息，比如emit的event names, 合约的apis, 合约的offline apis
         */
        public string GetMetaInfoJson()
        {
            var info = new Dictionary<string, object>();
            info["event"] = EventNames;
            info["api"] = ContractApiNames;
            info["offline_api"] = ContractOfflineApiNames;
            var storagePropertiesTypesArray = new List<List<object>>();
            foreach (var key in ContractStoragePropertiesTypes.Keys)
            {
                storagePropertiesTypesArray.Add(new List<object>() { key, ContractStoragePropertiesTypes[key] });
            }
            info["storage_properties_types"] = storagePropertiesTypesArray;
            var contractApiArgsTypesArray = new List<List<object>>();
            foreach (var key in ContractApiArgsTypes.Keys)
            {
                contractApiArgsTypesArray.Add(new List<object>() { key, ContractApiArgsTypes[key] });
            }
            info["api_args_types"] = contractApiArgsTypesArray;
            return JsonConvert.SerializeObject(info);
        }

        public void TranslateModule(ModuleDefinition module, StringBuilder ilContentBuilder, StringBuilder uvmAsmBuilder)
        {
            GeneratedInstructions.Clear();
            EventNames.Clear();
            ContractApiNames.Clear();
            ContractOfflineApiNames.Clear();
            ContractStoragePropertiesTypes.Clear();
            ContractApiArgsTypes.Clear();
            DefinedTypes.Clear();
            this.ContractType = null;

            DefinedTypes = (from t in module.Types where !t.FullName.Equals("<Module>") select t).ToList();

            var MainTypes = (from t in module.Types where TranslatorUtils.IsMainClass(t) select t).ToList();
            if (MainTypes.Count != 1)
            {
                throw new Exception("必需提供1个且只有1个Main方法(非static)的类型");
            }
            var eventEmitterTypes = (from t in module.Types where TranslatorUtils.IsEventEmitterType(t) select t).ToList();
            foreach (var emitterType in eventEmitterTypes)
            {
                var eventNames = TranslatorUtils.GetEmitEventNamesFromEventEmitterType(emitterType);
                foreach (var eventName in eventNames)
                {
                    if (!EventNames.Contains(eventName))
                    {
                        EventNames.Add(eventName);
                    }
                }
            }
            var contractTypes = (from t in module.Types where TranslatorUtils.IsContractType(t) select t).ToList();
            if (contractTypes.Count > 1)
            {
                throw new Exception("暂时不支持一个文件中定义超过1个合约类型");
            }
            // 合约类型不直接定义，但是MainType里用到合约类型时，调用构造函数时需要去调用对应构造函数并设置各成员函数
            if (contractTypes.Count > 0)
            {
                this.ContractType = contractTypes[0];
                if (TranslatorUtils.IsMainClass(this.ContractType))
                {
                    throw new Exception("合约类型不能直接包含Main方法，请另外定义一个类型包含Main方法");
                }
                // 抽取合约API列表和offline api列表
                TranslatorUtils.LoadContractTypeInfo(this.ContractType, ContractApiNames, ContractOfflineApiNames, ContractApiArgsTypes);

                if (ContractType.BaseType != null && ContractType.BaseType is GenericInstanceType
                  && (this.ContractType.BaseType as GenericInstanceType).GenericArguments.Count > 0)
                {
                    var storageType = (this.ContractType.BaseType as GenericInstanceType).GenericArguments[0] as TypeDefinition;
                    foreach (var storageField in storageType.Properties)
                    {
                        var storagePropName = storageField.Name;
                        var storageValueType = TranslatorUtils.GetStorageValueTypeFromType(storageField.PropertyType as TypeReference);
                        this.ContractStoragePropertiesTypes[storagePropName] = storageValueType;
                    }
                    foreach (var storageField in storageType.Fields)
                    {
                        var storagePropName = storageField.Name;
                        if (storagePropName.Contains("<"))
                        {
                            // property自动产生的field，不算
                            continue;
                        }
                        var storageValueType = TranslatorUtils.GetStorageValueTypeFromType(storageField.FieldType as TypeReference);
                        this.ContractStoragePropertiesTypes[storagePropName] = storageValueType;
                    }
                }
            }
            UvmProto mainProto = null;
            foreach (var typeDefinition in MainTypes)
            {
                if (typeDefinition.FullName.Equals("<Module>"))
                {
                    continue;
                }
                var proto = TranslateILType(typeDefinition, ilContentBuilder, uvmAsmBuilder, true, null);
                uvmAsmBuilder.Append(proto.ToUvmAss(true));
                mainProto = proto;
            }
            // TODO: 考虑把所有proto放到一个级别，放在main下面，统一通过upval或者getuptab访问
            if (this.ContractType != null)
            {
                var proto = TranslateILType(this.ContractType, ilContentBuilder, uvmAsmBuilder, false, mainProto.FindMainProto());
                uvmAsmBuilder.Append(proto.ToUvmAss(false));
            }

          
        }

        private UvmProto TranslateILType(TypeDefinition typeDefinition, StringBuilder ilContentBuilder,
          StringBuilder uvmAsmBuilder, bool isMainType, UvmProto parentProto)
        {
            var proto = new UvmProto(TranslatorUtils.MakeProtoNameOfTypeConstructor(typeDefinition));
            proto.Parent = parentProto;
            if (parentProto != null)
            {
                parentProto.SubProtos.Add(proto);
            }
            // 把类型转换成的proto被做成有一些slot指向成员函数的构造函数，保留slot指向成员函数是为了方便子对象upval访问(不一定需要)
            var tableSlot = 0;
            proto.AddInstructionLine(UvmOpCodeEnums.OP_NEWTABLE, "newtable %" + tableSlot + " 0 0", null);
            var tmp1Slot = typeDefinition.Methods.Count + 1;

            foreach (var m in typeDefinition.Methods)
            {
                var methodProto = TranslateILMethod(m, ilContentBuilder, uvmAsmBuilder, proto);
                if (methodProto == null)
                {
                    continue;
                }
                // 把各成员函数加入slots    
                proto.InternConstantValue(methodProto.Name);
                var slotIndex = proto.SubProtos.Count + 1;
                proto.AddInstructionLine(UvmOpCodeEnums.OP_CLOSURE, "closure %" + slotIndex + " " + methodProto.Name, null);


                proto.InternConstantValue(m.Name);
                proto.AddInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk %" + tmp1Slot + " const \"" + m.Name + "\"", null);
                proto.AddInstructionLine(UvmOpCodeEnums.OP_SETTABLE,
                  "settable %" + tableSlot + " %" + tmp1Slot + " %" + slotIndex, null);
                proto.Locvars.Add(new UvmLocVar() { Name = methodProto.Name, SlotIndex = slotIndex }); 
                proto.SubProtos.Add(methodProto);
            }

            // TODO: 顶层构造函数proto，考虑设置成员函数并且有返回值
            proto.MaxStackSize = tmp1Slot + 1;
            var mainProto = proto.FindMainProto();
            if (mainProto != null && isMainType)
            {
                // TODO: 可能需要Main类中返回合约, 目前返回有问题
                proto.MaxStackSize = tmp1Slot + 4;
                var mainFuncSlot = proto.SubProtos.Count + 2; // proto.SubProtos.IndexOf(mainProto) + 1;
                proto.AddInstructionLine(UvmOpCodeEnums.OP_CLOSURE, "closure %" + mainFuncSlot + " " + mainProto.Name, null);
                proto.AddInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + (mainFuncSlot + 1) + " %0", null);
                var returnCount = (mainProto.method.ReturnType.FullName != typeof(void).FullName) ? 1 : 0;
                var argsCount = 1;
                proto.AddInstructionLine(UvmOpCodeEnums.OP_CALL, "call %" + mainFuncSlot + " " + (argsCount + 1) + " " + (returnCount + 1), null);
                if (returnCount > 0)
                {
                    proto.AddInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %" + mainFuncSlot + " " + (returnCount + 1), null);
                }
                proto.AddInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %0 1", null);
            }
            else
            {
                proto.AddInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %" + tableSlot + " 2", null); // 构造函数的返回值
                proto.AddInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %0 1", null);
            }

            return proto;
        }

        private static string ILVariableNameFromDefinition(VariableDefinition varInfo)
        {
            return varInfo != null ? varInfo.ToString() : "";
        }

        private static string ILParamterNameFromDefinition(ParameterDefinition argInfo)
        {
            return argInfo != null ? argInfo.ToString() : "";
        }

        private void MakeJmpToInstruction(UvmProto proto, Instruction i, string opName,
            Instruction toJmpToInst, IList<UvmInstruction> result, string commentPrefix, bool onlyNeedResultCount)
        {
            // 满足条件，跳转到目标指令
            // 在要跳转的目标指令的前面增加 label:
            var jmpLabel = proto.Name + "_to_dest_" + opName + "_" + i.Offset;
            var toJmpToOffset = toJmpToInst.Offset;
            if (toJmpToOffset < i.Offset)
            {
                var uvmInstToJmp = proto.FindUvmInstructionMappedByIlInstruction(toJmpToInst);
                var idx = proto.IndexOfUvmInstruction(uvmInstToJmp);
                if (idx < 1 && !onlyNeedResultCount)
                {
                    throw new Exception("Can't find mapped instruction to jmp");
                }
                jmpLabel = proto.InternNeedLocationLabel(idx, jmpLabel);
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel + commentPrefix + " " + opName,
                    i));
            }
            else
            {
                // 未来的指令位置
                var toJmpUvmInstsCount = 1;
                var idx1 = proto.method.Body.Instructions.IndexOf(i);
                var idx2 = proto.method.Body.Instructions.IndexOf(toJmpToInst);
                if (idx1 < 0 || idx2 < 0 || idx1 >= idx2)
                {
                    throw new Exception("wrong to jmp instruction index");
                }

                for (var j = idx1 + 1; j < idx2; j++)
                {
                    var oldNotAffectMode = proto.InNotAffectMode;
                    proto.InNotAffectMode = true;
                    var uvmInsts = TranslateILInstruction(proto, proto.method.Body.Instructions[j],
                        "", true); // 因为可能有嵌套情况，这里只需要获取准确的指令数量不需要准确的指令内容
                    proto.InNotAffectMode = oldNotAffectMode;
                    var notEmptyUvmInstsCount = uvmInsts.Count((UvmInstruction uvmInst) =>
                    {
                        return !(uvmInst is UvmEmptyInstruction);
                    });
                    toJmpUvmInstsCount += notEmptyUvmInstsCount;
                }

                //modify by zq
                //int groupIndex = GetGroupIndex(proto, idx1 + 1);
                //int targetGroupIndex = GetGroupIndex(proto, idx2);
                //if (groupIndex < 0 || targetGroupIndex < 0)
                //{
                //    throw new Exception("程序出错 MakeJmpToInstruction");
                //}
                //if (proto.ILInsGroups.ElementAt(targetGroupIndex).ILIndexInMethod != idx2)
                //{
                //    //fixme zq  跳转目的地位于group内，需要拆掉group !!!
                //    throw new Exception("程序出错 跳转目的地不能位于group内 MakeJmpToInstruction ");
                //}

                //ILInstructionGroup ilgroup = null;
                //while (groupIndex < targetGroupIndex)
                //{
                //    var oldNotAffectMode = proto.InNotAffectMode;
                //    proto.InNotAffectMode = true;
                //    ilgroup = proto.ILInsGroups.ElementAt(groupIndex);
                //    var uvmInsts = TranslateILInstructionsGroup(proto, ilgroup, commentPrefix, true);

                //    proto.InNotAffectMode = oldNotAffectMode;
                //    var notEmptyUvmInstsCount = uvmInsts.Count((UvmInstruction uvmInst) =>
                //    {
                //        return !(uvmInst is UvmEmptyInstruction);
                //    });
                //    toJmpUvmInstsCount += notEmptyUvmInstsCount;
                //    groupIndex++;
                //}


                jmpLabel = proto.InternNeedLocationLabel(toJmpUvmInstsCount + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel);
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel + commentPrefix + " " + opName,
                    i));
            }
        }


        private void PopFromEvalStackTopSlot(UvmProto proto, int slotIndex, Instruction i, IList<UvmInstruction> result,
          string commentPrefix)
        {
            UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_POP, "pop %" + slotIndex + commentPrefix, i);
            result.Add(inst);
        }

        private void PushIntoEvalStackTopSlot(UvmProto proto, int slotIndex, Instruction i, IList<UvmInstruction> result,
          string commentPrefix)
        {
           UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_PUSH, "push %" + slotIndex + commentPrefix, i);
           result.Add(inst);
        }


        private void PushStrIntoEvalStackTopSlot(UvmProto proto, string str, Instruction i, IList<UvmInstruction> result,
          string commentPrefix)
        {
            UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_PUSH, "push " + " const  \"" + str + "\"" + commentPrefix + " ldstr", i);
            result.Add(inst);

        }

        private void MakeArithmeticInstructions(UvmProto proto, string uvmOpName, Instruction i, IList<UvmInstruction> result,
          string commentPrefix, bool convertResultTypeBoolIfInt)
        {
            //result.Add(proto.MakeEmptyInstruction(i.ToString()));
            proto.InternConstantValue(1);

            var arg1SlotIndex = proto.tmp3StackTopSlotIndex + 1; // top-1
            var arg2SlotIndex = proto.tmp3StackTopSlotIndex + 2; // top                 

            /*modify by zq */ // LoadNilInstruction(proto, proto.tmp3StackTopSlotIndex, i, result, commentPrefix);
            PopFromEvalStackTopSlot(proto, arg2SlotIndex, i, result, commentPrefix);
            PopFromEvalStackTopSlot(proto, arg1SlotIndex, i, result, commentPrefix);

            // 执行算术操作符，结果存入tmp2
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD, uvmOpName + " %" + proto.tmp2StackTopSlotIndex + " %" + arg1SlotIndex + " %" + arg2SlotIndex + commentPrefix, i));

            if (convertResultTypeBoolIfInt)
            {
                // 判断是否是0，如果是就是false，需要使用jmp
                proto.InternConstantValue(0);
                proto.InternConstantValue(true);
                proto.InternConstantValue(false);
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk %" + proto.tmp1StackTopSlotIndex + " const false" + commentPrefix, i));
                // if tmp2==0 then pc++
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_EQ, "eq 0 %" + proto.tmp2StackTopSlotIndex + " const 0" + commentPrefix, i));

                var labelWhenTrue = proto.Name + "_true_" + i.Offset;
                var labelWhenFalse = proto.Name + "_false_" + i.Offset;
                labelWhenTrue = proto.InternNeedLocationLabel(
                                        2 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), labelWhenTrue);

                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + labelWhenTrue + commentPrefix, i));
                labelWhenFalse =
                       proto.InternNeedLocationLabel(
                           2 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), labelWhenFalse);
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + labelWhenFalse + commentPrefix, i));

                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk %" + proto.tmp1StackTopSlotIndex + " const true" + commentPrefix, i));
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + proto.tmp2StackTopSlotIndex + " %" + proto.tmp1StackTopSlotIndex + commentPrefix, i));
            }
            // 把add结果存入eval stack
            PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " add");
        }

        private void MakeCompareInstructions(UvmProto proto, string compareType, Instruction i, IList<UvmInstruction> result,
          string commentPrefix)
        {
            // 从eval stack弹出两个值(top和top-1)，比较大小，比较结果存入eval stack
            //result.Add(proto.MakeEmptyInstruction(i.ToString()));

            // 消耗eval stack的顶部2个值, 然后比较，比较结果存入eval stack
            // 获取eval stack顶部的值
            proto.InternConstantValue(1);
            var arg1SlotIndex = proto.tmp3StackTopSlotIndex + 1; // top
            var arg2SlotIndex = proto.tmp3StackTopSlotIndex + 2; // top-1
            
            PopFromEvalStackTopSlot(proto, arg1SlotIndex, i, result, commentPrefix);

            // 再次获取eval stack栈顶的值
            PopFromEvalStackTopSlot(proto, arg2SlotIndex, i, result, commentPrefix);

            // 比较arg1和arg2
            // uvm的lt指令: if ((RK(B) <  RK(C)) ~= A) then pc++
            switch (compareType)
            {
                case "gt":
                    {
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                    "lt 0 %" + arg1SlotIndex + " %" + arg2SlotIndex +
                    commentPrefix, i));
                    }
                    break;
                case "lt":
                    {
                        // lt: if ((RK(B) <  RK(C)) ~= A) then pc++
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                            "lt 0 %" + arg2SlotIndex + " %" + arg1SlotIndex +
                            commentPrefix, i));
                    }
                    break;
                case "ne":
                    {
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                   "eq 1 %" + arg1SlotIndex + " %" + arg2SlotIndex +
                   commentPrefix, i));
                    }
                    break;
                default:
                    {
                        // eq: if ((RK(B) == RK(C)) ~= A) then pc++
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                            "eq 0 %" + arg1SlotIndex + " %" + arg2SlotIndex +
                            commentPrefix, i));
                    }
                    break;
            }
            // 满足条件就执行下下条指令(把1压eval stack栈)，否则执行下条jmp指令(把0压eval stack栈)
            // 构造下条jmp指令和下下条指令
            var jmpLabel1 = proto.Name + "_1_cmp_" + i.Offset;
            var offsetOfInst1 = 2; // 如果比较失败，跳转到把0压eval-stack栈的指令
            jmpLabel1 = proto.InternNeedLocationLabel(offsetOfInst1 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel1);
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel1 + commentPrefix,
                i));

            var jmpLabel2 = proto.Name + "_2_cmp_" + i.Offset;
            var offsetOfInst2 = 5; // 如果比较成功，跳转到把1压eval-stack栈的指令  
            jmpLabel2 = proto.InternNeedLocationLabel(offsetOfInst2 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel2);
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel2 + commentPrefix,
                i));

            proto.InternConstantValue(0);
            proto.InternConstantValue(1);


            // 把结果0存入eval stack
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK,
            "loadk %" + proto.tmp2StackTopSlotIndex + " const 0" + commentPrefix, i));
            PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " add");

            // jmp到压栈第1个分支后面
            var jmpLabel3 = proto.Name + "_3_cmp_" + i.Offset;
            var offsetOfInst3 = 3; // modify
            jmpLabel3 = proto.InternNeedLocationLabel(offsetOfInst3 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel3);
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel3 + commentPrefix,
                i));

            // 把结果1存入eval stack
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK,
            "loadk %" + proto.tmp3StackTopSlotIndex + " const 1" + commentPrefix, i));
            PushIntoEvalStackTopSlot(proto, proto.tmp3StackTopSlotIndex, i, result, commentPrefix + " cgt");

        }

        /**
         * 读取并弹出eval stack top-1(table)和top(value)值，top值弹出, 执行table写属性操作，然后table压栈回到eval-stack ???? 弹出2个不需要压回
         */
        private void MakeSetTablePropInstructions(UvmProto proto, string propName, Instruction i, IList<UvmInstruction> result,
          string commentPrefix, bool needConvtToBool)
        {
            proto.InternConstantValue(propName);
            var tableSlot = proto.tmp2StackTopSlotIndex + 1;
            var valueSlot = proto.tmp2StackTopSlotIndex + 2;

            // 加载value     
            PopFromEvalStackTopSlot(proto, valueSlot, i, result, commentPrefix);

            // 对于布尔类型，因为.net中布尔类型参数加载的时候用的ldc.i，加载的是整数，所以这里要进行类型转换成bool类型，使用 not not a来转换
            if (needConvtToBool)
            {
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, "not %" + valueSlot + " %" + valueSlot + commentPrefix, i));
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, "not %" + valueSlot + " %" + valueSlot + commentPrefix, i));
            }
      

            // 加载table    
            PopFromEvalStackTopSlot(proto, tableSlot, i, result, commentPrefix);

            // settable
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK,
              "loadk %" + proto.tmp2StackTopSlotIndex + " const \"" + propName + "\"" + commentPrefix, i));
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE,
              "settable %" + tableSlot + " % " + proto.tmp2StackTopSlotIndex + " %" + valueSlot + commentPrefix, i));

        }

        /**
         * 读取eval stack top(table)值，执行table读属性操作,读取结果放入eval stack  pop 1 push 1
         */
        public void MakeGetTablePropInstructions(UvmProto proto, string propName, Instruction i, IList<UvmInstruction> result,
          string commentPrefix, bool needConvtToBool)
        {
            proto.InternConstantValue(propName);
            var tableSlot = proto.tmp2StackTopSlotIndex + 1;
            var valueSlot = proto.tmp2StackTopSlotIndex + 2;

            // 加载table  
            PopFromEvalStackTopSlot(proto, tableSlot, i, result, commentPrefix);

            // gettable
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK,
              "loadk %" + proto.tmp2StackTopSlotIndex + " const \"" + propName + "\"" + commentPrefix, i));
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABLE,
              "gettable %" + valueSlot + " % " + tableSlot + " %" + proto.tmp2StackTopSlotIndex + commentPrefix, i));

            // 对于布尔类型，因为.net中布尔类型参数加载的时候用的ldc.i，加载的是整数，所以这里要进行类型转换成bool类型，使用 not not a来转换
            if (needConvtToBool)
            {
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, "not %" + valueSlot + " %" + valueSlot + commentPrefix, i));
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, "not %" + valueSlot + " %" + valueSlot + commentPrefix, i));
            }
            proto.InternConstantValue(1);
            // value放回eval stack                                                                      
            PushIntoEvalStackTopSlot(proto, valueSlot, i, result, commentPrefix );
        }

        /**
         * 单元操作符转换成指令  pop 1  push 1
         */
        public void MakeSingleArithmeticInstructions(UvmProto proto, string uvmOpName, Instruction i, IList<UvmInstruction> result,
          string commentPrefix, bool convertResultTypeBoolIfInt)
        {
            //result.Add(proto.MakeEmptyInstruction(i.ToString()));
            proto.InternConstantValue(1);
            var arg1SlotIndex = proto.tmp3StackTopSlotIndex + 1; // top
           
            PopFromEvalStackTopSlot(proto, arg1SlotIndex, i, result, commentPrefix);

            // 执行算术操作符，结果存入tmp2
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, uvmOpName + " %" + proto.tmp2StackTopSlotIndex + " %" + arg1SlotIndex + commentPrefix, i));

            if (convertResultTypeBoolIfInt)
            {
                // 判断是否是0，如果是就是false，需要使用jmp
                proto.InternConstantValue(0);
                proto.InternConstantValue(true);
                proto.InternConstantValue(false);
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk %" + proto.tmp1StackTopSlotIndex + " const false" + commentPrefix, i));
                // if tmp2==0 then pc++
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_EQ, "eq 0 %" + proto.tmp2StackTopSlotIndex + " const 0" + commentPrefix, i));

                var labelWhenTrue = proto.Name + "_true_" + i.Offset;
                var labelWhenFalse = proto.Name + "_false_" + i.Offset;
                labelWhenTrue =
                                    proto.InternNeedLocationLabel(
                                        2 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), labelWhenTrue);

                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + labelWhenTrue + commentPrefix, i));
                labelWhenFalse =
                       proto.InternNeedLocationLabel(
                           2 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), labelWhenFalse);
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + labelWhenFalse + commentPrefix, i));

                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk %" + proto.tmp1StackTopSlotIndex + " const true" + commentPrefix, i));
                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + proto.tmp2StackTopSlotIndex + " %" + proto.tmp1StackTopSlotIndex + commentPrefix, i));
            }

            // 把add结果存入eval stack
            PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix+" add");
        }

        private void MakeGetTopOfEvalStackInst(UvmProto proto, Instruction i, IList<UvmInstruction> result,
          int targetSlot, string commentPrefix)
        {
            //UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABLE, "gettable %" + targetSlot + " %" + proto.evalStackIndex +
            //         " %" + proto.evalStackSizeIndex + commentPrefix, i);
            //inst.EvalOp = EvalStackOp.GetEvalStackTop;
            //result.Add(inst);

            UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTOP, "gettop %" + targetSlot + commentPrefix, i);
            //inst.EvalOp = EvalStackOp.GetEvalStackTop;

            result.Add(inst);
        }

        //private void MakeSetTopOfEvalStackInst(UvmProto proto, Instruction i, IList<UvmInstruction> result,
        //  int valueSlot, string commentPrefix)
        //{
        //    UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE,
        //             "settable %" + proto.evalStackIndex + " %" + proto.evalStackSizeIndex + " %" + valueSlot + commentPrefix, i);
        //    inst.EvalOp = EvalStackOp.SetEvalStackTop;
        //    result.Add(inst);
        //}

        //private void MakeSetTopOfEvalStackStrInst(UvmProto proto, Instruction i, IList<UvmInstruction> result,
        //  string strValue, string commentPrefix)
        //{
        //    // 设置string                          
        //    UvmInstruction inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE,
        //      "settable %" + proto.evalStackIndex + " %" + proto.evalStackSizeIndex + " const \"" + strValue + "\"" + commentPrefix + " ldstr", i);
        //    inst.EvalOp = EvalStackOp.SetEvalStackTop;
        //    result.Add(inst);
        //}

        private void MakeLoadNilInst(UvmProto proto, Instruction i, IList<UvmInstruction> result,
          int targetSlot, string commentPrefix)
        {
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADNIL, "loadnil %" + targetSlot + " 0" + commentPrefix, i));
        }

        private void MakeLoadConstInst(UvmProto proto, Instruction i, IList<UvmInstruction> result,
          int targetSlot, object value, string commentPrefix)
        {
            if (value is string)
            {
                var literalValueInUvms = TranslatorUtils.Escape(value as string);
            }
            var constantIndex = proto.InternConstantValue(value);
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK,
              "loadk %" + targetSlot + " const " + (value is string ? ("\"" + value + "\"") : value) + commentPrefix, i));
        }

        private IList<UvmInstruction> TranslateILInstruction(UvmProto proto, Instruction i, string commentPrefix, bool onlyNeedResultCount)
        {
            var result = new List<UvmInstruction>();
            switch (i.OpCode.Code)
            {
                case Code.Nop:
                    proto.AddNotMappedILInstruction(i);
                    return result;
                case Code.Stloc:
                case Code.Stloc_S:
                case Code.Stloc_0:
                case Code.Stloc_1:
                case Code.Stloc_2:
                case Code.Stloc_3:
                    {
                        // 从evaluation stack 弹出栈顶数据复制到call stack slot
                        // ->uvm. 取出eval stack的长度, 从eval stack的slot中弹出数据到栈顶，然后move到合适slot，然后弹出栈顶
                        int loc;
                        VariableDefinition varInfo = null;
                        if (i.OpCode.Code == Code.Stloc_S)
                        {
                            varInfo = i.Operand as VariableDefinition;
                            loc = varInfo.Index;
                        }
                        else
                        {
                            loc = i.OpCode.Value - 10;
                        }
                        if (loc > proto.maxCallStackSize)
                        {
                            proto.maxCallStackSize = loc;
                        }
                        // 获取eval stack的栈顶值
                        PopFromEvalStackTopSlot(proto, proto.callStackStartIndex+loc, i, result, commentPrefix);
                    }
                    break;
                case Code.Starg:
                case Code.Starg_S:
                    {
                        // 将位于计算堆栈顶部的值存储到位于指定索引的参数槽中
                        int argLoc;
                        ParameterDefinition argInfo = i.Operand as ParameterDefinition;
                        argLoc = argInfo.Index;
                        var argSlot = argLoc;
                        if (proto.method.HasThis)
                        {
                            argSlot++;
                        }
                        // 获取eval stack的栈顶值
                        PopFromEvalStackTopSlot(proto, argSlot, i, result, commentPrefix);
                    }
                    break;
                case Code.Stfld:
                    {
                        // 用新值替换在对象引用或指针的字段中存储的值
                        // top-1是table, top是value 
                        var fieldDefinition = i.Operand as FieldDefinition;
                        var fieldName = fieldDefinition.Name;
                        bool needConvToBool = fieldDefinition.FieldType.FullName == typeof(bool).FullName;
                        MakeSetTablePropInstructions(proto, fieldName, i, result, commentPrefix, needConvToBool);
                    }
                    break;
                case Code.Ldfld:
                case Code.Ldflda:
                    {
                        // 查找对象中其引用当前位于计算堆栈的字段的值        
                        var fieldDefinition = i.Operand as FieldReference;
                        var fieldName = fieldDefinition.Name;
                        bool needConvToBool = fieldDefinition.FieldType.FullName == typeof(bool).FullName;

                        MakeGetTablePropInstructions(proto, fieldName, i, result, commentPrefix, needConvToBool);
                    }
                    break;
                case Code.Ldarg:
                case Code.Ldarg_S:
                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarga:
                case Code.Ldarga_S:
                    {
                        // 将参数（由指定索引值引用）加载到eval stack        
                        int argLoc;
                        ParameterDefinition argInfo = null;
                        var opCode = i.OpCode.Code;
                        if (opCode == Code.Ldarg || opCode == Code.Ldarg_S
                         || opCode == Code.Ldarga || opCode == Code.Ldarga_S)
                        {
                            argInfo = i.Operand as ParameterDefinition;
                            argLoc = argInfo.Index;
                        }
                        else
                        {
                            argLoc = i.OpCode.Value - 3;
                        }
                        if (proto.method.HasThis)
                        {
                            argLoc++;
                        }
                        //result.Add(proto.MakeEmptyInstruction("")); ;
                        var slotIndex = argLoc;
                        // 复制数据到eval stack
                        PushIntoEvalStackTopSlot(proto, slotIndex, i, result, commentPrefix + " ldarg " + argLoc + " " + ILParamterNameFromDefinition(argInfo));
                    }
                    break;
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                case Code.Ldloca:
                case Code.Ldloca_S:
                    {
                        int loc;
                        VariableDefinition varInfo = null;
                        var opCode = i.OpCode.Code;
                        // FIXME: ldloca, ldloca_S的意义是加载局部变量的地址，和其他几个不一样
                        if (opCode == Code.Ldloc_S || opCode == Code.Ldloc
              || opCode == Code.Ldloca || opCode == Code.Ldloca_S)
                        {
                            varInfo = i.Operand as VariableDefinition;
                            loc = varInfo.Index;
                        }
                        else
                        {
                            loc = i.OpCode.Value - 6;
                            if (loc > proto.maxCallStackSize)
                            {
                                proto.maxCallStackSize = loc;
                            }
                        }
                        // 从当前函数栈的call stack(slots区域)把某个数据复制到eval stack
                        var slotIndex = proto.callStackStartIndex + loc;
                        // 复制数据到eval stack
                        PushIntoEvalStackTopSlot(proto, slotIndex, i, result, commentPrefix + " ldloc " + loc + " " + ILVariableNameFromDefinition(varInfo));
                    }
                    break;
                case Code.Ldc_I4:
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                case Code.Ldc_I4_S:
                case Code.Ldc_I4_M1:

                case Code.Ldc_R4:
                case Code.Ldc_R8:

                case Code.Ldc_I8:
                    {
                        // 加载int/float常量到eval stack
                        var opCode = i.OpCode.Code;
                        var value = (opCode == Code.Ldc_I4_S || opCode == Code.Ldc_I4
                          || opCode == Code.Ldc_I4_M1 || opCode == Code.Ldc_R4
                          || opCode == Code.Ldc_R8 || opCode == Code.Ldc_I8) ? i.Operand : (i.OpCode.Value - 22);
                        MakeLoadConstInst(proto, i, result, proto.tmp1StackTopSlotIndex, value, commentPrefix);
                        PushIntoEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix + " ldc " + value);
                    }
                    break;
                case Code.Ldnull:
                    {
                        // 加载null到eval stack
                        // 加载nil
                        MakeLoadNilInst(proto, i, result, proto.tmp1StackTopSlotIndex, commentPrefix);
                        PushIntoEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix + " lnull" );
                    }
                    break;
                case Code.Ldstr:
                    {
                        // 加载字符串常量到eval stack
                        var constValue = i.Operand.ToString();
                        var literalValueInUvms = TranslatorUtils.Escape(constValue);
                        var valueIdx = proto.InternConstantValue(literalValueInUvms);

                        PushStrIntoEvalStackTopSlot(proto, literalValueInUvms, i, result, commentPrefix + " ldstr " );

                    }
                    break;
                case Code.Add:
                case Code.Add_Ovf:
                case Code.Add_Ovf_Un:
                case Code.Sub:
                case Code.Sub_Ovf:
                case Code.Sub_Ovf_Un:
                case Code.Mul:
                case Code.Mul_Ovf:
                case Code.Mul_Ovf_Un:
                case Code.Div:
                case Code.Div_Un:
                case Code.Rem:
                case Code.Rem_Un:
                case Code.And:
                case Code.Or:
                case Code.Xor:
                case Code.Shl:
                case Code.Shr:
                case Code.Shr_Un:
                    {
                        // 消耗eval stack的顶部2个值(top-1和top), 计算结果存入eval stack
                        // 获取eval stack顶部的值

                        string uvmOpName;
                        var dotNetOpCode = i.OpCode.Code;
                        if (dotNetOpCode == Code.Add
                          || dotNetOpCode == Code.Add_Ovf
                          || dotNetOpCode == Code.Add_Ovf
                          || dotNetOpCode == Code.Add_Ovf_Un)
                        {
                            uvmOpName = "add";
                        }
                        else if (dotNetOpCode == Code.Sub
                        || dotNetOpCode == Code.Sub_Ovf
                        || dotNetOpCode == Code.Sub_Ovf_Un)
                        {
                            uvmOpName = "sub";
                        }
                        else if (dotNetOpCode == Code.Mul
                          || dotNetOpCode == Code.Mul_Ovf
                          || dotNetOpCode == Code.Mul_Ovf_Un)
                        {
                            uvmOpName = "mul";
                        }
                        else if (dotNetOpCode == Code.Div
                          || dotNetOpCode == Code.Div_Un)
                        {
                            uvmOpName = "idiv";
                        }
                        else if (dotNetOpCode == Code.Rem
                          || dotNetOpCode == Code.Rem_Un)
                        {
                            uvmOpName = "mod";
                        }
                        else if (dotNetOpCode == Code.And)
                        {
                            uvmOpName = "band";
                        }
                        else if (dotNetOpCode == Code.Or)
                        {
                            uvmOpName = "bor";
                        }
                        else if (dotNetOpCode == Code.Xor)
                        {
                            uvmOpName = "bxor";
                        }
                        else if (dotNetOpCode == Code.Shl)
                        {
                            uvmOpName = "shl";
                        }
                        else if (dotNetOpCode == Code.Shr
                          || dotNetOpCode == Code.Shr_Un)
                        {
                            uvmOpName = "shr";
                        }
                        else
                        {
                            throw new Exception("not supported op code " + dotNetOpCode);
                        }
                        MakeArithmeticInstructions(proto, uvmOpName, i, result, commentPrefix, false);
                    }
                    break;
                case Code.Neg:
                case Code.Not:
                    {
                        var dotNetOpCode = i.OpCode.Code;
                        string uvmOpName;
                        var needConvertToBool = false;
                        if (dotNetOpCode == Code.Neg)
                        {
                            uvmOpName = "unm";
                        }
                        else if (dotNetOpCode == Code.Not)
                        {
                            uvmOpName = "not";
                            needConvertToBool = true;
                        }
                        else
                        {
                            throw new Exception("not supported op code " + dotNetOpCode);
                        }
                        MakeSingleArithmeticInstructions(proto, uvmOpName, i, result, commentPrefix, needConvertToBool);
                    }
                    break;
                case Code.Box:
                case Code.Unbox:
                case Code.Unbox_Any:
                    {
                        // Box: 把eval stack栈顶的基本类型数值比如int类型值弹出，装箱成对象类型，重新把引用压栈到eval stack顶部
                        // Unbox: 拆箱
                        // 转成uvm字节码指令实际什么都不做
                        proto.AddNotMappedILInstruction(i);
                    }
                    break;
                case Code.Call:
                case Code.Callvirt:
                    {
                        //result.Add(proto.MakeEmptyInstruction(i.ToString()));
                        var operand = (MethodReference)i.Operand;
                        var calledMethod = operand;
                        var methodName = calledMethod.Name;
                        var calledType = operand.DeclaringType;
                        var calledTypeName = calledType.FullName;
                        var methodParams = operand.Parameters;
                        var paramsCount = methodParams.Count;
                        var hasThis = operand.HasThis;
                        var hasReturn = operand.ReturnType.FullName != "System.Void";
                        var needPopFirstArg = false; // 一些函数，比如import module的函数，因为用object成员函数模拟，而在uvm中是table中属性的函数，所以.net中多传了个this对象
                        var returnCount = hasReturn ? 1 : 0;
                        var isUserDefineFunc = false; // 如果是本类中要生成uvm字节码的方法，这里标记为true
                        var isUserDefinedInTableFunc = false; // 是否是模拟table的类型中的成员方法，这样的函数需要gettable取出函数再调用
                        var targetModuleName = ""; // 转成uvm后对应的模块名称，比如可能转成print，也可能转成table.concat等
                        var targetFuncName = ""; // 全局方法名或模块中的方法名，或者本模块中的名称
                        var useOpcode = false;
                        if (calledTypeName == "System.Console")
                        {
                            if (methodName == "WriteLine")
                            {
                                targetFuncName = "print";
                            }
                        }
                        else if (methodName == "ToString")
                        {
                            targetFuncName = "tostring";
                            returnCount = 1;
                        }
                        else if (calledTypeName == typeof(System.String).FullName)
                        {
                            if (methodName == "Concat")
                            {
                                // 连接字符串可以直接使用op_concat指令
                                targetFuncName = "concat";
                                useOpcode = true;
                                hasThis = false;
                                if (paramsCount == 1)
                                {
                                    targetFuncName = "tostring"; // 只有一个参数的情况下，当成tostring处理
                                    useOpcode = false;
                                }
                            }
                            else if (methodName == "op_Equality")
                            {
                                MakeCompareInstructions(proto, "eq", i, result, commentPrefix);
                                return result;
                            }
                            else if (methodName == "op_Inequality")
                            {
                                MakeCompareInstructions(proto, "ne", i, result, commentPrefix);
                                return result;
                            }
                            else if (methodName == "get_Length")
                            {
                                targetFuncName = "len";
                                useOpcode = true;
                                hasThis = true;
                            }
                            else
                            {
                                throw new Exception("not supported method " + calledTypeName + "::" + methodName);
                            }
                            // TODO: 其他字符串特殊函数
                        }
                        else if (calledTypeName == typeof(UvmCoreLib.UvmStringModule).FullName)
                        {
                            // 调用string模块的方法  
                            targetFuncName = UvmCoreLib.UvmStringModule.libContent[methodName];
                            isUserDefineFunc = true;
                            isUserDefinedInTableFunc = true;
                            needPopFirstArg = true;
                            hasThis = false;
                        }
                        else if (calledTypeName == typeof(UvmCoreLib.UvmTableModule).FullName)
                        {
                            // 调用table模块的方法  
                            targetFuncName = UvmCoreLib.UvmTableModule.libContent[methodName];
                            isUserDefineFunc = true;
                            isUserDefinedInTableFunc = true;
                            needPopFirstArg = true;
                            hasThis = false;
                        }
                        else if (calledTypeName == typeof(UvmCoreLib.UvmMathModule).FullName)
                        {
                            // 调用math模块的方法  
                            targetFuncName = UvmCoreLib.UvmMathModule.libContent[methodName];
                            isUserDefineFunc = true;
                            isUserDefinedInTableFunc = true;
                            needPopFirstArg = true;
                            hasThis = false;
                        }
                        else if (calledTypeName == typeof(UvmCoreLib.UvmTimeModule).FullName)
                        {
                            // 调用time模块的方法  
                            targetFuncName = UvmCoreLib.UvmTimeModule.libContent[methodName];
                            isUserDefineFunc = true;
                            isUserDefinedInTableFunc = true;
                            needPopFirstArg = true;
                            hasThis = false;
                        }
                        else if (calledTypeName == typeof(UvmCoreLib.UvmJsonModule).FullName)
                        {
                            // 调用json模块的方法  
                            targetFuncName = UvmCoreLib.UvmJsonModule.libContent[methodName];
                            isUserDefineFunc = true;
                            isUserDefinedInTableFunc = true;
                            needPopFirstArg = true;
                            hasThis = false;
                        }
                        else if (calledTypeName == proto.method.DeclaringType.FullName)
                        {
                            // 调用本类型的方法
                            isUserDefineFunc = true;
                            targetFuncName = methodName;
                            isUserDefinedInTableFunc = false;
                        }
                        else if (calledTypeName == typeof(UvmCoreLib.UvmCoreFuncs).FullName)
                        {
                            if (methodName == "and")
                            {
                                targetFuncName = "band";
                                useOpcode = true;
                                hasThis = false;
                                MakeArithmeticInstructions(proto, targetFuncName, i, result, commentPrefix, true);
                                break;
                            }
                            else if (methodName == "or")
                            {
                                targetFuncName = "bor";
                                useOpcode = true;
                                hasThis = false;
                                MakeArithmeticInstructions(proto, targetFuncName, i, result, commentPrefix, true);
                                break;
                            }
                            else if (methodName == "div")
                            {
                                targetFuncName = "div";
                                useOpcode = true;
                                hasThis = false;
                                MakeArithmeticInstructions(proto, targetFuncName, i, result, commentPrefix, false);
                                break;
                            }
                            else if (methodName == "idiv")
                            {
                                targetFuncName = "idiv";
                                useOpcode = true;
                                hasThis = false;
                                MakeArithmeticInstructions(proto, targetFuncName, i, result, commentPrefix, false);
                                break;
                            }
                            else if (methodName == "not")
                            {
                                targetFuncName = "not";
                                useOpcode = true;
                                hasThis = false;
                                MakeSingleArithmeticInstructions(proto, targetFuncName, i, result, commentPrefix, true);
                                break;
                            }
                            else if (methodName == "neg")
                            {
                                targetFuncName = "unm";
                                useOpcode = true;
                                hasThis = false;
                                MakeSingleArithmeticInstructions(proto, targetFuncName, i, result, commentPrefix, false);
                                break;
                            }
                            else if (methodName == "print")
                            {
                                targetFuncName = "print";
                            }
                            else if (methodName == "tostring")
                            {
                                targetFuncName = "tostring";
                            }
                            else if (methodName == "tojsonstring")
                            {
                                targetFuncName = "tojsonstring";
                            }
                            else if (methodName == "pprint")
                            {
                                targetFuncName = "pprint";
                            }
                            else if (methodName == "tointeger")
                            {
                                targetFuncName = "tointeger";
                            }
                            else if (methodName == "tonumber")
                            {
                                targetFuncName = "tonumber";
                            }
                            else if (methodName == "importModule")
                            {
                                targetFuncName = "require";
                            }
                            else if (methodName == "importContract")
                            {
                                targetFuncName = "import_contract";
                            }
                            else if (methodName == "Debug")
                            {
                                result.AddRange(DebugEvalStack(proto));
                                return result;
                            }
                            else if (methodName == "set_mock_contract_balance_amount")
                            {
                                proto.AddNotMappedILInstruction(i);
                                return result;
                            }
                            else if (methodName == "caller")
                            {
                                proto.InternConstantValue("caller");
                                var envIndex = proto.InternUpvalue("ENV");
                                var globalPropName = "caller";
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABUP,
                                  "gettabup %" + proto.tmp1StackTopSlotIndex + " @" + envIndex + " const \"" + globalPropName + "\"" + commentPrefix, i));
                                PushIntoEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix );
                                return result;
                            }
                            else if (methodName == "caller_address")
                            {
                                proto.InternConstantValue("caller_address");
                                var envIndex = proto.InternUpvalue("ENV");
                                var globalPropName = "caller_address";
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABUP,
                                  "gettabup %" + proto.tmp1StackTopSlotIndex + " @" + envIndex + " const \"" + globalPropName + "\"" + commentPrefix, i));
                               
                                PushIntoEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix );

                                return result;
                            }
                            else if (UvmCoreLib.UvmCoreFuncs.GlobalFuncsMapping.ContainsKey(methodName))
                            {
                                targetFuncName = UvmCoreLib.UvmCoreFuncs.GlobalFuncsMapping[methodName];
                            }
                            else
                            {
                                targetFuncName = methodName;
                            }
                        }
                        else if (calledTypeName.StartsWith("UvmCoreLib.UvmArray")
                          || calledTypeName.StartsWith("UvmCoreLib.UvmMap"))
                        {
                            bool isArrayType = calledTypeName.StartsWith("UvmCoreLib.UvmArray");
                            if (methodName == "Create")
                            {
                                useOpcode = true;
                                targetFuncName = "newtable";
                            }
                            else if (methodName == "Add")
                            {
                                if (isArrayType)
                                {
                                    useOpcode = true;
                                    targetFuncName = "array.add";
                                }
                                else
                                {
                                    useOpcode = true;
                                    targetFuncName = "map.add";
                                }
                            }
                            else if (methodName == "Count")
                            {
                                useOpcode = true;
                                targetFuncName = "len";
                            }
                            else if (methodName == "Set")
                            {
                                if (isArrayType)
                                {
                                    useOpcode = true;
                                    targetFuncName = "array.set";
                                }
                                else
                                {
                                    useOpcode = true;
                                    targetFuncName = "map.set";
                                }
                            }
                            else if (methodName == "Get")
                            {
                                if (isArrayType)
                                {
                                    useOpcode = true;
                                    targetFuncName = "array.get";
                                }
                                else
                                {
                                    useOpcode = true;
                                    targetFuncName = "map.get";
                                }
                            }
                            else if (methodName == "Pop")
                            {
                                useOpcode = true;
                                targetFuncName = "array.pop";
                            }
                            else if (methodName == "Pairs" && !isArrayType)
                            {
                                useOpcode = true;
                                targetFuncName = "map.pairs";
                            }
                            else if (methodName == "Ipairs" && isArrayType)
                            {
                                useOpcode = true;
                                targetFuncName = "array.ipairs";
                            }
                            else if (calledTypeName.Contains("/MapIterator") && methodName == "Invoke")
                            {
                                useOpcode = true;
                                targetFuncName = "iterator_call";
                            }
                            else if (calledTypeName.Contains("/ArrayIterator") && methodName == "Invoke")
                            {
                                useOpcode = true;
                                targetFuncName = "iterator_call";
                            }
                            else
                            {
                                throw new Exception("Not supported func " + calledTypeName + "::" + methodName);
                            }
                        }
                        else if ((calledType is TypeDefinition) && (TranslatorUtils.IsEventEmitterType(calledType as TypeDefinition)))
                        {
                            var eventName = TranslatorUtils.GetEventNameFromEmitterMethodName(calledMethod);
                            if (eventName != null)
                            {
                                // 把eventName压栈,调用全局emit函数
                                targetFuncName = "emit";
                                paramsCount++;
                                // 弹出eventArg，压入eventName，然后压回eventArg
                                PopFromEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix);
                                MakeLoadConstInst(proto, i, result, proto.tmp1StackTopSlotIndex, eventName, commentPrefix);                              
                                PushIntoEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix );
                                PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix );
                            }
                            else
                            {
                                throw new Exception("不支持调用方法" + calledType + "::" + methodName);
                            }
                        }
                        var preAddParamsCount = 0; // 前置额外增加的参数，比如this
                        if (hasThis)
                        {
                            paramsCount++;
                            preAddParamsCount = 1;
                        }

                        // 如果methodName是set_XXXX或者get_XXXX，则是C#的属性操作，转换成uvm的table属性读写操作
                        if (hasThis && methodName.StartsWith("set_") && methodName.Length > 4 && methodParams.Count == 1
                          && (targetFuncName == null || targetFuncName == ""))
                        {
                            // set_XXXX，属性写操作
                            var needConvtToBool = methodParams[0].ParameterType.FullName == "System.Boolean";

                            var propName = methodName.Substring(4);
                            MakeSetTablePropInstructions(proto, propName, i, result, commentPrefix, needConvtToBool);
                            break;
                        }
                        else if (hasThis && methodName.StartsWith("get_") && methodName.Length > 4
                          && methodParams.Count == 0 && returnCount == 1
                          && (targetFuncName == null || targetFuncName == ""))
                        {
                            // get_XXXX, table属性读操作
                            var propName = methodName.Substring(4);
                            var needConvtToBool = returnCount == 1 && operand.ReturnType.FullName == "System.Boolean";
                            MakeGetTablePropInstructions(proto, propName, i, result, commentPrefix, needConvtToBool);
                            break;
                        }
                        else if (calledTypeName != proto.method.DeclaringType.FullName && (targetFuncName == null || targetFuncName.Length < 1))
                        {
                            // 调用其他类的方法
                            isUserDefineFunc = true;
                            targetFuncName = methodName;
                            isUserDefinedInTableFunc = true;
                        }

                        // TODO: 更多内置库的函数支持
                        if (targetFuncName.Length < 1)
                        {
                            throw new Exception("暂时不支持使用方法" + operand.FullName);
                        }
                        if (paramsCount > proto.tmpMaxStackTopSlotIndex - proto.tmp1StackTopSlotIndex - 1)
                        {
                            throw new Exception("暂时不支持超过" + (proto.tmpMaxStackTopSlotIndex - proto.tmp1StackTopSlotIndex - 1) + "个参数的C#方法调用");
                        }

                        // TODO: 把抽取若干个参数的方法剥离成单独函数  

                        // 消耗eval stack顶部若干个值用来调用相应的本类成员函数或者静态函数, 返回值存入eval stack
                        // 不断取出eval-stack中数据(paramsCount)个，倒序翻入tmp stot，然后调用函数
                        var argStartSlot = proto.tmp3StackTopSlotIndex;
                        for (var c = 0; c < paramsCount; c++)
                        {
                            // 倒序遍历插入参数,不算this, c是第 index=paramsCount-c-1-preAddParamsCount个参数
                            // 栈顶取出的是最后一个参数
                            // tmp2 slot用来存放eval stack或者其他值，参数从tmp3开始存放
                            var slotIndex = paramsCount - c + argStartSlot - 1; // 存放参数的slot位置
                            if (slotIndex >= proto.tmpMaxStackTopSlotIndex)
                            {
                                throw new Exception("不支持将超过" + (proto.tmpMaxStackTopSlotIndex - proto.tmp2StackTopSlotIndex) + "个参数的C#函数编译到uvm字节码");
                            }
                            int methodParamIndex = paramsCount - c - 1 - preAddParamsCount; // 此参数是参数在方法的.net参数(不包含this)中的索引
                            var needConvtToBool = false;
                            if (methodParamIndex < methodParams.Count && methodParamIndex >= 0)
                            {
                                var paramType = methodParams[methodParamIndex].ParameterType;
                                if (paramType.FullName == "System.Boolean")
                                {
                                    needConvtToBool = true;
                                }
                            }

                            PopFromEvalStackTopSlot(proto, slotIndex, i, result, commentPrefix);

                            // 对于布尔类型，因为.net中布尔类型参数加载的时候用的ldc.i，加载的是整数，所以这里要进行类型转换成bool类型，使用 not not a来转换
                            if (needConvtToBool)
                            {
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, "not %" + slotIndex + " %" + slotIndex + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NOT, "not %" + slotIndex + " %" + slotIndex + commentPrefix, i));
                            }

                        }

                        var resultSlotIndex = proto.tmp2StackTopSlotIndex;
                        // tmp2StackTopSlotIndex 用来存放函数本身, tmp3是第一个参数
                        if (useOpcode && !isUserDefineFunc)
                        {
                            if (targetFuncName == "concat")
                            {
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CONCAT, "concat %" + resultSlotIndex + " %" + proto.tmp3StackTopSlotIndex + " %" + (proto.tmp3StackTopSlotIndex + 1) + commentPrefix, i));
                            }
                            else if (targetFuncName == "newtable")
                            {
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NEWTABLE, "newtable %" + resultSlotIndex + " 0 0" + commentPrefix, i));
                            }
                            else if (targetFuncName == "array.add")
                            {
                                var tableSlot = argStartSlot;
                                var valueSlot = argStartSlot + 1;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LEN, "len %" + proto.tmp1StackTopSlotIndex + " %" + tableSlot + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD, "add %" + proto.tmp1StackTopSlotIndex + " %" + proto.tmp1StackTopSlotIndex + " const 1" + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE, "settable %" + tableSlot + " %" + proto.tmp1StackTopSlotIndex + " %" + valueSlot + commentPrefix + "array.add", i));
                            }
                            else if (targetFuncName == "array.set")
                            {
                                var tableSlot = argStartSlot;
                                var keySlot = argStartSlot + 1;
                                var valueSlot = argStartSlot + 2;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE, "settable %" + tableSlot + " %" + keySlot + " %" + valueSlot + commentPrefix + " array.set", i));
                            }
                            else if (targetFuncName == "map.set")
                            {
                                var tableSlot = argStartSlot;
                                var keySlot = argStartSlot + 1;
                                var valueSlot = argStartSlot + 2;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE, "settable %" + tableSlot + " %" + keySlot + " %" + valueSlot + commentPrefix + " map.set", i));
                            }
                            else if (targetFuncName == "array.get")
                            {
                                var tableSlot = argStartSlot;
                                var keySlot = argStartSlot + 1;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABLE, "gettable %" + resultSlotIndex + " %" + tableSlot + " %" + keySlot + commentPrefix + " array.set", i));
                            }
                            else if (targetFuncName == "map.get")
                            {
                                var tableSlot = argStartSlot;
                                var keySlot = argStartSlot + 1;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABLE, "gettable %" + resultSlotIndex + " %" + tableSlot + " %" + keySlot + commentPrefix + " map.set", i));
                            }
                            else if (targetFuncName == "len")
                            {
                                var tableSlot = argStartSlot;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LEN, "len %" + resultSlotIndex + " %" + tableSlot + commentPrefix + "array.len", i));
                            }
                            else if (targetFuncName == "array.pop")
                            {
                                var tableSlot = argStartSlot;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LEN, "len %" + proto.tmp1StackTopSlotIndex + " %" + tableSlot + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADNIL,"loadnil %" + resultSlotIndex + " 0" + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE, "settable %" + tableSlot + " %" + proto.tmp1StackTopSlotIndex + " %" + resultSlotIndex + commentPrefix + "array.pop", i));
                            }
                            else if (targetFuncName == "map.pairs")
                            {
                                // 调用pairs函数，返回1个结果，迭代器函数对象
                                var tableSlot = argStartSlot;
                                var envIndex = proto.InternUpvalue("ENV");
                                proto.InternConstantValue("pairs");
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABUP, "gettabup %" + proto.tmp1StackTopSlotIndex + " @" + envIndex + " const \"pairs\"" + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + proto.tmp2StackTopSlotIndex + " %" + tableSlot + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CALL, "call %" + proto.tmp1StackTopSlotIndex + " 2 2" + commentPrefix, i));
                                // pairs函数返回1个函数，返回在刚刚函数调用时函数所处slot tmp1
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + resultSlotIndex + " %" + proto.tmp1StackTopSlotIndex + commentPrefix + " map.pairs", i));
                            }
                            else if (targetFuncName == "array.ipairs")
                            {
                                // 调用ipairs函数，返回1个结果，迭代器函数对象
                                var tableSlot = argStartSlot;
                                var envIndex = proto.InternUpvalue("ENV");
                                proto.InternConstantValue("ipairs");
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABUP, "gettabup %" + proto.tmp1StackTopSlotIndex + " @" + envIndex + " const \"ipairs\"" + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + proto.tmp2StackTopSlotIndex + " %" + tableSlot + commentPrefix, i));
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CALL, "call %" + proto.tmp1StackTopSlotIndex + " 2 2" + commentPrefix, i));
                                // ipairs函数返回1个函数，返回在刚刚函数调用时函数所处slot tmp1
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + resultSlotIndex + " %" + proto.tmp1StackTopSlotIndex + commentPrefix + " array.ipairs", i));
                            }
                            else if (targetFuncName == "iterator_call")
                            {
                                // 调用迭代器函数，接受两个参数(map和上一个key),返回2个结果写入一个新的table中, {"Key": 第一个返回值, "Value": 第二个返回值}
                                var iteratorSlot = argStartSlot;
                                var tableSlot = argStartSlot + 1;
                                var keySlot = argStartSlot + 2;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CALL, "call %" + iteratorSlot + " 3 3" + commentPrefix, i));
                                // 迭代器函数返回2个结果，返回在刚刚函数调用时函数所处slot，用来构造{"Key": 返回值1, "Value": 返回值2}
                                var resultKeySlot = iteratorSlot;
                                var resultValueSlot = iteratorSlot + 1;
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NEWTABLE,
                                  "newtable %" + proto.tmp1StackTopSlotIndex + " 0 0" + commentPrefix, i));
                                MakeLoadConstInst(proto, i, result, proto.tmp2StackTopSlotIndex, "Key", commentPrefix);
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE,
                                  "settable %" + proto.tmp1StackTopSlotIndex + " %" + proto.tmp2StackTopSlotIndex + " %" + resultKeySlot + commentPrefix, i));
                                MakeLoadConstInst(proto, i, result, proto.tmp2StackTopSlotIndex, "Value", commentPrefix);
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE,
                                  "settable %" + proto.tmp1StackTopSlotIndex + " %" + proto.tmp2StackTopSlotIndex + " %" + resultValueSlot + commentPrefix, i));
                                // 把产生的table作为结果
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE,
                                  "move %" + resultSlotIndex + " %" + proto.tmp1StackTopSlotIndex + commentPrefix + " map.iterator_call", i));
                            }
                            else
                            {
                                throw new Exception("not supported opcode " + targetFuncName);
                            }
                        }
                        else if (isUserDefineFunc)
                        {
                            //if (!isUserDefinedInTableFunc)
                            //{
                            //    //访问本类成员方法
                            //    var protoName = TranslatorUtils.MakeProtoName(calledMethod);
                            //    var funcUpvalIndex = proto.InternUpvalue(protoName); 
                            //    proto.InternConstantValue(protoName);

                            //    result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETUPVAL,
                            //            "getupval %" + proto.tmp2StackTopSlotIndex + " @" + funcUpvalIndex + commentPrefix, i));
                            //}
                            //else
                            //{ 
                            // 访问其他类的成员方法，需要gettable取出函数
                            var protoName = TranslatorUtils.MakeProtoName(calledMethod);
                            //var funcUpvalIndex = proto.InternUpvalue(protoName);  //check zq  delete ?? 删除，无需从upvalue里取， 从table中取，因为class创建closure时候未追加伪指令操作upvalue
                            proto.InternConstantValue(protoName);
                            if (targetFuncName == null || targetFuncName.Length < 1)
                            {
                                targetFuncName = calledMethod.Name;
                            }
                            MakeLoadConstInst(proto, i, result, proto.tmp2StackTopSlotIndex, targetFuncName, commentPrefix);
                            if (needPopFirstArg)
                            {
                                // object模拟uvm module，module信息在calledMethod的this参数中
                                // 这时候eval stack应该是[this], argStart开始的数据应该是this, ...
                                // result.AddRange(DebugEvalStack(proto));
                                PopFromEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix);
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABLE,
                                    "gettable %" + proto.tmp2StackTopSlotIndex + " %" + proto.tmp1StackTopSlotIndex + " %" + proto.tmp2StackTopSlotIndex + commentPrefix, i));
                            }
                            else
                            {
                                result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABLE,
                                    "gettable %" + proto.tmp2StackTopSlotIndex + " %" + argStartSlot + " %" + proto.tmp2StackTopSlotIndex + commentPrefix, i));
                            }
                            //}

                        }
                        else if (targetModuleName.Length < 1)
                        {
                            // 全局函数或局部函数
                            // TODO: 这里要从上下文查找是否是局部变量，然后分支处理，暂时都当全局函数处理
                            var envUp = proto.InternUpvalue("ENV");
                            proto.InternConstantValue(targetFuncName);
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABUP, "gettabup %" + proto.tmp2StackTopSlotIndex + " @" + envUp + " const \"" + targetFuncName + "\"" + commentPrefix, i));
                        }
                        else
                        {
                            throw new Exception("not supported yet");
                        }
                        if (!useOpcode)
                        {
                            // 调用tmp2位置的函数，函数调用返回结果会存回tmp2开始的slots
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CALL,
                                "call %" + proto.tmp2StackTopSlotIndex + " " + (paramsCount + 1) + " " + (returnCount + 1) +
                                commentPrefix, i));
                            //check zq ???
                            //result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + proto.tmp3StackTopSlotIndex + " %" + proto.tmp2StackTopSlotIndex + commentPrefix, i));


                        }
                        //else if (hasReturn)
                        //{
                        //    result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + proto.tmp3StackTopSlotIndex + " %" + proto.tmp2StackTopSlotIndex + commentPrefix, i));
                        //}
                        // 把调用结果存回eval-stack
                        if (hasReturn)
                        {
                            // 调用结果在tmp2
                            PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix );
                        }
                    }
                    break;
                case Code.Br:
                case Code.Br_S:
                    {
                        var toJmpToInst = i.Operand as Instruction;

                        MakeJmpToInstruction(proto, i, "br", toJmpToInst, result, commentPrefix, onlyNeedResultCount);
                    }
                    break;
                case Code.Beq:
                case Code.Beq_S:
                case Code.Bgt:
                case Code.Bgt_S:
                case Code.Bgt_Un:
                case Code.Bgt_Un_S:
                case Code.Blt:
                case Code.Blt_S:
                case Code.Blt_Un:
                case Code.Blt_Un_S:
                case Code.Bge:
                case Code.Bge_S:
                case Code.Bge_Un:
                case Code.Bge_Un_S:
                case Code.Ble:
                case Code.Ble_S:
                case Code.Ble_Un:
                case Code.Ble_Un_S:
                case Code.Bne_Un:
                case Code.Bne_Un_S:
                    {
                        // 比较两个值(top-1和top)，满足一定条件就jmp到目标指令
                        // beq: 如果两个值相等，则将控制转移到目标指令
                        // bgt: 如果第一个值大于第二个值，则将控制转移到目标指令
                        // bge: 如果第一个值大于或等于第二个值，则将控制转移到目标指令
                        // blt: 如果第一个值小于第二个值，则将控制转移到目标指令
                        // ble: 如果第一个值小于或等于第二个值，则将控制转移到目标指令
                        // bne: 当两个无符号整数值或不可排序的浮点型值不相等时，将控制转移到目标指令
                        var toJmpToInst = i.Operand as Instruction;
                        Console.WriteLine(i);
                        var toJmpToOffset = toJmpToInst.Offset;
                        var opCode = i.OpCode.Code;
                        var opName = opCode.ToString().Replace(" ", "_");
                        string compareType;
                        if (opCode == Code.Beq || opCode == Code.Beq_S)
                        {
                            compareType = "eq";
                        }
                        else if (opCode == Code.Bgt || opCode == Code.Bgt_S || opCode == Code.Bgt_Un ||
                                 opCode == Code.Bgt_Un_S)
                        {
                            compareType = "gt";
                        }
                        else if (opCode == Code.Bge || opCode == Code.Bge_S || opCode == Code.Bge_Un ||
                                 opCode == Code.Bge_Un_S)
                        {
                            compareType = "ge";
                        }
                        else if (opCode == Code.Blt || opCode == Code.Blt_S || opCode == Code.Blt_Un ||
                                 opCode == Code.Blt_Un_S)
                        {
                            compareType = "lt";
                        }
                        else if (opCode == Code.Ble || opCode == Code.Ble_S || opCode == Code.Ble_Un ||
                                 opCode == Code.Ble_Un_S)
                        {
                            compareType = "le";
                        }
                        else if (opCode == Code.Bne_Un || opCode == Code.Bne_Un_S)
                        {
                            compareType = "ne";
                        }
                        else
                        {
                            throw new Exception("Not supported opcode " + opCode);
                        }
                        // 从eval stack弹出两个值(top和top-1)，比较大小，比较结果存入eval stack
                        //result.Add(proto.MakeEmptyInstruction(i.ToString()));

                        // 消耗eval stack的顶部2个值, 然后比较，比较结果存入eval stack
                        // 获取eval stack顶部的值
                        proto.InternConstantValue(1);
                        var arg1SlotIndex = proto.tmp3StackTopSlotIndex + 1; // top-1
                        var arg2SlotIndex = proto.tmp3StackTopSlotIndex + 2; // top

                        PopFromEvalStackTopSlot(proto, arg2SlotIndex, i, result, commentPrefix);

                        // 再次获取eval stack栈顶的值
                        PopFromEvalStackTopSlot(proto, arg1SlotIndex, i, result, commentPrefix);

                        // 比较
                        if (compareType == "eq")
                        {
                            // eq: if ((RK(B) == RK(C)) ~= A) then pc++
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                            "eq " + 0 + " %" + arg1SlotIndex + " %" + arg2SlotIndex +
                            commentPrefix, i));
                        }
                        else if (compareType == "ne")
                        {
                            // eq: if ((RK(B) == RK(C)) ~= A) then pc++
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                            "eq " + 1 + " %" + arg1SlotIndex + " %" + arg2SlotIndex +
                            commentPrefix, i));
                        }
                        else if (compareType == "gt")
                        {
                            // lt: if ((RK(B) <  RK(C)) ~= A) then pc++
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                                "lt " + 1 + " %" + arg1SlotIndex + " %" + arg2SlotIndex +
                                commentPrefix, i));
                        }
                        else if (compareType == "lt")
                        {
                            // lt: if ((RK(B) <  RK(C)) ~= A) then pc++
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                                "lt " + 0 + " %" + arg1SlotIndex + " %" + arg2SlotIndex +
                                commentPrefix, i));
                        }
                        else if (compareType == "ge")
                        {
                            // lt: if ((RK(B) <  RK(C)) ~= A) then pc++
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                                "le " + 1 + " %" + arg1SlotIndex + " %" + arg2SlotIndex +
                                commentPrefix, i));
                        }
                        else if (compareType == "le")
                        {
                            // lt: if ((RK(B) <  RK(C)) ~= A) then pc++
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                                "le " + 0 + " %" + arg1SlotIndex + " %" + arg2SlotIndex +
                                commentPrefix, i));
                        }
                        else
                        {
                            throw new Exception("not supported compare type " + i);
                        }

                        // 不满足条件就执行下条tmp指令，否则执行下下条指令
                        var jmpLabel1 = proto.Name + "_1_" + opName + "_" + i.Offset;
                        var offsetOfInst1 = 2; // 如果不满足条件，跳转到本指令后的指令
                        jmpLabel1 =
                            proto.InternNeedLocationLabel(
                                offsetOfInst1 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel1);
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel1 + commentPrefix,
                            i));

                        // 满足条件，跳转到目标指令
                        MakeJmpToInstruction(proto, i, opName, toJmpToInst, result, commentPrefix, onlyNeedResultCount);
                    }
                    break;
                case Code.Brtrue:
                case Code.Brtrue_S:
                case Code.Brfalse:
                case Code.Brfalse_S:
                    {
                        // Branch to target if value is 1(true) or zero (false)
                        var toJmpToInst = i.Operand as Instruction;
                        Console.WriteLine(i);
                        var toJmpToOffset = toJmpToInst.Offset;
                        var opCode = i.OpCode.Code;
                        var opName = (opCode == Code.Brtrue || opCode == Code.Brtrue_S) ? "brtrue" : "brfalse";

                        var eqCmpValue = (opCode == Code.Brtrue || opCode == Code.Brtrue_S) ? 0 : 1;

                        // result.AddRange(DebugEvalStack(proto));

                        // 先判断eval stack top 是否是 1 or zero(根据是brtrue/brfalse决定cmpValue)
                        PopFromEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix);

                        MakeLoadConstInst(proto, i, result, proto.tmp3StackTopSlotIndex, 0, commentPrefix);
                        // eq: if ((tmp2 == 0) ~= A) then pc++
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_ADD,
                            "eq " + eqCmpValue + " %" + proto.tmp2StackTopSlotIndex + " %" + proto.tmp3StackTopSlotIndex +
                            commentPrefix, i));

                        // 为eqCmpValue就执行下条tmp指令，否则执行下下条指令
                        var jmpLabel1 = proto.Name + "_1_" + opName + "_" + i.Offset;
                        var offsetOfInst1 = 2; // 如果为eqCmpValue，跳转到目标指令
                        jmpLabel1 =
                            proto.InternNeedLocationLabel(
                                offsetOfInst1 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel1);
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel1 + commentPrefix,
                            i));

                        var jmpLabel2 = proto.Name + "_2_" + opName + "_" + i.Offset;
                        var offsetOfInst2 = 2; // 如果不为eqCmpValue，则跳转到本brtrue/brfalse指令后的指令
                        jmpLabel2 = proto.InternNeedLocationLabel(offsetOfInst2 + proto.NotEmptyCodeInstructions().Count + NotEmptyUvmInstructionsCountInList(result), jmpLabel2);
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_JMP, "jmp 1 $" + jmpLabel2 + commentPrefix,
                            i));

                        // 跳转到目标指令
                        MakeJmpToInstruction(proto, i, opName, toJmpToInst, result, commentPrefix, onlyNeedResultCount);
                    }
                    break;
                case Code.Ret:
                    {
                        // 结束当前函数栈并返回eval stack中的数据
                        // 根据c#代码的返回类型是否void来判断返回数据
                        var returnCount = proto.method.MethodReturnType.ReturnType.FullName == typeof(void).FullName ? 0 : 1;
                        if (returnCount > 0)
                        {
                            //此时应把所有evalstack pop出来 
                            PopFromEvalStackTopSlot(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix);
                            //MakeGetTopOfEvalStackInst(proto, i, result, proto.tmp1StackTopSlotIndex, commentPrefix);
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %" + proto.tmp1StackTopSlotIndex + " " + (returnCount + 1) + commentPrefix, i));
                        }
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %0 1" + commentPrefix + " ret", i));
                    }
                    break;
                case Code.Newarr:
                    {
                        // 因为.net数组是0-based，uvm的数组是1-based，所以不支持.net数组
                        throw new Exception("Not support .net array now");
                        // FIXME
                        // 创建一个空数组放入eval-stack顶
                        // 获取eval stack顶部的值                   
                        result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NEWTABLE, "newtable %" + proto.tmp2StackTopSlotIndex + " 0 0" + commentPrefix, i));
                       
                        PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " newarr ");
                    }
                    break;
                case Code.Initobj:
                    {
                        throw new Exception("not supported " + i);
                        // 将位于指定地址的值类型的每个字段初始化为空引用或适当的基元类型的 0  
                        // TODO: 根据是什么类型进行init，如果是Nullable类型进行initobj，则弹出数据，插入nil
                        // TODO: 改成 if判断是否nil，如果是nil，保持，如果不是，设置为nil
                        /*modify by zq */ // LoadNilInstruction(proto, proto.tmp1StackTopSlotIndex, i, result, commentPrefix);
                        //PopFromEvalStackToSlot(proto, proto.tmp2StackTopSlotIndex, proto.nilSlotIndex, i, result, commentPrefix);
                        //AddEvalStackSizeInstructions(proto, i, result, commentPrefix);
                        //result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_SETTABLE, "settable %" + proto.evalStackIndex + " %" + proto.evalStackSizeIndex + " %" + proto.nilSlotIndex, i));
                        proto.AddNotMappedILInstruction(i);
                        // return result;
                    }
                    break;
                case Code.Newobj:
                    {
                        // 如果是Nullable类型构建，什么都不做
                        var operand = i.Operand as MethodReference;
                        if (operand.DeclaringType.FullName.StartsWith("System.Nullable"))
                        {
                            // 什么都不做
                            break;
                        }


                        // 如果是contract类型,调用构造函数，而不是设置各成员函数              
                        if (operand.DeclaringType is TypeDefinition
                          && TranslatorUtils.IsContractType(operand.DeclaringType as TypeDefinition)
                          && operand.DeclaringType == this.ContractType
                          && this.ContractType != null)
                        {
                            var protoName = TranslatorUtils.MakeProtoNameOfTypeConstructor(operand.DeclaringType); // 构造函数的名字
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CLOSURE,
                              "closure %" + proto.tmp2StackTopSlotIndex + " " + protoName, i));
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CALL,
                              "call %" + proto.tmp2StackTopSlotIndex + " 1 2", i));
                            // 返回值(新对象处于tmp2 slot) 
                        }
                        else
                        {
                            // 创建一个空的未初始化对象放入eval-stack顶
                            // 获取eval stack顶部的值                   //有问题 check zq ????使用与于contract.storage = new Storage();  但其他class不适合？
                            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_NEWTABLE, "newtable %" + proto.tmp2StackTopSlotIndex + " 0 0" + commentPrefix, i));
                        }

                        PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " newobj ");

                    }
                    break;
                case Code.Dup:
                    {
                        // 把eval stack栈顶元素复制一份到eval-stack顶
                        // 获取eval stack顶部的值
                        PopFromEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " dup");
                        PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " dup ");
                        PushIntoEvalStackTopSlot(proto, proto.tmp2StackTopSlotIndex, i, result, commentPrefix + " dup ");

                    }
                    break;
                case Code.Pop:
                    {
                        PopFromEvalStackTopSlot(proto, proto.tmpMaxStackTopSlotIndex, i, result, commentPrefix);
                    }
                    break;
                case Code.Stelem_Ref:
                    {
                        throw new Exception("not supported opcode " + i.OpCode + " now");
                    }
                    break;
                case Code.Cgt:
                case Code.Cgt_Un:
                case Code.Clt:
                case Code.Clt_Un:
                case Code.Ceq:
                    {
                        var opCode = i.OpCode.Code;
                        // 比较两个值(eval stack top-1 和 top)。如果第一个值大于第二个值(如果是Clt/Clt_Un，则是比较是否小于)，则将整数值 1 (int32) 推送到计算堆栈上；反之，将 0 (int32) 推送到计算堆栈上
                        //var isComparingUnsignedValues = opCode == Code.Cgt_Un || opCode == Code.Clt_Un; // 是否是比较两个无符号值
                        //if (isComparingUnsignedValues)
                        //{
                        //  throw new Exception("not support compare unsigned values now");
                        //}
                        string compareType;
                        if (opCode == Code.Cgt || opCode == Code.Cgt_Un)
                        {
                            compareType = "gt";
                        }
                        else if (opCode == Code.Clt || opCode == Code.Clt_Un)
                        {
                            compareType = "lt";
                        }
                        else if (opCode == Code.Ceq)
                        {
                            compareType = "eq";
                        }
                        else
                        {
                            throw new Exception("not supported comparator " + i);
                        }

                        if (i.Previous != null && i.Previous.OpCode.Code == Code.Ldnull && compareType != "eq")
                        {
                            // 因为C#中将 a != null转换成了 ldnull; Gt_Un 指令
                            compareType = "ne";
                        }

                        MakeCompareInstructions(proto, compareType, i, result, commentPrefix);

                    }
                    break;
                case Code.Ldsfld:
                case Code.Ldsflda:
                    {
                        // 将静态字段的值推送到计算堆栈上
                        // TODO                
                        throw new Exception("not supported opcode " + i);
                    }
                    break;
                case Code.Isinst:
                    {
                        // 测试对象引用（O 类型）是否为特定类的实例. eval-stack栈顶是要测试对象，如果检查失败，弹出并压栈null
                        var toCheckType = i.Operand as TypeReference;
                        // 这里强行验证通过，因为uvm中运行时没有C#中的具体类型信息. 
                        proto.AddNotMappedILInstruction(i);

                    }
                    break;
                case Code.Conv_I:
                case Code.Conv_I1:
                case Code.Conv_I2:
                case Code.Conv_I4:
                case Code.Conv_I8:
                case Code.Conv_R4:
                case Code.Conv_R8:
                case Code.Conv_R_Un:
                case Code.Conv_U:
                case Code.Conv_U1:
                case Code.Conv_U2:
                case Code.Conv_U4:
                case Code.Conv_U8:
                    {
                        // 将位于计算堆栈顶部的值转换为其他格式
                        proto.AddNotMappedILInstruction(i);
                    }
                    break;
                case Code.Castclass:
                    {
                            string operand = i.Operand.ToString();
                            if (operand.StartsWith("UvmCoreLib.UvmMap") ||
                                operand.StartsWith("UvmCoreLib.UvmArray"))
                            {
                                break;
                            }
                            else
                            {
                                throw new Exception("not supported op code " + Code.Castclass);
                            }
                       
                    }
                default:
                    {
                        throw new Exception("not supported opcode " + i.OpCode + " now");
                    }
            }
            return result;
        }

        private int NotEmptyUvmInstructionsCountInList(IList<UvmInstruction> items)
        {
            var count = 0;
            foreach (var item in items)
            {
                if (!(item is UvmEmptyInstruction))
                {
                    count++;
                }
            }
            return count;
        }

        private UvmProto TranslateILMethod(MethodDefinition method, StringBuilder ilContentBuilder, StringBuilder uvmAsmBuilder, UvmProto parentProto)
        {
            if (method.Name.Equals(".ctor"))
            {
                return null;
            }
            var protoName = TranslatorUtils.MakeProtoName(method);
            var proto = new UvmProto(protoName);
            proto.SizeP = method.Parameters.Count; // 参数数量
            proto.paramsStartIndex = 0;
            if (method.HasThis)
            {
                proto.SizeP++; // this对象作为第一个参数
            }
            proto.IsVararg = false;
            proto.Parent = parentProto;
            proto.method = method;
            // Get a ILProcessor for the Run method
            var il = method.Body.GetILProcessor();

            ilContentBuilder.Append("method " + method.FullName + ", simple name is " + method.Name + "\r\n");
            // 在uvm的proto开头创建一个table局部变量，模拟evaluation stack
            var createEvalStackInst = UvmInstruction.Create(UvmOpCodeEnums.OP_NEWTABLE, null); // 除参数外的第一个局部变量固定用作eval stack
                                                                                               // createEvalStackInst.LineInSource = method.
            proto.evalStackIndex = proto.SizeP; // eval stack所在的局部变量的slot index
            createEvalStackInst.AsmLine = "newtable %" + proto.evalStackIndex + " 0 0";
            proto.AddInstruction(createEvalStackInst);


            proto.evalStackSizeIndex = proto.evalStackIndex + 1; // 固定存储最新eval stack长度的slot
            proto.InternConstantValue(0);
            proto.InternConstantValue(1);
            proto.AddInstruction(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk %" + proto.evalStackSizeIndex + " const 0", null));

            //add by zq 
            //proto.nilSlotIndex = proto.evalStackSizeIndex + 2; // 固定存储nil的slot
            //proto.AddInstruction(proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADNIL, "loadnil %" + proto.nilSlotIndex + " 0", null));

            // 除了eval-stack的额外局部变量slot，额外还要提供2个slot用来存放一个栈顶值，用来做存到eval-stack的中转
            proto.tmp1StackTopSlotIndex = proto.evalStackSizeIndex + 1; // 临时存储，比如存放栈中取出的值或者参数值，返回值等
            proto.tmp2StackTopSlotIndex = proto.tmp1StackTopSlotIndex + 1; // 临时存储，比如存放临时的栈顶值或者参数值等
            proto.tmp3StackTopSlotIndex = proto.tmp2StackTopSlotIndex + 1; // 临时存储，比如存放临时的参数值或者nil等
            proto.tmpMaxStackTopSlotIndex = proto.tmp1StackTopSlotIndex + 17; // 目前最多支持18个临时存储


            proto.callStackStartIndex = proto.tmpMaxStackTopSlotIndex + 10; // 模拟C#的call stack的起始slot索引,+2是为了留位置给tmp区域函数调用的返回值


            proto.Numparams = proto.SizeP;
            proto.maxCallStackSize = 0;

            var lastLinenumber = 0;


            //add by zq
            //GroupILInstructions(proto, method);
            //foreach (var ilgroup in proto.ILInsGroups)
            //{
            //    Instruction i = ilgroup.ILInstructions[0];
            //    ilContentBuilder.Append("" + i.OpCode + " " + i.Operand + "\r\n");
            //    var commentPrefix = ";"; // 一行指令的行信息的注释前缀
            //    var hasLineInfo = i.SequencePoint != null;
            //    if (hasLineInfo)
            //    {
            //        var startLine = i.SequencePoint.StartLine;
            //        var endLine = i.SequencePoint.EndLine;
            //        if (startLine > 1000000)
            //        {
            //            startLine = lastLinenumber;
            //        }
            //        commentPrefix += "L" + startLine + ";";
            //        lastLinenumber = startLine;
            //    }
            //    else
            //    {
            //        commentPrefix += "L" + lastLinenumber + ";";
            //    }
            //    var dotnetOpStr = i.OpCode.ToString();
            //    // commentPrefix += dotnetOpStr;
            //    // 关于.net的evaluation stack在uvm字节码虚拟机中的实现方式
            //    // 维护一个evaluation stack的局部变量,，每个proto入口处清空它
            //    var uvmInstructions = TranslateILInstructionsGroup(proto, ilgroup, commentPrefix, false);
            //    foreach (var uvmInst in uvmInstructions)
            //    {
            //        proto.AddInstruction(uvmInst);
            //    }
            //}


            // 不需要支持类型的虚函数调用，只支持静态函数
            foreach (var i in method.Body.Instructions)
            {
                ilContentBuilder.Append("" + i.OpCode + " " + i.Operand + "\r\n");
                var commentPrefix = ";"; // 一行指令的行信息的注释前缀
                var hasLineInfo = i.SequencePoint != null;
                if (hasLineInfo)
                {
                    var startLine = i.SequencePoint.StartLine;
                    var endLine = i.SequencePoint.EndLine;
                    if (startLine > 1000000)
                    {
                        startLine = lastLinenumber;
                    }
                    commentPrefix += "L" + startLine + ";";
                    lastLinenumber = startLine;
                }
                else
                {
                    commentPrefix += "L" + lastLinenumber + ";";
                }
                var dotnetOpStr = i.OpCode.ToString();
                // commentPrefix += dotnetOpStr;
                // 关于.net的evaluation stack在uvm字节码虚拟机中的实现方式
                // 维护一个evaluation stack的局部变量,，每个proto入口处清空它
                var uvmInstructions = TranslateILInstruction(proto, i, commentPrefix, false);
                foreach (var uvmInst in uvmInstructions)
                {
                    proto.AddInstruction(uvmInst);
                }
            }

            // 处理NeededLocationsMap，忽略empty lines
            var notEmptyInstructionsOfProto = proto.NotEmptyCodeInstructions();
            for (var j = 0; j < notEmptyInstructionsOfProto.Count; j++)
            {
                var inst = notEmptyInstructionsOfProto[j];
                if (proto.NeededLocationsMap.ContainsKey(j))
                {
                    inst.LocationLabel = proto.NeededLocationsMap[j];
                }
            }


            //add by zq 扫描uvm instructions 去除冗余代码
            ReduceProtoUvmInsts(proto);



            // TODO: 可能jmp到指令尾部?

            if (proto.CodeInstructions.Count < 1)
            {
                proto.AddInstructionLine(UvmOpCodeEnums.OP_RETURN, "return %0 1", null);
            }

            proto.MaxStackSize = proto.callStackStartIndex + 1 + proto.maxCallStackSize;

            // 函数代码块结尾添加return 0 1指令来结束代码块
            var endBlockInst = UvmInstruction.Create(UvmOpCodeEnums.OP_RETURN, null);
            endBlockInst.AsmLine = "return %0 1";

            // TODO: proto指令头部要插入常量池，upvalue池，func的属性声明等

            ilContentBuilder.Append("\r\n");
            // TODO
            return proto;
        }

        private IList<UvmInstruction> DebugEvalStack(UvmProto proto)
        {
            var result = new List<UvmInstruction>();
            // for debug,输出eval stack
            result.Add(proto.MakeEmptyInstruction("for debug eval stack"));
            var envSlot = proto.InternUpvalue("ENV");
            proto.InternConstantValue("pprint");
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_GETTABUP, "gettabup %" + (proto.evalStackIndex + 20) + " @" + envSlot + " const \"pprint\"; for debug eval-stack", null));
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move %" + (proto.evalStackIndex + 21) + " %" + proto.evalStackIndex + ";  for debug eval-stack", null));
            result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_CALL, "call %" + (proto.evalStackIndex + 20) + " 2 1;  for debug eval-stack", null));
            result.Add(proto.MakeEmptyInstruction(""));
            return result;
        }

        public static string TranslateDotNetDllToUvm(string dllFilepath)
        {
            var readerParameters = new ReaderParameters { ReadSymbols = true };
            var assemblyDefinition = AssemblyDefinition.ReadAssembly(dllFilepath, readerParameters);
            var sampleModule = assemblyDefinition.MainModule; // ModuleDefinition.ReadModule(sampleAssemblyPath);
            var translator = new ILToUvmTranslator();
            var ilContentBuilder = new StringBuilder();
            var uvmAsmContentBuilder = new StringBuilder();
            var symbolReader = sampleModule.SymbolReader;
            translator.TranslateModule(sampleModule, ilContentBuilder, uvmAsmContentBuilder);
            var ilText = ilContentBuilder.ToString();

            var fileDir = Path.GetDirectoryName(dllFilepath);
            var dllFileName = Path.GetFileNameWithoutExtension(dllFilepath);
            var ilAssFilepath = Path.GetFullPath(Path.Combine(fileDir, dllFileName + ".dotnet_ass.txt"));
            using (var ilAssFile = File.Create(ilAssFilepath))
            {
                var bytes = Encoding.UTF8.GetBytes(ilText);
                ilAssFile.Write(bytes, 0, bytes.Length);
            }

            var assText = uvmAsmContentBuilder.ToString();

            var uvmsOutputFilePath = Path.GetFullPath(Path.Combine(fileDir, dllFileName + ".uvms"));
            using (var outFileStream = File.Create(uvmsOutputFilePath))
            {
                var bytes = Encoding.UTF8.GetBytes(assText);
                outFileStream.Write(bytes, 0, bytes.Length);
            }

            var metaInfoJson = translator.GetMetaInfoJson();
            using (var os = File.Create(Path.GetFullPath(Path.Combine(fileDir, dllFileName + ".meta.json"))))
            {
                var bytes = Encoding.UTF8.GetBytes(metaInfoJson);
                os.Write(bytes, 0, bytes.Length);
            }
            return uvmsOutputFilePath;
        }


        //add by zq 
        //private IList<UvmInstruction> TranslateILInstructionsGroup(UvmProto proto, ILInstructionGroup ILgroup, string commentPrefix, bool onlyNeedResultCount)
        //{
        //    var result = new List<UvmInstruction>();
        //    if (ILgroup == null || ILgroup.ILInstructions.Count < 1)
        //    {
        //        throw new Exception("TranslateILInstructionsGroup程序出错");
        //    }

        //    switch (ILgroup.GroupCode)
        //    {
        //        case ILGroupCode.NoGroup:
        //            {
        //                result = (List<UvmInstruction>)TranslateILInstruction(proto, ILgroup.ILInstructions[0], commentPrefix, onlyNeedResultCount);
        //                break;

        //            }
        //        case ILGroupCode.LdSt:
        //            {
        //                var LdStComment = commentPrefix + " LdSt group translate";
        //                int fromSlot = -1;
        //                int targetSlot = GetTargetSlotIndex(proto, ILgroup.ILInstructions[1]);
        //                Instruction i = ILgroup.ILInstructions[0];
        //                var code = i.OpCode.Code;
        //                var codestr = code.ToString();
        //                if (codestr.StartsWith("Ldarg"))
        //                {
        //                    int argLoc;
        //                    ParameterDefinition argInfo = null;

        //                    if (code == Code.Ldarg || code == Code.Ldarg_S || code == Code.Ldarga || code == Code.Ldarga_S)
        //                    {
        //                        argInfo = i.Operand as ParameterDefinition;
        //                        argLoc = argInfo.Index;
        //                    }
        //                    else
        //                    {
        //                        argLoc = i.OpCode.Value - 3;
        //                    }
        //                    if (proto.method.HasThis)
        //                    {
        //                        argLoc++;
        //                    }
        //                    fromSlot = argLoc;
        //                    result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE,
        //                      "move %" + targetSlot + " %" + fromSlot + LdStComment, i));

        //                }
        //                else if (codestr.StartsWith("Ldc_"))
        //                {
        //                    // 加载int/float常量到eval stack

        //                    var value = (code == Code.Ldc_I4_S || code == Code.Ldc_I4
        //                      || code == Code.Ldc_I4_M1 || code == Code.Ldc_R4
        //                      || code == Code.Ldc_R8 || code == Code.Ldc_I8) ? i.Operand : (i.OpCode.Value - 22);

        //                    MakeLoadConstInst(proto, i, result, targetSlot, value, LdStComment);

        //                }
        //                else if (code == Code.Ldloc || code == Code.Ldloc_0 || code == Code.Ldloc_0 || code == Code.Ldloc_1 || code == Code.Ldloc_2 ||
        //                    code == Code.Ldloc_3 || code == Code.Ldloc_S /*|| code == Code.Ldloca || code == Code.Ldloca_S */ )
        //                {
        //                    VariableDefinition varInfo = null;
        //                    int loc;
        //                    if (code == Code.Ldloc_S || code == Code.Ldloc || code == Code.Ldloca || code == Code.Ldloca_S)
        //                    {
        //                        varInfo = i.Operand as VariableDefinition;
        //                        loc = varInfo.Index;
        //                    }
        //                    else
        //                    {
        //                        loc = i.OpCode.Value - 6;
        //                        if (loc > proto.maxCallStackSize)
        //                        {
        //                            proto.maxCallStackSize = loc;  //变更
        //                        }
        //                    }

        //                    fromSlot = proto.callStackStartIndex + loc;
        //                    result.Add(proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE,
        //                      "move %" + targetSlot + " %" + fromSlot + LdStComment, i));

        //                }
        //                else if (code == Code.Ldstr)
        //                {
        //                    var constValue = ILgroup.ILInstructions[0].Operand.ToString();
        //                    var literalValueInUvms = TranslatorUtils.Escape(constValue);
        //                    var valueIdx = proto.InternConstantValue(literalValueInUvms);
        //                    // 设置string 
        //                    MakeLoadConstInst(proto, i, result, targetSlot, constValue, LdStComment);

        //                }
        //                else if (code == Code.Ldnull)
        //                {
        //                    MakeLoadNilInst(proto, i, result, targetSlot, LdStComment);
        //                }
        //                else
        //                {
        //                    throw new Exception("TranslateILInstructionsGroup程序出错,错误的ILcode:" + code);
        //                }
        //                proto.AddNotMappedILInstruction(ILgroup.ILInstructions[1]); //添加
        //                break;
        //            }
        //        default:
        //            {
        //                throw new Exception("TranslateILInstructionsGroup程序出错,不支持的ILGroupCode:" + ILgroup.GroupCode);

        //            }
        //    }

        //    result.Add(proto.MakeEmptyInstruction(ILgroup.ILInstructions[0].ToString()));
        //    return result;
        //}


        private int GetTargetSlotIndex(UvmProto proto, Instruction i)
        {
            int targetSlot = -1;

            switch (i.OpCode.Code)
            {
                case Code.Stloc:
                case Code.Stloc_S:
                case Code.Stloc_0:
                case Code.Stloc_1:
                case Code.Stloc_2:
                case Code.Stloc_3:
                    {
                        // 从evaluation stack 弹出栈顶数据复制到call stack slot
                        // ->uvm. 取出eval stack的长度, 从eval stack的slot中弹出数据到栈顶，然后move到合适slot，然后弹出栈顶
                        int loc;
                        VariableDefinition varInfo = null;
                        if (i.OpCode.Code == Code.Stloc_S)
                        {
                            varInfo = i.Operand as VariableDefinition;
                            loc = varInfo.Index;
                        }
                        else
                        {
                            loc = i.OpCode.Value - 10;
                        }
                        if (loc > proto.maxCallStackSize)
                        {
                            proto.maxCallStackSize = loc;
                        }
                        targetSlot = proto.callStackStartIndex + loc;
                    }
                    break;
                case Code.Starg:
                case Code.Starg_S:
                    {
                        // 将位于计算堆栈顶部的值存储到位于指定索引的参数槽中
                        int argLoc;
                        ParameterDefinition argInfo = i.Operand as ParameterDefinition;
                        argLoc = argInfo.Index;
                        var argSlot = argLoc;
                        if (proto.method.HasThis)
                        {
                            argSlot++;
                        }
                        targetSlot = argSlot;
                    }
                    break;
                default:
                    {
                        throw new Exception("程序出错，只有st类型opcode指令才能GetTargetSlotIndex");
                    }
            }
            return targetSlot;
        }

  

        //------------------------reduce code imp----------------------------------
        private void checkSlot(string slot)
        {
            if (!slot.StartsWith("%"))
            {
                throw new Exception("error ReduceUvmInsts,invalid uvm inst:slot");
            }
        }

        private void checkConstStr(string str)
        {
            if (!str.StartsWith("const ")|| !str.EndsWith("\""))
            {
                throw new Exception("error ReduceUvmInsts,invalid uvm inst[" + str + "]");
            }
        }

        private int getLtCount(List<int> intlist, int inta)
        {
            int count = 0;
            foreach (var i in intlist)
            {
                if (i < inta) count++;
            }
            return count;
        }

        private void checkLoctionMoveRight(int originalLoc, int newLoc, UvmProto proto)
        {
            if (newLoc < originalLoc && newLoc >= 0)
            {
                int newIdx = 0;
                for (int j = 0; j < originalLoc; j++)
                {
                    if (!(proto.CodeInstructions[j] is UvmEmptyInstruction))
                    {
                        newIdx++;
                    }
                }
                if (proto.CodeInstructions[originalLoc].LocationLabel == null || newLoc != newIdx)
                {
                    throw new Exception("loc move error,please check!!!!");
                }
            }
        }


        private void ReduceProtoUvmInsts(UvmProto proto)
        {
            Console.Write("begin reduce: proto name = " + proto.Name + " totalLines = " + proto.CodeInstructions.Count() + "\n");
            int r = 0;
            int totalReduceLines = 0;
            int idx = 0;
            do
            {
                Console.WriteLine("idx=" + idx + " , reduce proto begin:" + proto.Name);
                r = ReduceUvmInstsImp(proto);
                totalReduceLines = totalReduceLines + r;
                idx++;
            } while (r > 0);

            Console.Write("proto name = " + proto.Name + " totalReduceLines = " + totalReduceLines + " , now totalLines = " + proto.CodeInstructions.Count() + "\n");
        }


    
        private int ReduceUvmInstsImp(UvmProto proto)
        {
            var notEmptyCodeInstructions = new List<UvmInstruction>();
            foreach (var codeInst in proto.CodeInstructions)
            {
                if (!(codeInst is UvmEmptyInstruction))
                {
                    notEmptyCodeInstructions.Add(codeInst);
                }
            }
            proto.CodeInstructions.Clear();
            foreach (var inst in notEmptyCodeInstructions)
            {
                proto.CodeInstructions.Add(inst);
            }

            var CodeInstructions = proto.CodeInstructions;
            List<int> delIndexes = new List<int>();
            List<int> modifyIndexes = new List<int>();
            List<UvmInstruction> modifyUvmIns = new List<UvmInstruction>();
            List<string> uvmJmpIns = new List<string>();
            
            var UvmInstCount = CodeInstructions.Count;
            string affectedSlot = "";
            string uvmInsstr = "";
            int commentIndex = -1;
            string commentPrefix = "";
            string constStr = "";

            for (int gIndex = 0; gIndex < UvmInstCount; gIndex++)
            {
                UvmInstruction uvmIns = CodeInstructions[gIndex];
                if (uvmIns.IlInstruction == null)
                {
                    continue;
                }

                uvmInsstr = uvmIns.ToString().Trim();
                commentIndex = uvmInsstr.IndexOf(";");
                if (commentIndex >= 0)
                {
                    commentPrefix = "" + uvmInsstr.Substring(commentIndex);
                    uvmInsstr = uvmInsstr.Substring(0, commentIndex);
                }
                else
                {
                    commentPrefix = "";
                }
                var ss = uvmInsstr.Split();


                UvmOpCodeEnums opcode1 = CodeInstructions[gIndex].OpCode.OpCodeValue;
                if((opcode1 == UvmOpCodeEnums.OP_PUSH) && (!CodeInstructions[gIndex].HasLocationLabel()))
                {
                    if (gIndex+1< UvmInstCount)
                    {
                        var ins2 = CodeInstructions[gIndex + 1];
                        if ((ins2.OpCode.OpCodeValue == UvmOpCodeEnums.OP_POP) && (!ins2.HasLocationLabel()))
                        {
                            var ssCount = ss.Count();
                            if ((ssCount == 2)&& ss[1].StartsWith("%"))
                            {
                                affectedSlot = ss[1];
                            }
                            else
                            {
                                affectedSlot = "";
                                var constidx = uvmInsstr.IndexOf("const");
                                if (constidx >= 0)
                                {
                                    constStr = uvmInsstr.Substring(constidx);
                                    checkConstStr(constStr); 
                                }
                                else
                                {
                                    throw new Exception("error ReduceUvmInsts,invalid uvm inst:" + uvmInsstr);
                                } 
                                
                            }


                            var uvmInsstr2 = ins2.ToString().Trim();
                            var commentIndex2 = uvmInsstr2.IndexOf(";");
                         
                            if (commentIndex2 >= 0)
                            {
                                commentPrefix = commentPrefix + "," + uvmInsstr2.Substring(commentIndex2);
                                uvmInsstr2 = uvmInsstr2.Substring(0, commentIndex2);
                            }


                            
                            var ss2 = uvmInsstr2.Split();
                            var targetSlot = ss2[1];

                            if (!targetSlot.StartsWith("%"))
                            {
                                throw new Exception("error ReduceUvmInsts,invalid uvm inst:" + uvmInsstr2);
                            }
    

                            UvmInstruction inst;
                            if (affectedSlot.StartsWith("%"))
                            {
                                inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_MOVE, "move " + targetSlot + " " + affectedSlot + commentPrefix + ";pushpop slot", uvmIns.IlInstruction);
                            }
                            else if (constStr.StartsWith("const "))
                            {
                                inst = proto.MakeInstructionLine(UvmOpCodeEnums.OP_LOADK, "loadk " + targetSlot + " " + constStr + commentPrefix + ";pushpop slot", uvmIns.IlInstruction);
                            }
                            else
                            {
                                throw new Exception("error ReduceUvmInsts,invalid uvm inst:" + uvmInsstr);
                            }

                            delIndexes.Add(gIndex);
                            modifyIndexes.Add(gIndex+1);
                            modifyUvmIns.Add(inst);
                            Console.Write("find push-pop evaltop group, index " + gIndex + "," + (gIndex+1) + "\n");
                        }
                    }
                }
                
            }
            int delcount = delIndexes.Count();

            foreach (var mi in modifyIndexes)
            {
                proto.CodeInstructions[mi] = modifyUvmIns[modifyIndexes.IndexOf(mi)];
            }


            //删除delindex对应的指令
            if (delcount > 0)
            {
                for (int j = 0; j < delcount; j++)
                {
                    var instr = proto.CodeInstructions[delIndexes[j]].ToString();
                    proto.CodeInstructions[delIndexes[j]] = proto.MakeEmptyInstruction(";deleted inst;original inst:" + instr);
                }
            }

            //调整location map
            int needLocCount = proto.NeededLocationsMap.Count();
            if (needLocCount > 0 && delcount > 0)
            {
                Dictionary<int, string> locationMap = new Dictionary<int, string>();
                foreach (var loc in proto.NeededLocationsMap)
                {
                    var newKey = loc.Key - getLtCount(delIndexes, loc.Key);
                    checkLoctionMoveRight(loc.Key, newKey, proto);
                    locationMap.Add(newKey, loc.Value);
                }

                proto.NeededLocationsMap.Clear();
                foreach (var loc in locationMap)
                {
                    proto.NeededLocationsMap.Add(loc.Key, loc.Value);
                }
            }
            Console.Write("reduce codeslines =" + delIndexes.Count() + "\n");
            return delcount;
        }



    }
}
