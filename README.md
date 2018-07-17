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

# Build

* install .netcore 2.1+
* `dotnet build -r win10-x64` or `dotnet build -r ubuntu16.10-x64` or `dotnet build -r osx.10.11-x64`

# Publish
* `dotnet publish -r win10-x64` or `dotnet publish -r ubuntu16.10-x64` or `dotnet publish -r osx.10.11-x64`