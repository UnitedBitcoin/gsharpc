gsharpc
=============

A compiler using to compile .Net bytecode(.dll) to uvm/lua bytecode

# Features

* Many core .Net features/C# syntax support
* base types, user defined C# types, Map, Array, etc. types support
* operators, control flows(if, for, while, break, continue, etc.)
* function call, function define, function return, function parameters
* interate Array and Map
* etc.


# Usage

* use csc.exe to compile C# file to .dll file(.Net bytecode file)
* use this program to compile .dll file to xxxx.uvms file, xxxx.uvms is the output uvm assembler file
* `gsharpc xxxx.dll` to compile .net .dll file to uvm bytecode
* `uvm xxxx.out` to run compiled bytecode file
* `package_gpc xxxx.out xxxx.meta.json` to package the bytecode and meta info file to .gpc file
* you can also use `gsharpc --gpc xxxx.dll` to directly compile .net .dll file to .gpc file

# TODO

* Main函数调用的时候如果不是static，传入this对象
* 合并重复无用指令(IL翻译到uvm指令时的重复压栈入栈操作等)
* Conv, Switch, loca等更多IL指令的支持(不必要，另外有一些.Net指令没有C#中语法，也暂不支持)
* 现有的C#类型中构造函数没有合并到产生的uvm自定义类型的构造函数中
* 整理重构代码
* Release编译C#项目情况下，一些地方比如pairs处理会出BUG