using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gsharpc
{
    public enum StorageValueTypes
    {
        storage_value_null = 0,
        storage_value_int = 1,
        storage_value_number = 2,
        storage_value_bool = 3,
        storage_value_string = 4,
        storage_value_stream = 5,

        storage_value_unknown_table = 50,
        storage_value_int_table = 51,
        storage_value_number_table = 52,
        storage_value_bool_table = 53,
        storage_value_string_table = 54,
        storage_value_stream_table = 55,

        storage_value_unknown_array = 100,
        storage_value_int_array = 101,
        storage_value_number_array = 102,
        storage_value_bool_array = 103,
        storage_value_string_array = 104,
        storage_value_stream_array = 105,

        storage_value_userdata = 201,
        storage_value_not_support = 202

    }
}
