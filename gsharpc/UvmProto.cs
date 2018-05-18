using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace gsharpc
{

    public class UvmLocVar
    {
        public string Name { get; set; }
        public int SlotIndex { get; set; }
    }

    public class UvmUpvaldesc
    {
        public string Name { get; set; }  /* upvalue name (for debug information) */
        public bool Instack { get; set; }  /* whether it is in stack (register) */
        public int Idx { get; set; }  /* index of upvalue (in stack or in outer function's list) */
    }

    public class UvmProto
    {
        public string Name { get; set; } /* name of proto */

        public int Numparams { get; set; } /* number of fixed parameters */
        public bool IsVararg { get; set; } /* 2: declared vararg; 1: uses vararg */
        public int MaxStackSize { get; set; } /* number of registers needed by this function */

        public int SizeUpvalues { get; set; }  /* size of 'upvalues' */
        public int SizeK { get; set; }  /* size of 'k' */
        public int SizeCode { get; set; }
        public int SizeLineinfo { get; set; }
        public int SizeP { get; set; }  /* size of 'p' */
        public int SizeLocVars { get; set; }
        public int LineDefined { get; set; }  /* debug information  */
        public int LastLineDefined { get; set; }  /* debug information  */
        public IList<object> ConstantValues { get; set; }  /* constants used by the function */
        public IList<UvmInstruction> CodeInstructions { get; set; }  /* opcodes */
        public IList<UvmProto> SubProtos;  /* functions defined inside the function */
        public IList<int> Lineinfo { get; set; }  /* map from opcodes to source lines (debug information) */
        public IList<UvmLocVar> Locvars { get; set; }  /* information about local variables (debug information) */
        public IList<UvmUpvaldesc> Upvalues { get; set; }  /* upvalue information */
        public string Source { get; set; }  /* used for debug information */

        // .net IL指令到uvm指令的映射
        public Dictionary<Instruction, UvmInstruction> IlInstructionsToUvmInstructionsMap { get; set; }

        public Dictionary<int, string> NeededLocationsMap { get; set; }

        public UvmProto Parent { get; set; }

        // translating states
        public int paramsStartIndex { get; set; }
        //public int evalStackIndex { get; set; }
        //public int evalStackSizeIndex { get; set; }
        public int tmp1StackTopSlotIndex { get; set; }
        public int tmp2StackTopSlotIndex { get; set; }
        public int tmp3StackTopSlotIndex { get; set; }
        public int tmpMaxStackTopSlotIndex { get; set; }
        //public int callStackStartIndex { get; set; }
        //public int maxCallStackSize { get; set; }
        public MethodDefinition method { get; set; }

        public bool InNotAffectMode { get; set; } // 是否处于proto数据不受影响的模式（调用proto函数不会改变proto状态的模式，伪装纯函数）

        public IList<Instruction> NotMappedILInstructions { get; set; } // 没有映射到uvm instruction的.Net IL的nop等指令的列表，为了将每条IL指令关联到uvm指令方便跳转查找，对于不产生uvm instructions的IL指令，加入这个队列等待下一个有效非空uvm instruction一起映射关联

        public UvmProto(string name = null)
        {
            Name = name != null ? name : ("tmp_" + protoNameIncrementor++);
            ConstantValues = new List<object>();
            CodeInstructions = new List<UvmInstruction>();
            SubProtos = new List<UvmProto>();
            Lineinfo = new List<int>();
            Locvars = new List<UvmLocVar>();
            Upvalues = new List<UvmUpvaldesc>();
            Source = "";
            IlInstructionsToUvmInstructionsMap = new Dictionary<Instruction, UvmInstruction>();
            NeededLocationsMap = new Dictionary<int, string>();
            InNotAffectMode = false;
            NotMappedILInstructions = new List<Instruction>();
        }

        private static int protoNameIncrementor = 0;

        public string ToUvmAss(bool isTop = false)
        {
            var builder = new StringBuilder();

            // 如果是顶部proto，增加.upvalues num
            if (isTop)
            {
                builder.Append(".upvalues " + Upvalues.Count + "\r\n");
            }
            if (isTop)
            {
                Name = "main";
            }
            builder.Append(".func " + Name + " " + MaxStackSize + " " + Numparams + " " + SizeLocVars + "\r\n");

            builder.Append(".begin_const\r\n");
            foreach (object value in ConstantValues)
            {
                builder.Append("\t");
                if (value == null)
                {
                    builder.Append("nil\r\n");
                }
                if (value is string)
                {
                    builder.Append("\"" + (string)value + "\"\r\n");
                }
                else if (value is int || value is Int64 || value is double || value is sbyte)
                {
                    builder.Append(value.ToString() + "\r\n");
                }
                else if (value is Boolean)
                {
                    builder.Append((((bool)value) ? "true" : "false") + "\r\n");
                }
                else
                {
                    builder.Append(value.ToString() + "\r\n");
                }
            }
            builder.Append(".end_const\r\n");

            builder.Append(".begin_upvalue\r\n");
            foreach (var upvalue in Upvalues)
            {
                builder.Append("\t" + (upvalue.Instack ? 1 : 0) + " " + upvalue.Idx + "\r\n");
            }
            builder.Append(".end_upvalue\r\n");

            builder.Append(".begin_local\r\n");
            SizeCode = CodeInstructions.Count;
            foreach (var local in Locvars)
            {
                builder.Append("\t" + "\"" +local.Name + "\"" + " 1 " + SizeCode + "\r\n");
            }
            builder.Append(".end_local\r\n");

            builder.Append(".begin_code\r\n");
            foreach (var inst in CodeInstructions)
            {
                if (inst.HasLocationLabel())
                {
                    builder.Append(inst.LocationLabel + ":\r\n");
                }
                builder.Append("\t");
                builder.Append(inst.ToString());
                builder.Append("\r\n");
            }
            builder.Append(".end_code\r\n");

            foreach (var subProto in SubProtos)
            {
                builder.Append("\r\n");
                builder.Append(subProto.ToUvmAss(false));
            }
            builder.Append("\r\n");
            return builder.ToString();
        }

        public override string ToString()
        {
            return ToUvmAss();
        }

        public UvmProto FindMainProto()
        {
            if (method != null && method.Name.Equals("Main"))
            {
                return this;
            }
            foreach (var proto in SubProtos)
            {
                var found = proto.FindMainProto();
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        public UvmLocVar FindLocvar(string name)
        {
            foreach (var locvar in Locvars)
            {
                if (locvar.Name.Equals(name))
                {
                    return locvar;
                }
            }
            return null;
        }

        // 在proto的常量池中找常量的索引，从0开始，如果常量没在proto常量池，就加入常量池再返回索引
        public int InternConstantValue(object value)
        {
            if (value == null)
            {
                throw new Exception("Can't put null in constant pool");
            }
            if (InNotAffectMode)
            {
                return 0;
            }
            if (!ConstantValues.Contains(value))
            {
                ConstantValues.Add(value);
            }
            return ConstantValues.IndexOf(value);
        }

        public int InternUpvalue(string upvalueName)
        {
            if (upvalueName == null || upvalueName.Length < 1)
            {
                throw new Exception("upvalue名称不能为空");
            }
            if (InNotAffectMode)
            {
                return 0;
            }

            for (var i = 0; i < Upvalues.Count; i++)
            {
                var upvalueItem = Upvalues[i];
                if (upvalueItem.Name == upvalueName)
                {
                    return i;
                }
            }

            var upvalue = new UvmUpvaldesc();
            upvalue.Name = upvalueName;

            // 从上级proto中查找是否存在对应的localvars，判断instack的值
            if (Parent != null)
            {
                var locvar = Parent.FindLocvar(upvalueName);
                if (locvar != null)
                {
                    upvalue.Instack = true;
                    upvalue.Name = upvalueName;
                    upvalue.Idx = locvar.SlotIndex;
                }
            }
            else
            {
                upvalue.Instack = false;
                upvalue.Name = upvalueName;
            }
            if (!upvalue.Instack)
            {
                // 在上层proto的upvalues中找，没有找到就返回上层的 count(upvalues),因为上层proto需要增加新upvalue
                if (Parent == null)
                {
                    upvalue.Idx = Upvalues.Count;
                    upvalue.Instack = true;
                }
                else
                {
                    var parentUpvalueIndex = Parent.InternUpvalue(upvalueName);
                    upvalue.Idx = parentUpvalueIndex;
                    upvalue.Instack = false;
                }
            }
            Upvalues.Add(upvalue);
            return Upvalues.Count - 1;
        }

        public void AddNotMappedILInstruction(Instruction i)
        {
            if (InNotAffectMode)
            {
                return;
            }
            NotMappedILInstructions.Add(i);
        }

        public void AddInstruction(UvmInstruction inst)
        {
            this.CodeInstructions.Add(inst);
            if (inst.IlInstruction != null && !(inst is UvmEmptyInstruction))
            {
                if (!IlInstructionsToUvmInstructionsMap.ContainsKey(inst.IlInstruction))
                {
                    IlInstructionsToUvmInstructionsMap[inst.IlInstruction] = inst;
                }
                if (NotMappedILInstructions.Count > 0)
                {
                    foreach (var notMappedItem in NotMappedILInstructions)
                    {
                        IlInstructionsToUvmInstructionsMap[notMappedItem] = inst;
                    }
                    NotMappedILInstructions.Clear();
                }
            }
        }

        public UvmInstruction FindUvmInstructionMappedByIlInstruction(Instruction ilInstruction)
        {
            if (IlInstructionsToUvmInstructionsMap.ContainsKey(ilInstruction))
            {
                return IlInstructionsToUvmInstructionsMap[ilInstruction];
            }
            else
            {
                return null;
            }
        }

        public IList<UvmInstruction> NotEmptyCodeInstructions()
        {
            var notEmptyInsts = new List<UvmInstruction>();
            foreach (var codeInst in CodeInstructions)
            {
                if (!(codeInst is UvmEmptyInstruction))
                {
                    notEmptyInsts.Add(codeInst);
                }
            }
            return notEmptyInsts;
        }

        public int IndexOfUvmInstruction(UvmInstruction inst)
        {
            if (inst == null)
            {
                return -1;
            }
            return NotEmptyCodeInstructions().IndexOf(inst);
        }

        public void AddInstructionLine(UvmOpCodeEnums opCode, string line, Instruction ilInstruction)
        {
            var inst = UvmInstruction.Create(opCode, ilInstruction);
            inst.AsmLine = line;
            AddInstruction(inst);
        }

        public UvmInstruction MakeInstructionLine(UvmOpCodeEnums opCode, string line, Instruction ilInstruction)
        {
            var inst = UvmInstruction.Create(opCode, ilInstruction);
            inst.AsmLine = line;
            return inst;
        }

        public void AddEmptyInstruction(string comment)
        {
            AddInstruction(UvmInstruction.CreateEmpty(comment));
        }

        public UvmInstruction MakeEmptyInstruction(string comment)
        {
            return UvmInstruction.CreateEmpty(comment);
        }

        /**
         * 如果已经存在这个loc对应的label，直接复用，否则用参数的label构造
         */
        public string InternNeedLocationLabel(int loc, string label)
        {
            if (InNotAffectMode)
            {
                return "";
            }
            if (NeededLocationsMap.ContainsKey(loc))
            {
                return NeededLocationsMap[loc];
            }
            else
            {
                NeededLocationsMap[loc] = label;
                return label;
            }
        }

    }
}
