using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

/**
  * 实现一些uvm中内置函数和内置模块
  */
namespace UvmCoreLib
{

    /**
     * 实现uvm内置全局函数
     */
    public class UvmCoreFuncs
    {

        public static bool and(bool a, bool b)
        {
            Console.WriteLine("bool and");
            return a && b;
        }

        public static bool or(bool a, bool b)
        {
            Console.WriteLine("bool or");
            return a || b;
        }

        /**
         * 浮点除法
         */
        public static float div(float a, float b)
        {
            return a / b;
        }

        public static float div(int a, int b)
        {
            return a * 1.0f / b;
        }

        public static float div(float a, int b)
        {
            return a / b;
        }
        public static float div(int a, float b)
        {
            return a * 1.0f / b;
        }

        /**
         * 整除
         */
        public static int idiv(int a, int b)
        {
            return (int)(a / b);
        }
        public static int idiv(float a, float b)
        {
            return (int)(a / b);
        }
        public static int idiv(float a, int b)
        {
            return (int)(a / b);
        }
        public static int idiv(int a, float b)
        {
            return (int)(a / b);
        }

        /**
         * 取相反数
         */
        public static int neg(int a)
        {
            return -a;
        }
        public static float neg(float a)
        {
            return -1;
        }

        /**
         * 布尔取反
         */
        public static bool not(bool a)
        {
            return !a;
        }

        public static void print(object obj)
        {
            Console.WriteLine(obj);
        }
        public static string tostring(object obj)
        {
            return obj != null ? obj.ToString() : "nil";
        }
        public static string tojsonstring(object obj)
        {
            if (obj == null)
            {
                return "nil";
            }
            else
            {
                return JsonConvert.SerializeObject(obj);
            }
        }
        public static void pprint(object obj)
        {
            Console.WriteLine(tojsonstring(obj));
        }


        public static long tointeger(object obj)
        {
            if (obj == null)
            {
                Error("tointeger error ");
            }
            if ((obj is long)|| (obj is int))
            {
                return (long)obj;
            }
            long result = 0;
            if (!long.TryParse(obj.ToString(), out result))
            {
                Error("tointeger error ");
            }
            return result;
        }

        public static float tonumber(object obj)
        {
            if (obj == null)
            {
                Console.WriteLine("tonumber error ");
                return 0.0F;
            }
            if (obj is float)
            {
                return (float)obj;
            }
            float result = 0;
            if (!float.TryParse(obj.ToString(), out result))
            {
                Console.WriteLine("tonumber error ");
                return 0.0F;
            }
            return result;
        }

        public static T importContract<T>(string contractName) where T : new()
        {
            return new T();
        }

        public static T importContractFromAddress<T>(string contractAddr) where T : new()
        {
            return new T();
        }

        public static T importModule<T>(string moduleName) where T : new()
        {
            var innerModules = new List<string>() {
      "string", "table", "math", "time", "json", "os", "net", "http", "jsonrpc"
      };
            if (innerModules.Contains(moduleName))
            {
                return new T();
            }
            else
            {
                throw new Exception("not supported module " + moduleName);
            }
        }

        public static void Debug()
        {
            // do nothing, the gsharpc will generate debug info for printing eval stack
        }

        public static Dictionary<string, string> GlobalFuncsMapping = new Dictionary<string, string>()
    {
      { "Type", "type" },
      { "Exit", "exit" },
      { "Error", "error" }
    };

        public static string Type(object value)
        {
            if (value == null)
            {
                return "nil";
            }
            else if (value is int || value is long || value is float || value is double)
            {
                return "number";
            }
            else if (value is bool)
            {
                return "boolean";
            }
            else if (value is UvmTable)
            {
                return "table";
            }
            else
            {
                return "object"; // TODO: "function" type
            }
        }

        public static void Exit(int exitCode)
        {
            Console.WriteLine("exit with code " + exitCode);
            throw new Exception("application exited with code " + exitCode);
        }

        public static void Error(string errorMsg)
        {
            Console.WriteLine("error with message " + errorMsg);
            throw new Exception("application exit with error " + errorMsg);
        }

        // getmetatable
        public static UvmMap<object> getmetatable(UvmTable table)
        {
            return UvmMap<object>.Create(); // TODO: 模拟metatable
        }
        // setmetatable
        public static void setmetatable(UvmTable table, UvmTable metatable)
        {
            // TODO: 模拟metatable
        }
        // toboolean
        public static bool toboolean(object value)
        {
            if (value == null)
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            return true;
        }
        // totable
        public static UvmTable totable(object value)
        {
            if (value == null)
            {
                return null;
            }
            if (value is UvmTable)
            {
                return value as UvmTable;
            }
            return null;
        }
        // rawequal
        public static bool rawequal(object a, object b)
        {
            return a == b;
        }
        // rawlen
        public static long rawlen(object value)
        {
            if (value == null)
            {
                return 0;
            }
            if (value is UvmArray<object>)
            {
                return (value as UvmArray<object>).Count();
            }
            return 0;
        }
        // rawget
        public static object rawget(object table, object key)
        {
            if (table == null || key == null)
            {
                return null;
            }
            if (table is UvmMap<object>)
            {
                return (table as UvmMap<object>).Get(key.ToString());
            }
            if (table is UvmArray<object> && (key is int || key is long))
            {
                return (table as UvmArray<object>).Get((int)key);
            }
            return null;
        }
        // rawset
        public static void rawset(object table, object key, object value)
        {
            if (value == null || key == null)
            {
                return;
            }
            if (table is UvmMap<object>)
            {
                (table as UvmMap<object>).Set(key.ToString(), value);
                return;
            }
            if (table is UvmArray<object> && (key is int || key is long))
            {
                (table as UvmArray<object>).Set((int)key, value);
                return;
            }
        }
        // select

        // transfer_from_contract_to_address
        public static int transfer_from_contract_to_address(string address,
          string assetName, long amount)
        {
            Console.WriteLine("this is C# mock of transfer_from_contract_to_address " + address + " " + (amount * 1.0 / 100000) + assetName);
            return 0;
        }

        private static Dictionary<string, long> _cacheOfContractBalanceMock = new Dictionary<string, long>();

        /**
         * 模拟修改合约的余额，用来在C#调试中使用。这个函数的调用实际不会调用，但是还是会产生几行字节码，所以建议上链前注释掉
         */
        public static void set_mock_contract_balance_amount(string contractAddress, string assetName, long amount)
        {
            string key = contractAddress + "$" + assetName;
            _cacheOfContractBalanceMock[key] = amount;
        }

        // get_contract_balance_amount
        public static long get_contract_balance_amount(string contractAddress, string assetName)
        {
            Console.WriteLine("this is a C# mock of get_contract_balance_amount, contract: " + contractAddress + ", asset name: " + assetName);
            string key = contractAddress + "$" + assetName;
            if (_cacheOfContractBalanceMock.ContainsKey(key))
            {
                return _cacheOfContractBalanceMock[key];
            }
            else
            {
                return 0;
            }
        }
        // get_chain_now
        public static long get_chain_now()
        {
            Console.WriteLine("this is a C# mock of get_chain_now");
            return (long)new TimeSpan().TotalSeconds;
        }
        // get_chain_random
        public static long get_chain_random()
        {
            Console.WriteLine("this is a C# mock of get_chain_random");
            return new Random().Next(10000000);
        }
        // get_header_block_num
        public static long get_header_block_num()
        {
            Console.WriteLine("this is a C# mock of get_header_block_num");
            return 10086; // this is mock value
        }
        // get_waited
        public static long get_waited(long num)
        {
            Console.WriteLine("this is a C# mock of get_waited");
            return 10086; // this is mock value
        }
        // get_current_contract_address
        public static string get_current_contract_address()
        {
            return "mock_dotnet_contract_address"; // this is mock value
        }
        // caller
        public static string caller()
        {
            return "mock_dotnet_caller";
        }
        // caller_address
        public static string caller_address()
        {
            return "mock_dotnet_caller_address";
        }
        // get_transaction_fee
        public static long get_transaction_fee()
        {
            Console.WriteLine("this is a C# mock of get_transaction_fee");
            return 1; // this is mock value
        }
        // transfer_from_contract_to_public_account
        public static long transfer_from_contract_to_public_account(string to_account_name, string assertName,
          long amount)
        {
            Console.WriteLine("this is a C# mock of transfer_from_contract_to_public_account," +
              " " + (amount * 1.0 / 100000) + assertName + " to account " + to_account_name);
            return 0;
        }

        public static bool is_valid_address(string addr)
        {
            Console.WriteLine("this is a C# mock of is_valid_address");
            return true;
        }

        public static bool is_valid_contract_address(string addr)
        {
            Console.WriteLine("this is a C# mock of is_valid_contract_address");
            return true;
        }

        public static string get_prev_call_frame_contract_address()
        {
            Console.WriteLine("this is a C# mock of get_prev_call_frame_contract_address");
            return "prev_frame";
        }

        private static string _prev_call_frame_api_name_for_mock = "test";

        public static void set_prev_call_frame_api_name_for_mock(string name)
        {
            _prev_call_frame_api_name_for_mock = name;
        }


        public static string get_prev_call_frame_api_name()
        {
            return _prev_call_frame_api_name_for_mock;
        }

        private static int _contract_call_frame_stack_size_for_mock = 1;

        public static void set_contract_call_frame_stack_size_for_mock(int size)
        {
            _contract_call_frame_stack_size_for_mock = size;
        }

        public static int get_contract_call_frame_stack_size()
        {
            return _contract_call_frame_stack_size_for_mock;
        }


        public static string get_system_asset_symbol()
        {
            return "TEST";
        }

        private static int _system_asset_precision_for_mock = 8;

        public static void set_system_asset_precision_for_mock(int precision)
        {
            _system_asset_precision_for_mock = precision;
        }

        public static int get_system_asset_precision()
        {
            return _system_asset_precision_for_mock;
        }


        public static object fast_map_get(string storagename,string key)
        {   
            return new object();
        }


        public static void fast_map_set(string storagename, string key,object value)
        {
            
        }


        public static object call_contract_api(string contract_address, string api_name, object arg)
        {
            return new object();
        }

        public static object static_call_contract_api(string contract_address, string api_name, object arg)
        {
            return new object();
        }

        //return table: [result,code]
        public static UvmArray<object> send_message(string contract_address, string api_name, UvmArray<object> args)
        {
            return UvmArray<object>.Create();
        }


    }
}
