using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gsharpc
{
    public enum
            UvmOpCodeEnums
    {
        /*----------------------------------------------------------------------
        name		args	description
        ------------------------------------------------------------------------*/
        OP_MOVE = 0, /*	A B	R(A) := R(B)					*/
        OP_LOADK, /*	A Bx	R(A) := Kst(Bx)					*/
        OP_LOADKX, /*	A 	R(A) := Kst(extra arg)				*/
        OP_LOADBOOL, /*	A B C	R(A) := (Bool)B; if (C) pc++			*/
        OP_LOADNIL, /*	A B	R(A), R(A+1), ..., R(A+B) := nil		*/
        OP_GETUPVAL, /*	A B	R(A) := UpValue[B]				*/

        OP_GETTABUP, /*	A B C	R(A) := UpValue[B][RK(C)]			*/
        OP_GETTABLE, /*	A B C	R(A) := R(B)[RK(C)]				*/

        OP_SETTABUP, /*	A B C	UpValue[A][RK(B)] := RK(C)			*/
        OP_SETUPVAL, /*	A B	UpValue[B] := R(A)				*/
        OP_SETTABLE, /*	A B C	R(A)[RK(B)] := RK(C)				*/

        OP_NEWTABLE, /*	A B C	R(A) := {} (size = B,C)				*/

        OP_SELF, /*	A B C	R(A+1) := R(B); R(A) := R(B)[RK(C)]		*/

        OP_ADD, /*	A B C	R(A) := RK(B) + RK(C)				*/
        OP_SUB, /*	A B C	R(A) := RK(B) - RK(C)				*/
        OP_MUL, /*	A B C	R(A) := RK(B) * RK(C)				*/
        OP_MOD, /*	A B C	R(A) := RK(B) % RK(C)				*/
        OP_POW, /*	A B C	R(A) := RK(B) ^ RK(C)				*/
        OP_DIV, /*	A B C	R(A) := RK(B) / RK(C)				*/
        OP_IDIV, /*	A B C	R(A) := RK(B) // RK(C)				*/
        OP_BAND, /*	A B C	R(A) := RK(B) & RK(C)				*/
        OP_BOR, /*	A B C	R(A) := RK(B) | RK(C)				*/
        OP_BXOR, /*	A B C	R(A) := RK(B) ~ RK(C)				*/
        OP_SHL, /*	A B C	R(A) := RK(B) << RK(C)				*/
        OP_SHR, /*	A B C	R(A) := RK(B) >> RK(C)				*/
        OP_UNM, /*	A B	R(A) := -R(B)					*/
        OP_BNOT, /*	A B	R(A) := ~R(B)					*/
        OP_NOT, /*	A B	R(A) := not R(B)				*/
        OP_LEN, /*	A B	R(A) := length of R(B)				*/

        OP_CONCAT, /*	A B C	R(A) := R(B).. ... ..R(C)			*/

        OP_JMP, /*	A sBx	pc+=sBx; if (A) close all upvalues >= R(A - 1)	*/
        OP_EQ, /*	A B C	if ((RK(B) == RK(C)) ~= A) then pc++		*/
        OP_LT, /*	A B C	if ((RK(B) <  RK(C)) ~= A) then pc++		*/
        OP_LE, /*	A B C	if ((RK(B) <= RK(C)) ~= A) then pc++		*/

        OP_TEST, /*	A C	if not (R(A) <=> C) then pc++			*/
        OP_TESTSET, /*	A B C	if (R(B) <=> C) then R(A) := R(B) else pc++	*/

        OP_CALL, /*	A B C	R(A), ... ,R(A+C-2) := R(A)(R(A+1), ... ,R(A+B-1)) */
        OP_TAILCALL, /*	A B C	return R(A)(R(A+1), ... ,R(A+B-1))		*/
        OP_RETURN, /*	A B	return R(A), ... ,R(A+B-2)	(see note)	*/

        OP_FORLOOP, /*	A sBx	R(A)+=R(A+2);
                if R(A) <?= R(A+1) then { pc+=sBx; R(A+3)=R(A) }*/
        OP_FORPREP, /*	A sBx	R(A)-=R(A+2); pc+=sBx				*/

        OP_TFORCALL, /*	A C	R(A+3), ... ,R(A+2+C) := R(A)(R(A+1), R(A+2));	*/
        OP_TFORLOOP, /*	A sBx	if R(A+1) ~= nil then { R(A)=R(A+1); pc += sBx }*/

        OP_SETLIST, /*	A B C	R(A)[(C-1)*FPF+i] := R(A+i), 1 <= i <= B	*/

        OP_CLOSURE, /*	A Bx	R(A) := closure(KPROTO[Bx])			*/

        OP_VARARG, /*	A B	R(A), R(A+1), ..., R(A+B-2) = vararg		*/

        OP_EXTRAARG, /*	Ax	extra (larger) argument for previous opcode	*/
        OP_PUSH, /*	add 		*/
        OP_POP, /*	add 	*/
        OP_GETTOP, /*	add 		*/

        OP_CMP,  /* A B C   if((R(B)>R(C))then{R(A):=1;}else if((R(B)==R(C))then{R(A):=0;}else if((R(B)<R(C))then{R(A):=-1;}*/
        OP_CMP_EQ,  /* A B C   */
        OP_CMP_NE,  /* A B C   */
        OP_CMP_GT,  /* A B C   */
        OP_CMP_LT  /* A B C   */


    }

    /*===========================================================================
  We assume that instructions are unsigned numbers.
  All instructions have an opcode in the first 6 bits.
  Instructions can have the following fields:
  'A' : 8 bits
  'B' : 9 bits
  'C' : 9 bits
  'Ax' : 26 bits ('A', 'B', and 'C' together)
  'Bx' : 18 bits ('B' and 'C' together)
  'sBx' : signed Bx

  A signed argument is represented in excess K; that is, the number
  value is the unsigned value minus K. K is exactly the maximum value
  for that argument (so that -max is represented by 0, and +max is
  represented by 2*max), which is half the maximum for the corresponding
  unsigned argument.
  ===========================================================================*/
    public enum OpMode { iABC, iABx, iAsBx, iAx } /* basic instruction format */

    public class UvmOpCode
    {
        public static IList<string> OpcodeNames = new List<string>()
        {
            "MOVE",
            "LOADK",
            "LOADKX",
            "LOADBOOL",
            "LOADNIL",
            "GETUPVAL",
            "GETTABUP",
            "GETTABLE",
            "SETTABUP",
            "SETUPVAL",
            "SETTABLE",
            "NEWTABLE",
            "SELF",
            "ADD",
            "SUB",
            "MUL",
            "MOD",
            "POW",
            "DIV",
            "IDIV",
            "BAND",
            "BOR",
            "BXOR",
            "SHL",
            "SHR",
            "UNM",
            "BNOT",
            "NOT",
            "LEN",
            "CONCAT",
            "JMP",
            "EQ",
            "LT",
            "LE",
            "TEST",
            "TESTSET",
            "CALL",
            "TAILCALL",
            "RETURN",
            "FORLOOP",
            "FORPREP",
            "TFORCALL",
            "TFORLOOP",
            "SETLIST",
            "CLOSURE",
            "VARARG",
            "EXTRAARG",
            //add
            "PUSH",
            "POP",
            "GETTOP",
            "CMP",
            "CMP_EQ",
            "CMP_NE",
            "CMP_GT",
            "CMP_LT"

        };
        public UvmOpCodeEnums OpCodeValue { get; set; }
    }
}
