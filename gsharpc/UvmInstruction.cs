using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil.Cil;

namespace gsharpc
{
    public class UvmOpArg
    {
        public int Value { get; set; } // opArg的值
        public uint BitCount { get; set; } // 占多少个bit
    }
    public class UvmInstruction
    {
        public UvmInstruction(UvmOpCode opCode, Instruction ilInstruction)
        {
            this.OpCode = opCode;
            OpArgs = new List<UvmOpArg>();
            AsmLine = "";
            LineInSource = 0;
            this.IlInstruction = ilInstruction;
        }

        public static UvmInstruction Create(UvmOpCodeEnums opCodeEnum, Instruction ilInstruction)
        {
            return new UvmInstruction(new UvmOpCode() {OpCodeValue = opCodeEnum}, ilInstruction);
        }

        public static UvmInstruction CreateEmpty(string comment)
        {
            return new UvmEmptyInstruction(comment);
        }

        public UvmOpCode OpCode { get; set; }
        public IList<UvmOpArg> OpArgs { get; set; }

        public uint LineInSource { get; set; }

        public Instruction IlInstruction;

        public string LocationLabel { get; set; }

        public void AddOpArg(int value, uint bitCount)
        {
            OpArgs.Add(new UvmOpArg() {Value = value, BitCount = bitCount});
        }

        public string AsmLine { get; set; } // 伪汇编代码的一行指令, 用来根据这指令生成最终的uvm字节码

        public bool HasLocationLabel()
        {
            return LocationLabel != null && LocationLabel.Length > 0;
        }
        public override string ToString()
        {
          return AsmLine;
        }
  }

    public class UvmEmptyInstruction : UvmInstruction
    {
        public string Comment { get; set; }
        public UvmEmptyInstruction(string comment): base(new UvmOpCode(), null)
        {
            this.Comment = comment;
        }

        public override string ToString()
        {
            return "";
        }
    }
}
