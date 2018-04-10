using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gsharpc
{
  public enum UvmTypeInfoEnum
  {
    LTI_OBJECT = 0,
    LTI_NIL = 1,
    LTI_STRING = 2,
    LTI_INT = 3,
    LTI_NUMBER = 4,
    LTI_BOOL = 5,
    LTI_TABLE = 6,
    LTI_FUNCTION = 7, // coroutine as function type
    LTI_UNION = 8,
    LTI_RECORD = 9, // 新语法, type <RecordName> = { <name> : <type> , ... }
    LTI_GENERIC = 10, // 新语法，泛型类型
    LTI_ARRAY = 11, // 新语法，列表类型
    LTI_MAP = 12, // 新语法，单纯的哈希表，并且key类型只能是字符串
    LTI_LITERIAL_TYPE = 13, // 新语法，literal type 字符串/数字/布尔值的字面值的union的类型，比如: "Male" | "Female"
    LTI_STREAM = 14, // Stream类型，没有直接的字面量
    LTI_UNDEFINED = 100 // 未定义值，类似undefined
  }
}
