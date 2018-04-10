using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UvmCoreLib
{
    // 如果合约中需要emit event，实现一个类继承这个接口，类中的static void EmitXXXX形式，且有且只有一个字符串参数的方法会被当做uvm中的emit event
    public interface IUvmEventEmitter
    {

    }
}
